using System;
using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Entities;
using Unity.InferenceEngine;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 웹캠 캡처를 깊이 추론에 제출하고 동일 캡처의 랜드마크로 샘플링해 ECS에 게시한다.
    /// Hand/Pose 싱글턴은 읽기만 하며 생성·소유권 획득을 하지 않는다.
    /// </summary>
    public sealed class DepthFrameProvider : MonoBehaviour
    {
        private const int TargetMaxPixels = 518;
        private const int PatchMultiple = 14;
        private const int MinValidHandLandmarks = 11;
        private const int MinValidPoseLandmarks = 17;

        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private ModelAsset _modelAsset;
        [SerializeField]
        private bool _useGpu = true;
        [SerializeField]
        private int _logIntervalFrames = 60;

        private DepthInferenceService _service;
        private float[] _inputBuffer;
        private int _inputWidth;
        private int _inputHeight;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private World _queryWorld;
        private EntityQuery _handQuery;
        private EntityQuery _poseQuery;
        private EntityQuery _settingsQuery;
        private bool _queriesCreated;
        private readonly CaptureSnapshotRing _ring = new();
        private readonly int[] _handedness = new int[CaptureSnapshotRing.MaxHands];
        private readonly float[] _handXY = new float[CaptureSnapshotRing.MaxHands * CaptureSnapshotRing.HandLandmarks * 2];
        private readonly float[] _poseXY = new float[CaptureSnapshotRing.PoseLandmarks * 2];
        private readonly float[] _sampleScratch = new float[CaptureSnapshotRing.PoseLandmarks];
        private long _lastSubmittedCaptureId;
        private long _submitCount;
        private long _completedCount;
        private long _droppedCount;
        private long _expiredCount;
        private long _publishedCaptureTimestampUs;
        private bool _hasLoggedOwnershipConflict;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_webcamSource == null || _modelAsset == null)
            {
                MpudLog.Error("[MPUD] DepthFrameProvider needs WebcamFrameProvider and ModelAsset.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize depth provider: {exception}");
                DisposeResources();
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_service == null || _webcamSource == null)
            {
                return;
            }

            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            ObserveSnapshots(entityManager);
            var settings = ReadSettings(entityManager);
            if (settings.Enabled == 0)
            {
                _service.Invalidate();
                return;
            }

            ExpirePublishedSample(entityManager, settings);
            var captureId = _webcamSource.LatestCaptureId;
            if (captureId != 0 && captureId != _lastSubmittedCaptureId && !_service.IsBusy)
            {
                SubmitCapture(captureId);
            }

            if (_service.TryTakeCompleted(out var completed))
            {
                _completedCount++;
                PublishSample(entityManager, completed);
                if (MpudLog.Enabled && _logIntervalFrames > 0 && _completedCount % _logIntervalFrames == 0)
                {
                    MpudLog.Log($"[MPUD] Depth #{_completedCount} | latency={completed.LatencyMs:F1}ms dropped={_droppedCount} expired={_expiredCount}");
                }
            }
        }

        private void OnDisable()
        {
            WriteResetStateIfPossible();
            DisposeResources();
        }

        private void OnDestroy() => DisposeResources();

        private void InitializeResources()
        {
            if (_service != null)
            {
                return;
            }

            _service = new DepthInferenceService(_modelAsset, _useGpu ? BackendType.GPUCompute : BackendType.CPU);
            if (!_service.IsReady)
            {
                throw new InvalidOperationException($"Depth worker not ready: {_service.LastError}");
            }

            _inputBuffer = null;
            _inputWidth = 0;
            _inputHeight = 0;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _lastSubmittedCaptureId = 0;
            _submitCount = 0;
            _completedCount = 0;
            _droppedCount = 0;
            _expiredCount = 0;
            _publishedCaptureTimestampUs = 0;
            _hasLoggedOwnershipConflict = false;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Depth provider started.");
        }

        private void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            DisposeQueries();
            _inputBuffer = null;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
        }

        private void SubmitCapture(long captureId)
        {
            var pixels = _webcamSource.LatestPixels;
            var width = _webcamSource.LatestPixelWidth;
            var height = _webcamSource.LatestPixelHeight;
            if (pixels == null || width <= 0 || height <= 0)
            {
                return;
            }

            var map = DepthSampler.ComputeMap(width, height, TargetMaxPixels, PatchMultiple);
            if (_inputBuffer == null || map.PaddedWidth != _inputWidth || map.PaddedHeight != _inputHeight)
            {
                _inputWidth = map.PaddedWidth;
                _inputHeight = map.PaddedHeight;
                _inputBuffer = new float[3 * _inputWidth * _inputHeight];
            }

            DepthSampler.Preprocess(pixels, width, height, _webcamSource.LatestFlipVertically, _inputBuffer, _inputWidth, _inputHeight, out _);
            if (_service.TrySubmit(_inputBuffer, _inputWidth, _inputHeight, captureId, _webcamSource.LatestCaptureTimestampUs, _webcamSource.CaptureEpoch))
            {
                _lastSubmittedCaptureId = captureId;
                _submitCount++;
            }
        }

        private void ObserveSnapshots(EntityManager entityManager)
        {
            var captureId = _webcamSource.LatestCaptureId;
            if (captureId == 0)
            {
                return;
            }

            var epoch = _webcamSource.CaptureEpoch;
            var srcWidth = _webcamSource.LatestPixelWidth;
            var srcHeight = _webcamSource.LatestPixelHeight;
            var handCount = 0;
            if (_handQuery.CalculateEntityCount() == 1)
            {
                var entity = _handQuery.GetSingletonEntity();
                var status = entityManager.GetComponentData<HandTrackingStatus>(entity);
                if (status.CaptureId == captureId && status.CaptureEpoch == epoch)
                {
                    var buffer = entityManager.GetBuffer<LandmarkElement>(entity);
                    handCount = Math.Min(status.HandCount, CaptureSnapshotRing.MaxHands);
                    for (var h = 0; h < handCount; h++)
                    {
                        _handedness[h] = h < status.HandednessList.Length ? status.HandednessList[h] : -1;
                        for (var i = 0; i < CaptureSnapshotRing.HandLandmarks; i++)
                        {
                            var index = h * CaptureSnapshotRing.HandLandmarks + i;
                            var x = -1f;
                            var y = -1f;
                            if (index < buffer.Length && buffer[index].HandIndex == h)
                            {
                                x = buffer[index].X;
                                y = buffer[index].Y;
                            }

                            _handXY[(h * CaptureSnapshotRing.HandLandmarks + i) * 2] = x;
                            _handXY[(h * CaptureSnapshotRing.HandLandmarks + i) * 2 + 1] = y;
                        }
                    }
                }
            }

            var poseCount = 0;
            if (_poseQuery.CalculateEntityCount() == 1)
            {
                var entity = _poseQuery.GetSingletonEntity();
                var status = entityManager.GetComponentData<PoseTrackingStatus>(entity);
                if (status.CaptureId == captureId && status.CaptureEpoch == epoch && status.PoseCount > 0)
                {
                    var buffer = entityManager.GetBuffer<PoseLandmarkElement>(entity);
                    poseCount = 1;
                    for (var i = 0; i < CaptureSnapshotRing.PoseLandmarks; i++)
                    {
                        var x = -1f;
                        var y = -1f;
                        if (i < buffer.Length && buffer[i].PoseIndex == 0)
                        {
                            x = buffer[i].X;
                            y = buffer[i].Y;
                        }

                        _poseXY[i * 2] = x;
                        _poseXY[i * 2 + 1] = y;
                    }
                }
            }

            _ring.Add(captureId, epoch, srcWidth, srcHeight, handCount, _handedness, _handXY, poseCount, _poseXY);
        }

        private void PublishSample(EntityManager entityManager, DepthInferenceService.CompletedMap completed)
        {
            if (!_ring.TryGet(completed.CaptureId, completed.CaptureEpoch, out var snapshot))
            {
                _droppedCount++;
                MpudLog.Warning($"[MPUD] Depth result for capture {completed.CaptureId} has no landmark snapshot, dropped.");
                return;
            }

            if (!EnsureOwnership(entityManager))
            {
                _droppedCount++;
                return;
            }

            var map = DepthSampler.ComputeMap(snapshot.SrcWidth, snapshot.SrcHeight, TargetMaxPixels, PatchMultiple);
            var handMask = 0;
            var handBuffer = entityManager.GetBuffer<HandDepthSampleElement>(_singletonEntity);
            if (handBuffer.Length != snapshot.HandCount)
            {
                handBuffer.ResizeUninitialized(snapshot.HandCount);
            }

            for (var h = 0; h < snapshot.HandCount; h++)
            {
                var representative = 0f;
                var valid = SampleTarget(completed, snapshot.HandXY, h * CaptureSnapshotRing.HandLandmarks, CaptureSnapshotRing.HandLandmarks, MinValidHandLandmarks, snapshot.SrcWidth, snapshot.SrcHeight, in map, out representative);
                handBuffer[h] = new HandDepthSampleElement { Depth = representative, HandIndex = h };
                if (valid)
                {
                    handMask |= 1 << h;
                }
            }

            var poseValid = 0;
            var poseBuffer = entityManager.GetBuffer<PoseDepthSampleElement>(_singletonEntity);
            if (poseBuffer.Length != snapshot.PoseCount)
            {
                poseBuffer.ResizeUninitialized(snapshot.PoseCount);
            }

            for (var p = 0; p < snapshot.PoseCount; p++)
            {
                var representative = 0f;
                var valid = SampleTarget(completed, snapshot.PoseXY, 0, CaptureSnapshotRing.PoseLandmarks, MinValidPoseLandmarks, snapshot.SrcWidth, snapshot.SrcHeight, in map, out representative);
                poseBuffer[p] = new PoseDepthSampleElement { Depth = representative, PoseIndex = p };
                if (valid)
                {
                    poseValid = 1;
                }
            }

            entityManager.SetComponentData(_singletonEntity, new DepthSampleStatus
            {
                IsValid = handMask != 0 || poseValid != 0,
                CaptureId = completed.CaptureId,
                CaptureTimestampUs = completed.CaptureTimestampUs,
                CaptureEpoch = completed.CaptureEpoch,
                HandCount = snapshot.HandCount,
                PoseCount = snapshot.PoseCount,
                HandValidMask = handMask,
                PoseValid = poseValid,
            });
            _publishedCaptureTimestampUs = completed.CaptureTimestampUs;
        }

        private bool SampleTarget(DepthInferenceService.CompletedMap completed, float[] xy, int offset, int count, int minValid, int srcWidth, int srcHeight, in DepthInputMap map, out float representative)
        {
            representative = 0f;
            var valid = 0;
            for (var i = 0; i < count; i++)
            {
                if (DepthSampler.TryMapToDepth(xy[(offset + i) * 2], xy[(offset + i) * 2 + 1], srcWidth, srcHeight, in map, completed.Width, completed.Height, out var dx, out var dy)
                    && DepthSampler.TrySample(completed.Values, completed.Width, completed.Height, dx, dy, out var value))
                {
                    _sampleScratch[valid++] = value;
                }
            }

            return valid >= minValid && DepthSampler.TryMedian(_sampleScratch, valid, out representative);
        }

        private void ExpirePublishedSample(EntityManager entityManager, DepthSettings settings)
        {
            if (_publishedCaptureTimestampUs == 0 || _singletonEntity == Entity.Null
                || !entityManager.Exists(_singletonEntity))
            {
                return;
            }

            var nowUs = _webcamSource.CaptureClockUs;
            if (!DepthSampleGate.IsFresh(nowUs, _publishedCaptureTimestampUs, settings.MaxSampleAgeUs)
                && TrackingWriterOwnershipUtil.IsOwner(entityManager, _singletonEntity, OwnerRaw()))
            {
                DepthSamplingSingletonUtil.WriteResetEmptyState(entityManager, _singletonEntity);
                _publishedCaptureTimestampUs = 0;
                _expiredCount++;
            }
        }

        private DepthSettings ReadSettings(EntityManager entityManager)
        {
            if (_settingsQuery.CalculateEntityCount() == 1)
            {
                return entityManager.GetComponentData<DepthSettings>(_settingsQuery.GetSingletonEntity());
            }

            return DepthSettings.Default;
        }

        private bool EnsureOwnership(EntityManager entityManager)
        {
            var owner = OwnerRaw();
            if (TrackingWriterOwnershipUtil.IsOwner(entityManager, _singletonEntity, owner))
            {
                return true;
            }

            if (TrackingWriterOwnershipUtil.TryAcquire(entityManager, _singletonEntity, owner))
            {
                _hasLoggedOwnershipConflict = false;
                return true;
            }

            if (!_hasLoggedOwnershipConflict)
            {
                _hasLoggedOwnershipConflict = true;
                MpudLog.Warning("[MPUD] Depth 싱글턴이 다른 프로바이더 소유라 기록을 건너뛴다.");
            }

            return false;
        }

        private ulong OwnerRaw() => EntityId.ToULong(GetEntityId());

        private bool TryGetEntityManager(out EntityManager entityManager)
        {
            entityManager = default;

            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld is not { IsCreated: true })
            {
                _ecsWorld = null;
                _singletonEntity = Entity.Null;
                DisposeQueries();
                return false;
            }

            if (_ecsWorld == null || _ecsWorld != defaultWorld || !_ecsWorld.IsCreated)
            {
                _ecsWorld = defaultWorld;
                _singletonEntity = Entity.Null;
                DisposeQueries();
            }

            entityManager = _ecsWorld.EntityManager;
            EnsureQueries(entityManager);
            if (_singletonEntity == Entity.Null || !entityManager.Exists(_singletonEntity))
            {
                _singletonEntity = DepthSamplingSingletonUtil.GetOrCreateSingleton(entityManager);
                _hasLoggedOwnershipConflict = false;
            }

            return true;
        }

        private void EnsureQueries(EntityManager entityManager)
        {
            if (_queriesCreated)
            {
                return;
            }

            _queryWorld = _ecsWorld;
            _handQuery = entityManager.CreateEntityQuery(typeof(HandTrackingStatus));
            _poseQuery = entityManager.CreateEntityQuery(typeof(PoseTrackingStatus));
            _settingsQuery = entityManager.CreateEntityQuery(typeof(DepthSettings));
            _queriesCreated = true;
        }

        private void DisposeQueries()
        {
            if (!_queriesCreated)
            {
                return;
            }

            _queriesCreated = false;
            _queryWorld = null;
            _handQuery.Dispose();
            _poseQuery.Dispose();
            _settingsQuery.Dispose();
        }

        private void WriteResetStateIfPossible()
        {
            if (_ecsWorld is { IsCreated: true } && _singletonEntity != Entity.Null
                && _ecsWorld.EntityManager.Exists(_singletonEntity))
            {
                var owner = OwnerRaw();
                if (!TrackingWriterOwnershipUtil.IsOwner(_ecsWorld.EntityManager, _singletonEntity, owner))
                {
                    return;
                }

                DepthSamplingSingletonUtil.WriteResetEmptyState(_ecsWorld.EntityManager, _singletonEntity);
                TrackingWriterOwnershipUtil.Release(_ecsWorld.EntityManager, _singletonEntity, owner);
            }
        }
    }
}
