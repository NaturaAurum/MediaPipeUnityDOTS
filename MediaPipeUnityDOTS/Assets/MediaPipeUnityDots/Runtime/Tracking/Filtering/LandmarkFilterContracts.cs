using System;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Ecs;

namespace MediaPipeUnityDots.Runtime.Tracking.Filtering
{
    /// <summary>
    /// 필터가 처리하는 트래커 종류. 대상 ordinal은 식별자가 아니다.
    /// </summary>
    public enum LandmarkFilterTracker : int
    {
        Unknown = 0,
        Hand = 1,
        Face = 2,
        Pose = 3,
        Holistic = 4,
        Depth = 5,
    }

    /// <summary>
    /// MediaPipe 원본 좌표의 의미. Unity 표시 좌표는 필터 입력으로 사용하지 않는다.
    /// </summary>
    public enum LandmarkFilterCoordinate : int
    {
        NormalizedImage = 0,
        ModelWorld = 1,
    }

    /// <summary>
    /// 한 번의 필터 호출을 설명하는 비관리 메타데이터다.
    /// ContinuityToken은 호출자가 대상 연속성을 증명할 때만 채운다. 0은 알 수 없음이다.
    /// </summary>
    public readonly struct LandmarkFilterContext
    {
        public readonly ulong StreamId;
        public readonly LandmarkFilterTracker Tracker;
        public readonly int Target;
        public readonly LandmarkFilterCoordinate Coordinate;
        public readonly long TimestampUs;
        public readonly long CaptureTimestampUs;
        public readonly long CaptureEpoch;
        public readonly long CaptureId;
        public readonly long FrameCount;
        public readonly ulong ContinuityToken;

        public LandmarkFilterContext(
            ulong streamId,
            LandmarkFilterTracker tracker,
            int target,
            LandmarkFilterCoordinate coordinate,
            long timestampUs,
            long captureEpoch,
            ulong continuityToken = 0,
            long captureId = 0,
            long frameCount = 0,
            long captureTimestampUs = 0)
        {
            StreamId = streamId;
            Tracker = tracker;
            Target = target;
            Coordinate = coordinate;
            TimestampUs = timestampUs;
            CaptureTimestampUs = captureTimestampUs;
            CaptureEpoch = captureEpoch;
            CaptureId = captureId;
            FrameCount = frameCount;
            ContinuityToken = continuityToken;
        }

        public bool HasProvenContinuity => ContinuityToken != 0;

        public bool IsValid => StreamId != 0
            && Tracker != LandmarkFilterTracker.Unknown
            && Target >= 0
            && Coordinate >= LandmarkFilterCoordinate.NormalizedImage
            && Coordinate <= LandmarkFilterCoordinate.ModelWorld
            && TimestampUs >= 0
            && CaptureTimestampUs >= 0
            && CaptureEpoch >= 0
            && CaptureId >= 0
            && FrameCount >= 0;

        public bool HasSameIdentity(in LandmarkFilterContext other)
        {
            return StreamId == other.StreamId
                && Tracker == other.Tracker
                && Target == other.Target
                && Coordinate == other.Coordinate;
        }

        /// <summary>
        /// TimestampUs와 epoch가 같은 재조회는 capture id/frame count가 달라도 같은 입력 프레임으로 취급한다.
        /// </summary>
        public bool IsSameFrame(in LandmarkFilterContext other)
        {
            return HasSameIdentity(in other)
                && TimestampUs == other.TimestampUs
                && CaptureEpoch == other.CaptureEpoch
                && ContinuityToken == other.ContinuityToken;
        }
    }
    /// <summary>
    /// 커스텀 필터의 최소 계약. 입력과 출력은 호출자가 소유하며, 구현은 호출 범위를 넘겨 span을 보관하지 않는다.
    /// 출력 count는 입력 count와 같아야 한다. x/y/z/visibility/presence 모두 유한해야 한다.
    /// 관리 계층의 동일 메인 스레드에서 동기 호출하며, Unity API를 워커에서 호출하지 않는다.
    /// </summary>
    public interface ILandmarkFilter : IDisposable
    {
        int Filter(
            ReadOnlySpan<MpudNormalizedLandmark> input,
            Span<MpudNormalizedLandmark> output,
            in LandmarkFilterContext context);

        void Reset();
    }

    public enum LandmarkFilterResult : int
    {
        Success = 0,
        NoData = 1,
        InvalidContext = 2,
        InvalidInput = 3,
        InvalidOutput = 4,
        FilterError = 5,
        Disposed = 6,
    }

    /// <summary>
    /// managed 계층에서만 필터를 호출한다. ECS 컴포넌트에는 이 객체나 인터페이스를 저장하지 않는다.
    /// 한 coordinator에 한 필터를 연결하며, 연속성을 증명하지 않은 대상 전환은 보수적으로 reset한다.
    /// </summary>
    public sealed class LandmarkFilterCoordinator : IDisposable
    {
        private ILandmarkFilter _filter;
        private MpudNormalizedLandmark[] _lastOutput;
        private LandmarkFilterContext _lastContext;
        private int _lastCount;
        private bool _hasLastOutput;
        private bool _ownsFilter;
        private bool _disposed;

        public static LandmarkFilterCoordinator CreateOneEuro(
            in OneEuroFilterSettings settings,
            LandmarkFilterTracker tracker)
        {
            return new LandmarkFilterCoordinator(
                new OneEuroLandmarkFilter(in settings, tracker),
                true);
        }


        public LandmarkFilterCoordinator(ILandmarkFilter filter, bool ownsFilter = false)
        {
            _filter = filter ?? throw new ArgumentNullException(nameof(filter));
            _ownsFilter = ownsFilter;
            LastResult = LandmarkFilterResult.NoData;
        }

        public LandmarkFilterResult LastResult { get; private set; }
        public string LastError { get; private set; }
        public bool HasValidOutput => _hasLastOutput;
        public LandmarkFilterContext LastContext => _lastContext;

        public bool TryProcess(
            ReadOnlySpan<MpudNormalizedLandmark> input,
            Span<MpudNormalizedLandmark> output,
            in LandmarkFilterContext context,
            out int outputCount)
        {
            outputCount = 0;
            if (input.Overlaps((ReadOnlySpan<MpudNormalizedLandmark>)output))
            {
                _hasLastOutput = false;
                _lastCount = 0;
                LastResult = LandmarkFilterResult.InvalidOutput;
                LastError = "Landmark filter input and output spans must not overlap.";
                outputCount = 0;
                return false;
            }

            if (_disposed)
            {
                output.Clear();
                LastResult = LandmarkFilterResult.Disposed;
                LastError = "Landmark filter coordinator is disposed.";
                return false;
            }

            output.Clear();
            LastError = null;
            if (!context.IsValid)
            {
                Invalidate(output, LandmarkFilterResult.InvalidContext, "Landmark filter context is invalid.");
                return false;
            }

            if (output.Length < input.Length)
            {
                Invalidate(output, LandmarkFilterResult.InvalidOutput, "Landmark filter output span is smaller than input span.");
                return false;
            }

            for (var i = 0; i < input.Length; i++)
            {
                if (!IsFinite(input[i]))
                {
                    Invalidate(output, LandmarkFilterResult.InvalidInput, "Landmark filter input contains a non-finite value.");
                    return false;
                }
            }

            if (_hasLastOutput && _lastCount == input.Length && context.IsSameFrame(in _lastContext))
            {
                _lastOutput.AsSpan(0, _lastCount).CopyTo(output);
                outputCount = _lastCount;
                LastResult = LandmarkFilterResult.Success;
                return true;
            }

            // 대상 ordinal만으로 연속성을 추정하지 않는다. 토큰이 없거나 바뀌면 상태를 버린다.
            var reset = !_hasLastOutput
                || !context.HasSameIdentity(in _lastContext)
                || context.CaptureEpoch != _lastContext.CaptureEpoch
                || context.TimestampUs < _lastContext.TimestampUs
                || !context.HasProvenContinuity
                || !_lastContext.HasProvenContinuity
                || context.ContinuityToken != _lastContext.ContinuityToken;
            try
            {
                if (input.Length == 0)
                {
                    Reset();
                    LastResult = LandmarkFilterResult.NoData;
                    return true;
                }

                if (reset)
                {
                    _hasLastOutput = false;
                    _filter.Reset();
                }

                var count = _filter.Filter(input, output.Slice(0, input.Length), in context);
                if (count != input.Length)
                {
                    Invalidate(output, LandmarkFilterResult.InvalidOutput,
                        "Landmark filter must preserve input count and topology.");
                    return false;
                }
                for (var i = 0; i < count; i++)
                {
                    if (!IsFinite(output[i]))
                    {
                        Invalidate(output, LandmarkFilterResult.InvalidOutput,
                            "Landmark filter output contains a non-finite value.");
                        return false;
                    }
                }

                EnsureLastOutputCapacity(count);
                output.Slice(0, count).CopyTo(_lastOutput);
                _lastContext = context;
                _lastCount = count;
                _hasLastOutput = true;
                outputCount = count;
                LastResult = LandmarkFilterResult.Success;
                return true;
            }
            catch (Exception exception)
            {
                Invalidate(output, LandmarkFilterResult.FilterError, exception.Message);
                return false;
            }
        }

        public void Reset()
        {
            ThrowIfDisposed();
            _hasLastOutput = false;
            _lastCount = 0;
            _lastContext = default;
            if (_lastOutput != null)
            {
                Array.Clear(_lastOutput, 0, _lastOutput.Length);
            }

            LastResult = LandmarkFilterResult.NoData;
            LastError = null;
            _filter.Reset();
        }

        public void ReplaceFilter(ILandmarkFilter filter, bool ownsFilter = false)
        {
            ThrowIfDisposed();
            if (filter == null)
            {
                throw new ArgumentNullException(nameof(filter));
            }

            if (ReferenceEquals(_filter, filter))
            {
                _ownsFilter = ownsFilter;
                Reset();
                return;
            }

            if (_ownsFilter)
            {
                _filter.Dispose();
            }

            _filter = filter;
            _ownsFilter = ownsFilter;
            Reset();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            if (_ownsFilter)
            {
                _filter.Dispose();
            }

            _filter = null;
            _lastOutput = null;
            _lastCount = 0;
            _hasLastOutput = false;
            _disposed = true;
            LastResult = LandmarkFilterResult.Disposed;
            LastError = null;
        }

        private void EnsureLastOutputCapacity(int count)
        {
            if (_lastOutput == null || _lastOutput.Length < count)
            {
                _lastOutput = new MpudNormalizedLandmark[count];
            }
        }

        private void Invalidate(Span<MpudNormalizedLandmark> output, LandmarkFilterResult result, string error)
        {
            output.Clear();
            _hasLastOutput = false;
            _lastCount = 0;
            LastResult = result;
            LastError = error;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(LandmarkFilterCoordinator));
            }
        }

        private static bool IsFinite(in MpudNormalizedLandmark landmark)
        {
            return IsFinite(landmark.x)
                && IsFinite(landmark.y)
                && IsFinite(landmark.z)
                && IsFinite(landmark.visibility)
                && IsFinite(landmark.presence);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
