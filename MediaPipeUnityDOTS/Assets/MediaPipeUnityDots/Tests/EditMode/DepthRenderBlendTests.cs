using MediaPipeUnityDots.Runtime.Ecs;
using NUnit.Framework;
using Unity.Mathematics;
using Unity.Transforms;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 필터 시스템이 제공한 filtered 좌표의 표시 매핑 검증.
    /// 보정은 깊이만 이동시키며 XY를 오염시키지 않는다.
    /// </summary>
    public sealed class DepthRenderBlendTests
    {
        private static LandmarkOverlayMapping Mapping()
        {
            return new LandmarkOverlayMapping
            {
                IsValid = 1,
                Flipped = 0,
                UvScaleX = 1f,
                UvScaleY = 1f,
                UvOffsetX = 0f,
                UvOffsetY = 0f,
                Origin = new float3(0f, 0f, 5f),
                AxisX = new float3(2f, 0f, 0f),
                AxisY = new float3(0f, 2f, 0f),
                Forward = new float3(0f, 0f, -1f),
                CameraPosition = new float3(0f, 0f, 10f),
                NearClipPlane = 0f,
                IsPerspective = 0,
            };
        }

        private static float3 Resolve(float correction, int useCorrection)
        {
            var mapping = Mapping();
            LandmarkRender.ResolveFilteredPoint(
                0.5f, 0.5f, -0.5f, 2f, -0.2f, 1,
                correction, useCorrection,
                in mapping, out var targetPos);
            return targetPos;
        }

        [Test]
        public void Correction_ShiftsDepthOnlyTowardCamera()
        {
            var baseline = Resolve(0f, 0);
            var corrected = Resolve(-0.1f, 1);
            Assert.AreEqual(baseline.x, corrected.x, 1e-6f, "X must not move");
            Assert.AreEqual(baseline.y, corrected.y, 1e-6f, "Y must not move");
            Assert.AreEqual(0.1f, corrected.z - baseline.z, 1e-6f, "negative offset moves toward camera");
        }

        [Test]
        public void CorrectionFlagOff_IgnoresCorrectionValue()
        {
            var baseline = Resolve(0f, 0);
            var ignored = Resolve(-0.5f, 0);
            Assert.AreEqual(baseline.z, ignored.z, 1e-6f);
        }

        [Test]
        public void HidePoint_StillHidesWithZeroScale()
        {
            var transform = LocalTransform.FromPositionRotationScale(new float3(1f), quaternion.identity, 1f);
            LandmarkRender.HidePoint(ref transform);
            Assert.AreEqual(0f, transform.Scale, 1e-6f);
        }
    }
}
