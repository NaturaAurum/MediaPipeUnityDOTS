using System;
using System.Diagnostics;
using System.IO;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Models;
using MediaPipeUnityDots.Runtime.Tracking;
using MediaPipeUnityDots.Runtime.Tracking.Filtering;
using UnityEngine;

namespace MediaPipeUnityDots.Sample.LandmarkApi
{
    /// <summary>
    /// 빈 소비자 프로젝트에서 네 서비스와 필터 계약을 한 번에 확인하는 장면 독립 시나리오입니다.
    /// </summary>
    public sealed class LandmarkApiSmoke : IDisposable
    {
        public const string FixtureFileName = "male_full_height_hands.jpg";
        public const string FaceFixtureFileName = "portrait.jpg";

        private enum Phase
        {
            Created,
            SubmitDetected,
            PollDetected,
            Reset,
            SubmitNoDetection,
            PollNoDetection,
            Finished,
            Failed,
        }

        private readonly string _fixturePath;
        private readonly string[] _modelPaths;
        private readonly double _timeoutSeconds;
        private readonly Action<string> _log;
        private readonly Action<bool, string> _completed;
        private readonly Func<double> _clock;

        private Texture2D _texture;
        private Texture2D _faceTexture;
        private Color32[] _facePixels;
        private Color32[] _pixels;
        private Color32[] _blankPixels;
        private int _width;
        private int _height;
        private CaptureStamp _detectedStamp;
        private CaptureStamp _emptyStamp;
        private Phase _phase;
        private double _startedAt;
        private double _phaseStartedAt;
        private bool _disposed;
        private bool _completedOnce;

        private HandTrackingService _hand;
        private FaceTrackingService _face;
        private PoseTrackingService _pose;
        private HolisticTrackingService _holistic;
        private readonly bool[] _submitted = new bool[4];
        private readonly bool[] _statusLogged = new bool[4];
        private readonly TrackingResultStatus[] _statuses = new TrackingResultStatus[4];

        public LandmarkApiSmoke(
            string fixturePath,
            string handModelPath,
            string faceModelPath,
            string poseModelPath,
            string holisticModelPath,
            double timeoutSeconds,
            Action<string> log,
            Action<bool, string> completed,
            Func<double> clock = null)
        {
            _fixturePath = fixturePath;
            _modelPaths = new[] { handModelPath, faceModelPath, poseModelPath, holisticModelPath };
            _timeoutSeconds = timeoutSeconds > 0d ? timeoutSeconds : 120d;
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _completed = completed ?? throw new ArgumentNullException(nameof(completed));
            _clock = clock ?? DefaultClock;
            _detectedStamp = new CaptureStamp(1, 1_000_000, 1);
            _emptyStamp = new CaptureStamp(2, 2_000_000, 2);
            _phase = Phase.Created;
        }

        public static LandmarkApiSmoke CreateDefault(
            Action<string> log,
            Action<bool, string> completed,
            double timeoutSeconds = 120d)
        {
            return new LandmarkApiSmoke(
                ResolveFixturePath(null),
                ModelPaths.GetPath(TrackingModel.Hand),
                ModelPaths.GetPath(TrackingModel.Face),
                ModelPaths.GetPath(TrackingModel.Pose),
                ModelPaths.GetPath(TrackingModel.Holistic),
                timeoutSeconds,
                log,
                completed);
        }

        public static string ResolveFixturePath(string requestedPath)
        {
            if (!string.IsNullOrWhiteSpace(requestedPath))
            {
                return requestedPath;
            }

            return Path.Combine(Application.streamingAssetsPath, "MediaPipe", "Fixtures", FixtureFileName);
        }

        public void Start()
        {
            if (_phase != Phase.Created)
            {
                return;
            }

            _startedAt = _clock();
            _phaseStartedAt = _startedAt;
            Log("[MPUD CONSUMER SMOKE] START");
            _phase = Phase.SubmitDetected;
        }

        public void Tick()
        {
            if (_phase == Phase.Finished || _phase == Phase.Failed || _disposed)
            {
                return;
            }

            if (_phase == Phase.Created)
            {
                Start();
            }

            if (_clock() - _startedAt > _timeoutSeconds)
            {
                Fail("timeout phase=" + _phase);
                return;
            }

            try
            {
                switch (_phase)
                {
                    case Phase.SubmitDetected:
                        EnsureInitialized();
                        Submit(_pixels, _detectedStamp, Phase.PollDetected);
                        break;
                    case Phase.PollDetected:
                        PollDetected();
                        break;
                    case Phase.Reset:
                        ResetTrackers();
                        break;
                    case Phase.SubmitNoDetection:
                        Submit(_blankPixels, _emptyStamp, Phase.PollNoDetection);
                        break;
                    case Phase.PollNoDetection:
                        PollNoDetection();
                        break;
                }
            }
            catch (Exception exception)
            {
                Fail(exception.GetType().Name + ": " + exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            DisposeServices();
            DestroyTexture(_texture);
            DestroyTexture(_faceTexture);
            _texture = null;
            _faceTexture = null;
        }

        private static void DestroyTexture(Texture2D texture)
        {
            if (texture == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(texture);
            else
                UnityEngine.Object.DestroyImmediate(texture);
        }

        private void EnsureInitialized()
        {
            if (_hand != null)
            {
                return;
            }

            if (!File.Exists(_fixturePath))
            {
                throw new FileNotFoundException("fixture image was not found", _fixturePath);
            }

            for (var i = 0; i < _modelPaths.Length; i++)
            {
                if (string.IsNullOrWhiteSpace(_modelPaths[i]) || !File.Exists(_modelPaths[i]))
                {
                    throw new FileNotFoundException("model was not found", _modelPaths[i]);
                }
            }

            var imageBytes = File.ReadAllBytes(_fixturePath);
            _texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(_texture, imageBytes, false))
            {
                throw new InvalidDataException("fixture image could not be decoded");
            }
            var faceBytes = File.ReadAllBytes(Path.Combine(Path.GetDirectoryName(_fixturePath), FaceFixtureFileName));
            _faceTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(_faceTexture, faceBytes, false))
                throw new InvalidDataException("face fixture image could not be decoded");
            _facePixels = _faceTexture.GetPixels32();

            _width = _texture.width;
            _height = _texture.height;
            _pixels = _texture.GetPixels32();
            _blankPixels = new Color32[_pixels.Length];
            for (var i = 0; i < _blankPixels.Length; i++)
            {
                _blankPixels[i] = new Color32(0, 0, 0, 255);
            }

            _hand = new HandTrackingService(_modelPaths[0], 2);
            _face = new FaceTrackingService(_modelPaths[1], 1);
            _pose = new PoseTrackingService(_modelPaths[2], 1);
            _holistic = new HolisticTrackingService(_modelPaths[3]);
            Log("[MPUD CONSUMER SMOKE] INITIALIZED image=" + _width + "x" + _height);
        }

        private void Submit(Color32[] pixels, CaptureStamp stamp, Phase nextPhase)
        {
            if (!_submitted[0]) _submitted[0] = _hand.TrySubmit(pixels, _width, _height, true, stamp);
            if (!_submitted[1])
            {
                var detected = nextPhase == Phase.PollDetected;
                _submitted[1] = _face.TrySubmit(
                    detected ? _facePixels : pixels,
                    detected ? _faceTexture.width : _width,
                    detected ? _faceTexture.height : _height, true, stamp);
            }
            if (!_submitted[2]) _submitted[2] = _pose.TrySubmit(pixels, _width, _height, true, stamp);
            if (!_submitted[3]) _submitted[3] = _holistic.TrySubmit(pixels, _width, _height, true, stamp);

            if (_submitted[0] && _submitted[1] && _submitted[2] && _submitted[3])
            {
                _phase = nextPhase;
                _phaseStartedAt = _clock();
            }
            else if (_clock() - _phaseStartedAt > _timeoutSeconds)
            {
                throw new TimeoutException("one or more services did not accept the frame");
            }
        }

        private void PollDetected()
        {
            PollPending();
            for (var i = 0; i < _statuses.Length; i++)
            {
                if (!_statusLogged[i] && _statuses[i] != TrackingResultStatus.Waiting)
                {
                    _statusLogged[i] = true;
                    LogResult(i, _statuses[i]);
                }
            }

            if (_statuses[0] == TrackingResultStatus.Waiting ||
                _statuses[1] == TrackingResultStatus.Waiting ||
                _statuses[2] == TrackingResultStatus.Waiting ||
                _statuses[3] == TrackingResultStatus.Waiting)
            {
                return;
            }

            for (var i = 0; i < _statuses.Length; i++)
            {
                if (_statuses[i] != TrackingResultStatus.Success)
                {
                    throw new InvalidOperationException("expected detected result for service index " + i + ", got " + _statuses[i]);
                }
            }

            ValidateCopiesAndFilters();
            _phase = Phase.Reset;
            _phaseStartedAt = _clock();
            Log("[MPUD CONSUMER SMOKE] DETECTED_RESULTS_OK");
        }

        private void ValidateCopiesAndFilters()
        {
            var handCount = _hand.GetLatestLandmarkCount(0);
            var handLandmarks = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var handWorld = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var copiedHand = _hand.CopyLatestHandLandmarksTo(0, handLandmarks);
            var copiedHandWorld = _hand.CopyLatestHandWorldLandmarksTo(0, handWorld);
            if (handCount <= 0 || copiedHand != handCount || copiedHandWorld != handCount)
            {
                throw new InvalidOperationException("hand caller-owned copy count mismatch");
            }

            var faceCount = _face.GetLatestLandmarkCount(0);
            var faceLandmarks = new MpudNormalizedLandmark[MpudFaceResult.LandmarksPerFace];
            var faceBlendshapes = new float[MpudFaceResult.BlendshapesPerFace];
            var copiedFace = _face.CopyLatestFaceLandmarksTo(0, faceLandmarks);
            var copiedBlendshapes = _face.CopyLatestFaceBlendshapesTo(0, faceBlendshapes);
            if (faceCount <= 0 || copiedFace != faceCount || copiedBlendshapes != _face.GetLatestBlendshapeCount(0))
            {
                throw new InvalidOperationException("face caller-owned copy count mismatch");
            }

            var poseCount = _pose.GetLatestLandmarkCount(0);
            var poseLandmarks = new MpudNormalizedLandmark[MpudPoseResult.LandmarksPerPose];
            var poseWorld = new MpudNormalizedLandmark[MpudPoseResult.LandmarksPerPose];
            var copiedPose = _pose.CopyLatestPoseLandmarksTo(0, poseLandmarks);
            var copiedPoseWorld = _pose.CopyLatestPoseWorldLandmarksTo(0, poseWorld);
            if (poseCount <= 0 || copiedPose != poseCount || copiedPoseWorld != poseCount)
            {
                throw new InvalidOperationException("pose caller-owned copy count mismatch");
            }

            var holisticFace = new MpudNormalizedLandmark[MpudFaceResult.LandmarksPerFace];
            var holisticPose = new MpudNormalizedLandmark[MpudPoseResult.LandmarksPerPose];
            var holisticLeft = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var holisticRight = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var holisticPoseWorld = new MpudNormalizedLandmark[MpudPoseResult.LandmarksPerPose];
            var holisticLeftWorld = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var holisticRightWorld = new MpudNormalizedLandmark[MpudHandResult.LandmarksPerHand];
            var copiedHolisticFace = _holistic.CopyLatestFaceTo(holisticFace);
            var copiedHolisticPose = _holistic.CopyLatestPoseTo(holisticPose);
            var copiedHolisticLeft = _holistic.CopyLatestLeftHandTo(holisticLeft);
            var copiedHolisticRight = _holistic.CopyLatestRightHandTo(holisticRight);
            var copiedHolisticPoseWorld = _holistic.CopyLatestPoseWorldTo(holisticPoseWorld);
            var copiedHolisticLeftWorld = _holistic.CopyLatestLeftHandWorldTo(holisticLeftWorld);
            var copiedHolisticRightWorld = _holistic.CopyLatestRightHandWorldTo(holisticRightWorld);
            if (copiedHolisticFace <= 0 || copiedHolisticPose <= 0 ||
                copiedHolisticPoseWorld != copiedHolisticPose ||
                copiedHolisticLeftWorld != copiedHolisticLeft ||
                copiedHolisticRightWorld != copiedHolisticRight)
            {
                throw new InvalidOperationException("holistic caller-owned copy count mismatch");
            }
            Log("[MPUD CONSUMER SMOKE] RAW_COPY_OK");

            ValidateMetadata("hand", _hand.LatestMetadata);
            ValidateMetadata("face", _face.LatestMetadata);
            ValidateMetadata("pose", _pose.LatestMetadata);
            ValidateMetadata("holistic", _holistic.LatestMetadata);
            var metadata = _hand.LatestMetadata;

            var rawFirst = handLandmarks[0];
            var defaultOutput = new MpudNormalizedLandmark[handCount];
            var settings = OneEuroFilterSettings.Default;
            using var defaultCoordinator = LandmarkFilterCoordinator.CreateOneEuro(in settings, LandmarkFilterTracker.Hand);
            var context = new LandmarkFilterContext(
                1,
                LandmarkFilterTracker.Hand,
                0,
                LandmarkFilterCoordinate.NormalizedImage,
                metadata.TimestampUs,
                metadata.CaptureEpoch,
                1,
                metadata.CaptureId,
                metadata.FrameCount,
                metadata.CaptureTimestampUs);
            if (!defaultCoordinator.TryProcess(handLandmarks.AsSpan(0, handCount), defaultOutput.AsSpan(), in context, out var defaultCount) ||
                defaultCount != handCount)
            {
                throw new InvalidOperationException("default filter contract failed");
            }
            // 같은 검출 결과를 이동시킨 연속 프레임으로 기본 필터의 실제 평활화를 확인한다.
            const float positionStep = 0.05f;
            const long frameStepUs = 33_333;
            var nextInput = (MpudNormalizedLandmark[])handLandmarks.Clone();
            for (var i = 0; i < handCount; i++)
            {
                nextInput[i].x += positionStep;
            }
            var nextContext = new LandmarkFilterContext(
                context.StreamId, context.Tracker, context.Target, context.Coordinate,
                context.TimestampUs + frameStepUs, context.CaptureEpoch, context.ContinuityToken,
                context.CaptureId + 1, context.FrameCount + 1, context.CaptureTimestampUs + frameStepUs);
            if (!defaultCoordinator.TryProcess(nextInput.AsSpan(0, handCount), defaultOutput, in nextContext, out defaultCount) ||
                defaultCount != handCount || defaultOutput[0].x <= rawFirst.x || defaultOutput[0].x >= nextInput[0].x ||
                handLandmarks[0].x != rawFirst.x)
            {
                throw new InvalidOperationException("default filter did not smooth a continuous frame");
            }
            Log("[MPUD CONSUMER SMOKE] DEFAULT_FILTER_OK count=" + defaultCount);

            var customOutput = new MpudNormalizedLandmark[handCount];
            using var customCoordinator = new LandmarkFilterCoordinator(new OffsetLandmarkFilter(0.001f), true);
            if (!customCoordinator.TryProcess(handLandmarks.AsSpan(0, handCount), customOutput.AsSpan(), in context, out var customCount) ||
                customCount != handCount ||
                Mathf.Approximately(customOutput[0].x, rawFirst.x) ||
                !Mathf.Approximately(handLandmarks[0].x, rawFirst.x))
            {
                throw new InvalidOperationException("custom filter did not preserve raw output");
            }
            customCoordinator.Reset();
            Log("[MPUD CONSUMER SMOKE] CUSTOM_FILTER_OK count=" + customCount);

            Log("[MPUD CONSUMER SMOKE] TOPOLOGY_ACCESS_OK handTipX=" +
                handLandmarks[(int)HandLandmark.IndexFingerTip].x +
                " poseWristX=" + poseLandmarks[(int)PoseLandmark.LeftWrist].x +
                " jawOpen=" + faceBlendshapes[FaceBlendshapeNames.GetIndex("jawOpen")]);
        }

        private void ValidateMetadata(string service, TrackingResultMetadata metadata)
        {
            if (metadata.Status != TrackingResultStatus.Success || !metadata.HasCapture ||
                metadata.CaptureId != _detectedStamp.CaptureId || metadata.CaptureEpoch != _detectedStamp.CaptureEpoch)
            {
                throw new InvalidOperationException(service + " metadata contract mismatch");
            }
        }
        private void ResetTrackers()
        {
            _hand.ResetTracker();
            _face.ResetTracker();
            _pose.ResetTracker();
            _holistic.ResetTracker();
            if (_hand.LatestStatus != TrackingResultStatus.Reset ||
                _face.LatestStatus != TrackingResultStatus.Reset ||
                _pose.LatestStatus != TrackingResultStatus.Reset ||
                _holistic.LatestStatus != TrackingResultStatus.Reset)
            {
                throw new InvalidOperationException("reset status contract failed");
            }

            Array.Clear(_submitted, 0, _submitted.Length);
            Array.Clear(_statusLogged, 0, _statusLogged.Length);
            Array.Clear(_statuses, 0, _statuses.Length);
            _phase = Phase.SubmitNoDetection;
            _phaseStartedAt = _clock();
            Log("[MPUD CONSUMER SMOKE] RESET_OK");
        }

        private void PollNoDetection()
        {
            PollPending();
            if (_statuses[0] == TrackingResultStatus.Waiting ||
                _statuses[1] == TrackingResultStatus.Waiting ||
                _statuses[2] == TrackingResultStatus.Waiting ||
                _statuses[3] == TrackingResultStatus.Waiting)
            {
                return;
            }

            for (var i = 0; i < _statuses.Length; i++)
            {
                if (_statuses[i] != TrackingResultStatus.NoDetection)
                {
                    throw new InvalidOperationException("expected no-detection result for service index " + i + ", got " + _statuses[i]);
                }
            }

            Log("[MPUD CONSUMER SMOKE] NO_DETECTION_OK");
            if (!DisposeServicesAndCheck())
            {
                throw new InvalidOperationException("dispose status contract failed");
            }

            _phase = Phase.Finished;
            Dispose();
            Log("[MPUD CONSUMER SMOKE] PASS");
            Complete(true, null);
        }

        private bool DisposeServicesAndCheck()
        {
            _hand.Dispose();
            _face.Dispose();
            _pose.Dispose();
            _holistic.Dispose();
            Log("[MPUD CONSUMER SMOKE] RELEASE_OK");
            var result = _hand.Poll() == TrackingResultStatus.Disposed &&
                _face.Poll() == TrackingResultStatus.Disposed &&
                _pose.Poll() == TrackingResultStatus.Disposed &&
                _holistic.Poll() == TrackingResultStatus.Disposed;
            Log("[MPUD CONSUMER SMOKE] DISPOSE_OK");
            return result;
        }

        private void LogResult(int index, TrackingResultStatus status)
        {
            var names = new[] { "HAND", "FACE", "POSE", "HOLISTIC" };
            var metadata = index switch
            {
                0 => _hand.LatestMetadata,
                1 => _face.LatestMetadata,
                2 => _pose.LatestMetadata,
                _ => _holistic.LatestMetadata,
            };
            Log("[MPUD CONSUMER SMOKE] " + names[index] + "_RESULT status=" + status +
                " captureId=" + metadata.CaptureId + " timestampUs=" + metadata.TimestampUs);
        }

        private void PollPending()
        {
            if (_statuses[0] == TrackingResultStatus.Waiting) _statuses[0] = _hand.Poll();
            if (_statuses[1] == TrackingResultStatus.Waiting) _statuses[1] = _face.Poll();
            if (_statuses[2] == TrackingResultStatus.Waiting) _statuses[2] = _pose.Poll();
            if (_statuses[3] == TrackingResultStatus.Waiting) _statuses[3] = _holistic.Poll();
        }

        private void Fail(string message)
        {
            if (_phase == Phase.Failed || _phase == Phase.Finished)
            {
                return;
            }

            _phase = Phase.Failed;
            Log("[MPUD CONSUMER SMOKE] ERROR " + message);
            Dispose();
            Complete(false, message);
        }

        private void Complete(bool success, string error)
        {
            if (_completedOnce)
            {
                return;
            }

            _completedOnce = true;
            _completed(success, error);
        }

        private void DisposeServices()
        {
            _hand?.Dispose();
            _face?.Dispose();
            _pose?.Dispose();
            _holistic?.Dispose();
        }

        private void Log(string message)
        {
            _log(message);
        }

        private static double DefaultClock()
        {
            return (double)Stopwatch.GetTimestamp() / Stopwatch.Frequency;
        }
    }

    /// <summary>
    /// 호출자 필터 예제입니다. 원본 배열은 수정하지 않고 x만 일정량 이동합니다.
    /// </summary>
    public sealed class OffsetLandmarkFilter : ILandmarkFilter
    {
        private readonly float _offset;
        private bool _disposed;

        public OffsetLandmarkFilter(float offset)
        {
            _offset = offset;
        }

        public int Filter(
            ReadOnlySpan<MpudNormalizedLandmark> input,
            Span<MpudNormalizedLandmark> output,
            in LandmarkFilterContext context)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OffsetLandmarkFilter));
            }

            if (output.Length < input.Length)
            {
                throw new ArgumentException("output is smaller than input", nameof(output));
            }

            for (var i = 0; i < input.Length; i++)
            {
                var value = input[i];
                value.x += _offset;
                output[i] = value;
            }

            return input.Length;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
            _disposed = true;
        }
    }

}
