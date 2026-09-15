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
        private static LandmarkOverlayMapping ShapeMapping()
        {
            return new LandmarkOverlayMapping
            {
                IsValid = 1, IsPerspective = 1, Flipped = 0,
                CameraPosition = new float3(0f, 0f, 0f), NearClipPlane = 0.3f,
                Origin = new float3(0f, 0f, 15f),
                AxisX = new float3(20f, 0f, 0f), AxisY = new float3(0f, 10f, 0f),
                Forward = new float3(0f, 0f, 1f),
                UvScaleX = 1f, UvOffsetX = 0f, UvScaleY = 1f, UvOffsetY = 0f,
            };
        }

        [Test]
        public void ShapePreserving_LateralIgnoresDepth()
        {
            var mapping = ShapeMapping();
            var shallow = LandmarkOverlayMapping.MapShapePreserving(0.3f, 0.7f, 0f, in mapping);
            var deep = LandmarkOverlayMapping.MapShapePreserving(0.3f, 0.7f, -2f, in mapping);
            Assert.That(deep.x, Is.EqualTo(shallow.x).Within(1e-5f));
            Assert.That(deep.y, Is.EqualTo(shallow.y).Within(1e-5f));
            Assert.That(shallow.z - deep.z, Is.EqualTo(2f).Within(1e-5f));
        }

        [Test]
        public void StraightFinger_StaysStraightIn3D()
        {
            var mapping = ShapeMapping();
            var points = new float3[3];
            for (var i = 0; i < 3; i++)
            {
                var filter = new LandmarkFilterState();
                LandmarkRender.ResolvePoint(
                    0.5f, 0.4f + 0.1f * i, 0.5f - 0.1f * i, 2f, 0.5f, 1,
                    ref filter, 0,
                    new float3(1f), new float3(0.01f), 1f, 1000L,
                    0f, 0, in mapping, out points[i]);
            }

            // 광선별 확대를 쓰면 구간별 XY 간격이 달라진다. 전방 오프셋은 균일해야 한다.
            var first = points[1] - points[0];
            var second = points[2] - points[1];
            Assert.That((second - first).x, Is.EqualTo(0f).Within(1e-5f));
            Assert.That((second - first).y, Is.EqualTo(0f).Within(1e-5f));
            Assert.That((second - first).z, Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void DepthBounds_CapsRunawayExtent()
        {
            var mapping = ShapeMapping();
            var bounds = new LandmarkDepthBounds();
            bounds.Add(new float2(0.1f, 0.1f), new float3(0f, 0f, 0f));
            bounds.Add(new float2(0.9f, 0.9f), new float3(0.01f, 0.01f, 0.4f));
            var resolved = bounds.Resolve(in mapping);
            var maxExtent = 15f * LandmarkOverlayMapping.MaxTargetDepthFraction;
            Assert.That(resolved.x * 0.4f, Is.LessThanOrEqualTo(maxExtent + 1e-5f));
            Assert.That(resolved.x, Is.EqualTo(maxExtent / 0.4f).Within(1e-4f));
            Assert.That(resolved.y, Is.EqualTo(0.4f).Within(1e-6f));
        }
    }
}
