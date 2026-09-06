using MediaPipeUnityDots.Runtime.Ecs;
using NUnit.Framework;
using Unity.Mathematics;

namespace MediaPipeUnityDots.Tests.EditMode
{
    public sealed class LandmarkOverlayMappingTests
    {
        [TestCase(0, 0)]
        [TestCase(0, 1)]
        [TestCase(1, 0)]
        [TestCase(1, 1)]
        public void DepthPreservesTexturePixel_WithCropMirrorAndCameraTransform(int perspective, int flipped)
        {
            var rotation = quaternion.EulerXYZ(0.3f, -0.7f, 0.2f);
            var right = math.mul(rotation, new float3(1f, 0f, 0f));
            var up = math.mul(rotation, new float3(0f, 1f, 0f));
            var forward = math.mul(rotation, new float3(0f, 0f, 1f));
            var camera = new float3(3f, -2f, 5f);
            var mapping = new LandmarkOverlayMapping
            {
                IsValid = 1, IsPerspective = perspective, Flipped = flipped,
                CameraPosition = camera, NearClipPlane = 0.3f,
                Origin = camera + forward * 15f,
                AxisX = right * 24f, AxisY = up * 16f, Forward = forward,
                UvScaleX = -0.7f, UvOffsetX = 0.85f,
                UvScaleY = -0.8f, UvOffsetY = 0.9f,
            };
            const float x = 0.29f;
            const float y = 0.67f;
            var previousDepth = float.PositiveInfinity;
            foreach (var depth in new[] { 0f, -0.4f, -3f, -100f })
            {
                var position = LandmarkOverlayMapping.MapWithDepth(x, y, depth, in mapping);
                var cameraDepth = math.dot(position - camera, forward);
                Assert.That(cameraDepth, Is.GreaterThan(mapping.NearClipPlane));
                Assert.That(cameraDepth, Is.LessThan(previousDepth));
                previousDepth = cameraDepth;

                // 카메라에서 Quad로 역투영해 실제 샘플링되는 텍스처 좌표를 검증한다.
                var projected = perspective != 0
                    ? camera + (position - camera) * (15f / cameraDepth)
                    : position + forward * (15f - cameraDepth);
                var u = math.dot(projected - mapping.Origin, right) / 24f + 0.5f;
                var v = math.dot(projected - mapping.Origin, up) / 16f + 0.5f;
                Assert.That(u * mapping.UvScaleX + mapping.UvOffsetX, Is.EqualTo(x).Within(1e-5f));
                Assert.That(v * mapping.UvScaleY + mapping.UvOffsetY,
                    Is.EqualTo(flipped != 0 ? 1f - y : y).Within(1e-5f));
            }

            Assert.That(LandmarkOverlayMapping.Map(x, y, in mapping),
                Is.EqualTo(LandmarkOverlayMapping.MapWithDepth(x, y, 0f, in mapping)));
        }

        [Test]
        public void DepthScaleTracksImageSizeAndCrop_WithoutMirrorSignChange()
        {
            var mapping = new LandmarkOverlayMapping
            {
                AxisX = new float3(20f, 0f, 0f), AxisY = new float3(0f, 10f, 0f),
                UvScaleX = 0.5f, UvScaleY = -1f,
            };
            var imageSpan = new float2(0.1f, 0.2f);
            var worldSpan = new float2(0.2f, 0.1f);
            var scale = LandmarkOverlayMapping.GetDepthScale(imageSpan, worldSpan, in mapping);
            Assert.That(scale, Is.EqualTo(20f).Within(1e-5f));
            Assert.That(LandmarkOverlayMapping.GetDepthScale(imageSpan * 2f, worldSpan, in mapping),
                Is.EqualTo(scale * 2f).Within(1e-5f));
            mapping.UvScaleY = 1f;
            Assert.That(LandmarkOverlayMapping.GetDepthScale(imageSpan, worldSpan, in mapping),
                Is.EqualTo(scale).Within(1e-5f));
            Assert.That(LandmarkOverlayMapping.GetDepthScale(imageSpan, float2.zero, in mapping), Is.Zero);
        }
    }
}
