using System;
using MediaPipeUnityDots.Runtime.Ecs;
using MediaPipeUnityDots.Runtime.Interop;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Runtime.Tracking.Filtering
{
    /// <summary>
    /// caller-owned span을 그대로 채우는 기본 관리 필터다. 수치 계산은 기존 Burst OneEuro 코어를 재사용한다.
    /// 상태 배열은 landmark count가 늘 때만 확장되며, 안정된 topology에서는 프레임별 할당이 없다.
    /// </summary>
    public sealed class OneEuroLandmarkFilter : ILandmarkFilter
    {
        private float _derivativeCutoffHz;
        private float3 _minCutoffHz;
        private float3 _beta;
        private int _enabled;
        private LandmarkFilterState[] _states;
        private bool _disposed;

        public OneEuroLandmarkFilter(
            float minCutoffHz = 1f,
            float beta = 0.007f,
            float derivativeCutoffHz = 1f,
            int enabled = 1,
            float zMinCutoffHz = 0.3f,
            float zBeta = 0.002f)
        {
            ValidateFinite(nameof(minCutoffHz), minCutoffHz);
            ValidateFinite(nameof(beta), beta);
            ValidateFinite(nameof(derivativeCutoffHz), derivativeCutoffHz);
            ValidateFinite(nameof(zMinCutoffHz), zMinCutoffHz);
            ValidateFinite(nameof(zBeta), zBeta);
            _minCutoffHz = new float3(
                math.max(minCutoffHz, 0.0001f),
                math.max(minCutoffHz, 0.0001f),
                math.max(zMinCutoffHz, 0.0001f));
            _beta = new float3(
                math.max(beta, 0f),
                math.max(beta, 0f),
                math.max(zBeta, 0f));
            _derivativeCutoffHz = math.max(derivativeCutoffHz, 0.0001f);
            _enabled = enabled == 0 ? 0 : 1;
        }

        public OneEuroLandmarkFilter(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
            : this(
                ResolveMinCutoff(in settings, tracker),
                ResolveBeta(in settings, tracker),
                settings.DerivativeCutoffHz,
                settings.Enabled,
                settings.ZMinCutoff,
                settings.ZBeta)
        {
        }

        public int Filter(
            ReadOnlySpan<MpudNormalizedLandmark> input,
            Span<MpudNormalizedLandmark> output,
            in LandmarkFilterContext context)
        {
            ThrowIfDisposed();
            if (output.Length < input.Length)
            {
                throw new ArgumentException("One Euro output span is smaller than input span.", nameof(output));
            }

            EnsureStateCapacity(input.Length);
            for (var i = 0; i < input.Length; i++)
            {
                var source = input[i];
                var state = _states[i];
                var filtered = OneEuroFilter.Filter(
                    new float3(source.x, source.y, source.z),
                    ref state,
                    _enabled,
                    _minCutoffHz,
                    _beta,
                    _derivativeCutoffHz,
                    context.TimestampUs);
                _states[i] = state;
                output[i] = new MpudNormalizedLandmark
                {
                    x = filtered.x,
                    y = filtered.y,
                    z = filtered.z,
                    visibility = source.visibility,
                    presence = source.presence,
                };
            }

            return input.Length;
        }

        public void UpdateSettings(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
        {
            ThrowIfDisposed();
            var minCutoff = ResolveMinCutoff(in settings, tracker);
            var beta = ResolveBeta(in settings, tracker);
            ValidateFinite(nameof(settings.DerivativeCutoffHz), settings.DerivativeCutoffHz);
            ValidateFinite(nameof(settings.ZMinCutoff), settings.ZMinCutoff);
            ValidateFinite(nameof(settings.ZBeta), settings.ZBeta);
            ValidateFinite(nameof(minCutoff), minCutoff);
            ValidateFinite(nameof(beta), beta);
            _enabled = settings.Enabled == 0 ? 0 : 1;
            _derivativeCutoffHz = math.max(settings.DerivativeCutoffHz, 0.0001f);
            _minCutoffHz = new float3(
                math.max(minCutoff, 0.0001f),
                math.max(minCutoff, 0.0001f),
                math.max(settings.ZMinCutoff, 0.0001f));
            _beta = new float3(
                math.max(beta, 0f),
                math.max(beta, 0f),
                math.max(settings.ZBeta, 0f));
        }

        public void Reset()
        {
            ThrowIfDisposed();
            if (_states != null)
            {
                Array.Clear(_states, 0, _states.Length);
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _states = null;
            _disposed = true;
        }

        private void EnsureStateCapacity(int count)
        {
            if (_states == null || _states.Length < count)
            {
                Array.Resize(ref _states, count);
            }
        }

        private static void ValidateFinite(string name, float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "One Euro settings must be finite.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(OneEuroLandmarkFilter));
            }
        }

        private static float ResolveMinCutoff(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
        {
            return tracker switch
            {
                LandmarkFilterTracker.Face => settings.FaceMinCutoff,
                LandmarkFilterTracker.Pose => settings.PoseMinCutoff,
                _ => settings.HandMinCutoff,
            };
        }

        private static float ResolveBeta(in OneEuroFilterSettings settings, LandmarkFilterTracker tracker)
        {
            return tracker switch
            {
                LandmarkFilterTracker.Face => settings.FaceBeta,
                LandmarkFilterTracker.Pose => settings.PoseBeta,
                _ => settings.HandBeta,
            };
        }
    }
}
