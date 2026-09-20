using System;
using MediaPipeUnityDots.Runtime.Ecs;
using Unity.Collections;
using Unity.Entities;
using MediaPipeUnityDots.Runtime.Interop;
using MediaPipeUnityDots.Runtime.Tracking.Filtering;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    public sealed class LandmarkFilterPipelineTests
    {
        [Test]
        public void CustomFilter_WritesCallerOutput_AndLeavesRawUntouched()
        {
            var filter = new OffsetFilter(1f);
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var raw = new[] { Landmark(0.25f) };
            var output = new MpudNormalizedLandmark[raw.Length];
            var context = Context(0, 10, 7, 11);

            Assert.IsTrue(coordinator.TryProcess(raw, output, in context, out var count));
            Assert.AreEqual(1, count);
            Assert.AreEqual(0.25f, raw[0].x, 1e-6f);
            Assert.AreEqual(1.25f, output[0].x, 1e-6f);
        }

        [Test]
        public void SameTimestamp_IsIdempotent_AndDoesNotInvokeCustomFilterAgain()
        {
            var filter = new OffsetFilter(1f);
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var firstInput = new[] { Landmark(1f) };
            var secondInput = new[] { Landmark(9f) };
            var output = new MpudNormalizedLandmark[1];
            var firstContext = Context(0, 10, 7, 11, 100, 1);
            var duplicateContext = Context(0, 10, 7, 11, 200, 2);

            Assert.IsTrue(coordinator.TryProcess(firstInput, output, in firstContext, out _));
            Assert.AreEqual(1, filter.CallCount);
            Assert.IsTrue(coordinator.TryProcess(secondInput, output, in duplicateContext, out _));
            Assert.AreEqual(1, filter.CallCount);
            Assert.AreEqual(2f, output[0].x, 1e-6f);
        }

        [Test]
        public void EpochBackwardTimeAndContinuityChange_ResetState()
        {
            var filter = new StatefulFilter();
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var input = new[] { Landmark(0f) };
            var output = new MpudNormalizedLandmark[1];

            var first = Context(0, 10, 7, 11);
            Assert.IsTrue(coordinator.TryProcess(input, output, in first, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);

            var next = Context(0, 11, 7, 11);
            Assert.IsTrue(coordinator.TryProcess(input, output, in next, out _));
            Assert.AreEqual(2f, output[0].x, 1e-6f);

            var backwards = Context(0, 9, 7, 11);
            Assert.IsTrue(coordinator.TryProcess(input, output, in backwards, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);

            var newEpoch = Context(0, 10, 8, 11);
            Assert.IsTrue(coordinator.TryProcess(input, output, in newEpoch, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);

            var newContinuity = Context(0, 11, 8, 12);
            Assert.IsTrue(coordinator.TryProcess(input, output, in newContinuity, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);
        }

        [Test]
        public void DifferentTargetAndCoordinate_DoNotShareState()
        {
            var filter = new StatefulFilter();
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var input = new[] { Landmark(0f) };
            var output = new MpudNormalizedLandmark[1];
            var targetZero = Context(0, 10, 7, 11);
            var targetOne = Context(1, 10, 7, 11);
            var world = new LandmarkFilterContext(
                1,
                LandmarkFilterTracker.Hand,
                0,
                LandmarkFilterCoordinate.ModelWorld,
                11,
                7,
                11);

            Assert.IsTrue(coordinator.TryProcess(input, output, in targetZero, out _));
            Assert.IsTrue(coordinator.TryProcess(input, output, in targetOne, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);
            Assert.IsTrue(coordinator.TryProcess(input, output, in world, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);
        }

        [Test]
        public void CustomException_InvalidatesOutput()
        {
            using var coordinator = new LandmarkFilterCoordinator(new ThrowingFilter());
            var output = new[] { Landmark(42f) };
            var context = Context(0, 10, 7, 11);

            Assert.IsFalse(coordinator.TryProcess(new[] { Landmark(1f) }, output, in context, out var count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(LandmarkFilterResult.FilterError, coordinator.LastResult);
            Assert.AreEqual(0f, output[0].x, 1e-6f);
            Assert.IsFalse(coordinator.HasValidOutput);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void CustomResetException_InvalidatesOutputEvenWithoutInput(bool empty)
        {
            var settings = OneEuroFilterSettings.Default;
            using var coordinator = LandmarkFilterCoordinator.CreateOneEuro(in settings, LandmarkFilterTracker.Hand);
            var input = empty ? Array.Empty<MpudNormalizedLandmark>() : new[] { Landmark(1f) };
            var output = new[] { Landmark(42f) };
            var context = Context(0, 10, 7, 11);
            Assert.IsTrue(coordinator.TryProcess(new[] { Landmark(1f) }, output, in context, out _));
            Assert.Throws<InvalidOperationException>(() =>
                coordinator.ReplaceFilter(new ThrowingFilter(failOnReset: true)));

            Assert.IsFalse(coordinator.TryProcess(input, output, in context, out var count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(LandmarkFilterResult.FilterError, coordinator.LastResult);
            Assert.AreEqual(0f, output[0].x);
            Assert.IsFalse(coordinator.HasValidOutput);
        }

        [Test]
        public void InvalidOutputSpan_IsReportedAndCleared()
        {
            using var coordinator = new LandmarkFilterCoordinator(new OffsetFilter(1f));
            var output = new[] { Landmark(42f) };
            var input = new[] { Landmark(1f), Landmark(2f) };
            var context = Context(0, 10, 7, 11);

            Assert.IsFalse(coordinator.TryProcess(input, output, in context, out var count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(LandmarkFilterResult.InvalidOutput, coordinator.LastResult);
            Assert.AreEqual(0f, output[0].x, 1e-6f);
        }

        [Test]
        public void OverlappingInputAndOutput_IsRejectedWithoutMutatingRaw()
        {
            using var coordinator = new LandmarkFilterCoordinator(new OffsetFilter(1f));
            var raw = new[] { Landmark(3f), Landmark(4f) };
            var original = raw[0];
            var context = Context(0, 10, 7, 11);

            Assert.IsFalse(coordinator.TryProcess(raw, raw, in context, out var count));
            Assert.AreEqual(0, count);
            Assert.AreEqual(LandmarkFilterResult.InvalidOutput, coordinator.LastResult);
            Assert.AreEqual(original.x, raw[0].x, 1e-6f);
            Assert.AreEqual(original.y, raw[0].y, 1e-6f);
            Assert.AreEqual(original.z, raw[0].z, 1e-6f);
        }

        [Test]
        public void Reset_ClearsCachedOutputAndAdvancesFilterAgain()
        {
            var filter = new StatefulFilter();
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var input = new[] { Landmark(0f) };
            var output = new MpudNormalizedLandmark[1];
            var context = Context(0, 10, 7, 11);

            Assert.IsTrue(coordinator.TryProcess(input, output, in context, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);
            coordinator.Reset();
            Assert.IsTrue(coordinator.TryProcess(input, output, in context, out _));
            Assert.AreEqual(1f, output[0].x, 1e-6f);
            Assert.IsFalse(coordinator.LastResult == LandmarkFilterResult.NoData);
        }

        [Test]
        public void BorrowedFilter_IsNotDisposed_AndOwnedFilterIsDisposed()
        {
            var borrowed = new TrackingFilter();
            using (var coordinator = new LandmarkFilterCoordinator(borrowed))
            {
            }
            Assert.IsFalse(borrowed.Disposed);

            var owned = new TrackingFilter();
            using (var coordinator = new LandmarkFilterCoordinator(owned, true))
            {
            }
            Assert.IsTrue(owned.Disposed);
        }

        [Test]
        public void WarmedOneEuroFilter_HasNoSteadyStateManagedAllocation()
        {
            using var filter = new OneEuroLandmarkFilter();
            using var coordinator = new LandmarkFilterCoordinator(filter);
            var input = new[] { Landmark(0f) };
            var output = new MpudNormalizedLandmark[1];
            var context = Context(0, 10, 7, 11);
            coordinator.TryProcess(input, output, in context, out _);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 1; i <= 30; i++)
            {
                var frame = Context(0, 10 + i, 7, 11);
                coordinator.TryProcess(input, output, in frame, out _);
            }

            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.AreEqual(0L, allocated, "워밍업 이후 기본 필터는 프레임별 managed 할당을 만들지 않아야 한다.");
        }

        [Test]
        public void EcsFilter_PreservesRawWithoutRendererAndResetsOnLossOrIdentityChange()
        {
            using var world = new World("Landmark filter contract");
            var manager = world.EntityManager;
            var system = world.GetOrCreateSystem<LandmarkFilterSystem>();
            var source = HandTrackingSingletonUtil.GetOrCreateSingleton(manager);
            manager.AddBuffer<LandmarkContinuityElement>(source).Add(new LandmarkContinuityElement { Token = 1 });
            var settings = OneEuroFilterSettings.Default;
            settings.Enabled = 1;
            settings.HandBeta = 0f;
            manager.CreateSingleton(settings);
            using var query = manager.CreateEntityQuery(typeof(FilteredLandmarkStatus));
            using var outputs = query.ToEntityArray(Allocator.Temp);
            var handOutput = Entity.Null;
            foreach (var entity in outputs)
                if (manager.GetComponentData<FilteredLandmarkStatus>(entity).Tracker == LandmarkTracker.Hand)
                    handOutput = entity;
            Assert.AreNotEqual(Entity.Null, handOutput);

            WriteHandFrame(manager, source, 0f, 1_000_000);
            system.Update(world.Unmanaged);
            WriteHandFrame(manager, source, 1f, 1_033_333);
            system.Update(world.Unmanaged);
            var filtered = manager.GetBuffer<FilteredLandmarkElement>(handOutput)[0].X;
            Assert.That(filtered, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, manager.GetBuffer<LandmarkElement>(source)[0].X);
            Assert.That(manager.GetBuffer<FilteredWorldLandmarkElement>(handOutput)[0].X,
                Is.GreaterThan(1f).And.LessThan(2f));
            system.Update(world.Unmanaged);
            Assert.AreEqual(filtered, manager.GetBuffer<FilteredLandmarkElement>(handOutput)[0].X);

            var continuity = manager.GetBuffer<LandmarkContinuityElement>(source);
            continuity[0] = new LandmarkContinuityElement { Token = 2 };
            system.Update(world.Unmanaged);
            Assert.AreEqual(1f, manager.GetBuffer<FilteredLandmarkElement>(handOutput)[0].X);
            var status = manager.GetComponentData<HandTrackingStatus>(source);
            status.IsValid = false;
            manager.SetComponentData(source, status);
            system.Update(world.Unmanaged);
            var destination = new[] { Landmark(42f) };
            Assert.IsFalse(FilteredLandmarkReader.TryCopy(
                manager.GetComponentData<FilteredLandmarkStatus>(handOutput),
                manager.GetBuffer<FilteredLandmarkElement>(handOutput), destination, out _));
            Assert.AreEqual(0f, destination[0].x);

            WriteHandFrame(manager, source, 0.75f, 1_066_666);
            system.Update(world.Unmanaged);
            Assert.AreEqual(0.75f, manager.GetBuffer<FilteredLandmarkElement>(handOutput)[0].X);
            continuity = manager.GetBuffer<LandmarkContinuityElement>(source);
            continuity[0] = default;
            WriteHandFrame(manager, source, 0.1f, 1_099_999);
            system.Update(world.Unmanaged);
            WriteHandFrame(manager, source, 0.9f, 1_133_332);
            system.Update(world.Unmanaged);
            Assert.AreEqual(0.9f, manager.GetBuffer<FilteredLandmarkElement>(handOutput)[0].X);
        }

        private static void WriteHandFrame(EntityManager manager, Entity entity, float x, long timestampUs)
        {
            manager.SetComponentData(entity, new HandTrackingStatus
            {
                IsValid = true, HandCount = 1, TimestampUs = timestampUs, CaptureEpoch = 1,
            });
            var image = manager.GetBuffer<LandmarkElement>(entity);
            var modelWorld = manager.GetBuffer<HandWorldLandmarkElement>(entity);
            image.ResizeUninitialized(MpudHandResult.LandmarksPerHand);
            modelWorld.ResizeUninitialized(MpudHandResult.LandmarksPerHand);
            for (var i = 0; i < image.Length; i++)
            {
                image[i] = new LandmarkElement { X = x, HandIndex = 0 };
                modelWorld[i] = new HandWorldLandmarkElement { X = x + 1f, HandIndex = 0 };
            }
        }

        private static LandmarkFilterContext Context(
            int target,
            long timestamp,
            long epoch,
            ulong continuity,
            long captureId = 0,
            long frameCount = 0)
        {
            return new LandmarkFilterContext(
                1,
                LandmarkFilterTracker.Hand,
                target,
                LandmarkFilterCoordinate.NormalizedImage,
                timestamp,
                epoch,
                continuity,
                captureId,
                frameCount);
        }

        private static MpudNormalizedLandmark Landmark(float value)
        {
            return new MpudNormalizedLandmark
            {
                x = value,
                y = value,
                z = value,
                visibility = 1f,
                presence = 1f,
            };
        }

        private sealed class OffsetFilter : ILandmarkFilter
        {
            private readonly float _offset;

            public OffsetFilter(float offset)
            {
                _offset = offset;
            }

            public int CallCount { get; private set; }

            public int Filter(
                ReadOnlySpan<MpudNormalizedLandmark> input,
                Span<MpudNormalizedLandmark> output,
                in LandmarkFilterContext context)
            {
                CallCount++;
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
            }
        }

        private sealed class StatefulFilter : ILandmarkFilter
        {
            private int _state;

            public int Filter(
                ReadOnlySpan<MpudNormalizedLandmark> input,
                Span<MpudNormalizedLandmark> output,
                in LandmarkFilterContext context)
            {
                _state++;
                for (var i = 0; i < input.Length; i++)
                {
                    var value = input[i];
                    value.x = _state;
                    output[i] = value;
                }

                return input.Length;
            }

            public void Reset()
            {
                _state = 0;
            }

            public void Dispose()
            {
            }
        }

        private sealed class ThrowingFilter : ILandmarkFilter
        {
            private readonly bool _failOnReset;

            public ThrowingFilter(bool failOnReset = false)
            {
                _failOnReset = failOnReset;
            }

            public int Filter(
                ReadOnlySpan<MpudNormalizedLandmark> input,
                Span<MpudNormalizedLandmark> output,
                in LandmarkFilterContext context)
            {
                throw new InvalidOperationException("custom filter failed");
            }

            public void Reset()
            {
                if (_failOnReset)
                {
                    throw new InvalidOperationException("custom reset failed");
                }
            }

            public void Dispose()
            {
            }
        }

        private sealed class TrackingFilter : ILandmarkFilter
        {
            public bool Disposed { get; private set; }

            public int Filter(
                ReadOnlySpan<MpudNormalizedLandmark> input,
                Span<MpudNormalizedLandmark> output,
                in LandmarkFilterContext context)
            {
                input.CopyTo(output);
                return input.Length;
            }

            public void Reset()
            {
            }

            public void Dispose()
            {
                Disposed = true;
            }
        }
    }
}
