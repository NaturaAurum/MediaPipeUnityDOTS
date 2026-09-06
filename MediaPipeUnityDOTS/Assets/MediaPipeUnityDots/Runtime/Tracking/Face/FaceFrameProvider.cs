using System;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 공유 웹캠 픽셀을 얼굴 트래커에 제출하고 결과를 ECS에 푸시하는 런타임 프로바이더.
    /// WebcamFrameProvider.Update 이후에 동작하므로 LateUpdate에서 소비한다.
    /// </summary>
    public sealed class FaceFrameProvider : MonoBehaviour, IPointSource
    {
        private const int LandmarkCapacity = 478;

        [SerializeField]
        private WebcamFrameProvider _webcamSource;
        [SerializeField]
        private int _logIntervalFrames = 60;
        [SerializeField]
        private int _numFaces = 1;
        [SerializeField]
        private float _minDetectionConfidence = 0.5f;
        [SerializeField]
        private float _minTrackingConfidence = 0.5f;

        /// <summary>
        /// 추적할 얼굴 수. FaceTrackingService와 포인트 스포너가 공유한다.
        /// </summary>
        public int NumFaces => Mathf.Clamp(_numFaces, 1, MpudFaceResult.MaxFaces);
        int IPointSource.MaxTargets => NumFaces;

        private FaceTrackingService _service;
        private MpudNormalizedLandmark[] _landmarkCopyBuffer;
        private float[] _blendshapeCopyBuffer;
        private World _ecsWorld;
        private Entity _singletonEntity;
        private long _submitCount;
        private long _lastCopiedTimestamp;
        // TEMP 진단용. 원인 확정 후 SampleHash/DumpInvalidFrame와 함께 삭제.
        private int _prevHash;
        private int _invalidDumpCount;

        private void OnEnable()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (_webcamSource == null)
            {
                MpudLog.Error("[MPUD] FaceFrameProvider needs WebcamFrameProvider.");
                enabled = false;
                return;
            }

            try
            {
                InitializeResources();
            }
            catch (Exception exception)
            {
                MpudLog.Error($"[MPUD] Failed to initialize face provider: {exception}");
                DisposeResources();
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (_service == null)
            {
                return;
            }

            var pixels = _webcamSource.LatestPixels;
            var width = _webcamSource.LatestPixelWidth;
            var height = _webcamSource.LatestPixelHeight;
            if (pixels == null || width <= 0 || height <= 0)
            {
                return;
            }

            // TEMP 진단: 무효 프레임의 입력 영상 동결/지연 여부 판별용. 원인 확정 후 삭제.
            var hash = SampleHash(pixels);
            var submitStart = Time.realtimeSinceStartup;
            var previousFrameCount = _service.LatestFrameCount;
            _service.SubmitAndPoll(pixels, width, height, _webcamSource.LatestFlipVertically);
            var latencyMs = (Time.realtimeSinceStartup - submitStart) * 1000f;

            if (_service.LatestFrameCount == previousFrameCount)
            {
                return;
            }

            if (!_service.LatestIsValid)
            {
                MpudLog.Warning($"[MPUD][DIAG] face invalid | hash={hash} sameAsPrev={hash == _prevHash} latencyMs={latencyMs:F1}");
                // TEMP 진단: 무효 프레임 영상 3장 저장. 원인 확정 후 삭제.
                if (_invalidDumpCount < 3)
                {
                    _invalidDumpCount++;
                    DumpInvalidFrame(pixels, width, height);
                }
            }

            _prevHash = hash;

            _submitCount++;

            if (MpudLog.Enabled && _logIntervalFrames > 0 && _submitCount % _logIntervalFrames == 0)
            {
                MpudLog.Log(
                    $"[MPUD] Face frame #{_service.LatestFrameCount} | Valid={_service.LatestIsValid} | Faces={_service.LatestFaceCount} | Landmarks={_service.LatestLandmarkCount} | ts={_service.LatestTimestampUs}");
            }

            if (!TryGetEntityManager(out var entityManager))
            {
                return;
            }

            if (_service.LatestTimestampUs <= _lastCopiedTimestamp)
            {
                return;
            }

            PushLatestSnapshotToEcs(entityManager);
            _lastCopiedTimestamp = _service.LatestTimestampUs;
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

            var modelPath = Path.Combine(
                Application.streamingAssetsPath,
                "MediaPipe",
                "Models",
                "face_landmarker.task");
            if (!File.Exists(modelPath))
            {
                throw new FileNotFoundException("face_landmarker.task was not found.", modelPath);
            }
            _landmarkCopyBuffer = new MpudNormalizedLandmark[LandmarkCapacity];
            _blendshapeCopyBuffer = new float[MpudFaceResult.BlendshapesPerFace];
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
            _submitCount = 0;
            _lastCopiedTimestamp = 0;

            TryGetEntityManager(out _);

            MpudLog.Log("[MPUD] Face provider started.");
        }

        private void DisposeResources()
        {
            if (_service != null)
            {
                _service.Dispose();
                _service = null;
            }

            _landmarkCopyBuffer = null;
            _blendshapeCopyBuffer = null;
            _ecsWorld = null;
            _singletonEntity = Entity.Null;
        }


        private void PushLatestSnapshotToEcs(EntityManager entityManager)
        {
            if (_service.LatestIsValid)
            {
                WriteValidPolledState(entityManager);
                return;
            }

            FaceTrackingSingletonUtil.WriteInvalidPolledState(
                entityManager,
                _singletonEntity,
                _service.LatestTimestampUs,
                _service.LatestFrameCount);
        }

        private void WriteValidPolledState(EntityManager entityManager)
        {
            var faceCount = _service.LatestFaceCount;

            entityManager.SetComponentData(
                _singletonEntity,
                new FaceTrackingStatus
                {
                    IsValid = true,
                    FaceCount = faceCount,
                    LandmarkCount = _service.LatestLandmarkCount,
                    TimestampUs = _service.LatestTimestampUs,
                    FrameCount = _service.LatestFrameCount,
                });

            var landmarks = entityManager.GetBuffer<FaceLandmarkElement>(_singletonEntity);
            if (landmarks.Length != faceCount * LandmarkCapacity)
            {
                landmarks.ResizeUninitialized(faceCount * LandmarkCapacity);
            }

            for (var f = 0; f < faceCount; f++)
            {
                var copiedCount = _service.CopyLatestFaceLandmarksTo(f, _landmarkCopyBuffer);
                for (var i = 0; i < LandmarkCapacity; i++)
                {
                    var bufferIndex = f * LandmarkCapacity + i;
                    if (i < copiedCount)
                    {
                        var source = _landmarkCopyBuffer[i];
                        landmarks[bufferIndex] = new FaceLandmarkElement
                        {
                            X = source.x,
                            Y = source.y,
                            Z = source.z,
                            FaceIndex = f,
                        };
                    }
                    else
                    {
                        landmarks[bufferIndex] = new FaceLandmarkElement { FaceIndex = -1 };
                    }
                }
            }
            var blendshapeCapacity = MpudFaceResult.BlendshapesPerFace;
            var blendshapes = entityManager.GetBuffer<FaceBlendshapeElement>(_singletonEntity);
            if (blendshapes.Length != faceCount * blendshapeCapacity)
            {
                blendshapes.ResizeUninitialized(faceCount * blendshapeCapacity);
            }

            for (var f = 0; f < faceCount; f++)
            {
                var copiedCount = _service.CopyLatestFaceBlendshapesTo(f, _blendshapeCopyBuffer);
                for (var i = 0; i < blendshapeCapacity; i++)
                {
                    var bufferIndex = f * blendshapeCapacity + i;
                    if (i < copiedCount)
                    {
                        blendshapes[bufferIndex] = new FaceBlendshapeElement
                        {
                            Score = _blendshapeCopyBuffer[i],
                            FaceIndex = f,
                            BlendshapeIndex = i,
                        };
                    }
                    else
                    {
                        blendshapes[bufferIndex] = new FaceBlendshapeElement { FaceIndex = -1 };
                    }
                }
            }
        }

        // TEMP 진단용. 입력 영상이 프레임마다 바뀌는지 판별하는 cheap 해시. 원인 확정 후 삭제.
        private static int SampleHash(Color32[] pixels)
        {
            var hash = 0;
            for (var i = 0; i < pixels.Length; i += 4097)
            {
                hash = hash * 31 + pixels[i].r + pixels[i].g * 2 + pixels[i].b * 3;
            }

            return hash;
        }

        // TEMP 진단용. 무효 프레임 영상을 PNG로 저장한다. 원인 확정 후 삭제.
        private static void DumpInvalidFrame(Color32[] pixels, int width, int height)
        {
            try
            {
                var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                tex.SetPixels32(pixels);
                tex.Apply();
                var path = System.IO.Path.Combine(
                    Application.persistentDataPath,
                    $"face_invalid_{System.DateTime.Now:HHmmss_fff}.png");
                System.IO.File.WriteAllBytes(path, tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                MpudLog.Warning($"[MPUD][DIAG] dumped {path}");
            }
            catch (System.Exception exception)
            {
                MpudLog.Warning($"[MPUD][DIAG] dump failed: {exception.Message}");
            }
        }

        private bool TryGetEntityManager(out EntityManager entityManager)
        {
            entityManager = default;

            var defaultWorld = World.DefaultGameObjectInjectionWorld;
            if (defaultWorld is not { IsCreated: true })
            {
                _ecsWorld = null;
                _singletonEntity = Entity.Null;
                return false;
            }

            if (_ecsWorld == null || _ecsWorld != defaultWorld || !_ecsWorld.IsCreated)
            {
                _ecsWorld = defaultWorld;
                _singletonEntity = Entity.Null;
            }

            if (_singletonEntity == Entity.Null || !defaultWorld.EntityManager.Exists(_singletonEntity))
            {
                _singletonEntity = FaceTrackingSingletonUtil.GetOrCreateSingleton(defaultWorld.EntityManager);
            }

            entityManager = defaultWorld.EntityManager;
            return true;
        }

        private void WriteResetStateIfPossible()
        {
            if (_ecsWorld is { IsCreated: true } && _singletonEntity != Entity.Null
                && _ecsWorld.EntityManager.Exists(_singletonEntity))
            {
                FaceTrackingSingletonUtil.WriteResetEmptyState(_ecsWorld.EntityManager, _singletonEntity);
            }
        }
    }
}
