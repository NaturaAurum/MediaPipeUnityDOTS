using MediaPipeUnityDots.Runtime.Ecs;
using NUnit.Framework;
using Unity.Entities;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// 스트림 싱글턴 단일 작성자 계약 검증. 먼저 획득한 작성자만 쓰고, 나머지는 실패한다.
    /// </summary>
    public sealed class TrackingWriterOwnershipTests
    {
        private World _world;

        [SetUp]
        public void SetUp()
        {
            _world = new World("OwnershipTestWorld");
        }

        [TearDown]
        public void TearDown()
        {
            if (_world != null && _world.IsCreated)
            {
                _world.Dispose();
            }

            _world = null;
        }

        [Test]
        public void FirstWriterAcquires_SecondWriterFails()
        {
            var entityManager = _world.EntityManager;
            var singleton = HandTrackingSingletonUtil.GetOrCreateSingleton(entityManager);

            Assert.IsTrue(TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, 7UL));
            Assert.IsFalse(TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, 9UL));
            Assert.IsTrue(TrackingWriterOwnershipUtil.IsOwner(entityManager, singleton, 7UL));
            Assert.IsFalse(TrackingWriterOwnershipUtil.IsOwner(entityManager, singleton, 9UL));
        }

        [Test]
        public void NonOwnerCannotRelease_OwnerReleaseOpensForNextWriter()
        {
            var entityManager = _world.EntityManager;
            var singleton = HandTrackingSingletonUtil.GetOrCreateSingleton(entityManager);

            Assert.IsTrue(TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, 7UL));
            TrackingWriterOwnershipUtil.Release(entityManager, singleton, 9UL);
            Assert.IsTrue(TrackingWriterOwnershipUtil.IsOwner(entityManager, singleton, 7UL));

            TrackingWriterOwnershipUtil.Release(entityManager, singleton, 7UL);
            Assert.IsTrue(TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, 9UL));
        }

        [Test]
        public void ReaderCreatedSingleton_RemainsAcquirable()
        {
            var entityManager = _world.EntityManager;
            var singleton = HandTrackingSingletonUtil.GetOrCreateSingleton(entityManager);

            Assert.IsTrue(entityManager.HasComponent<TrackingWriterOwnership>(singleton));
            Assert.IsTrue(TrackingWriterOwnershipUtil.TryAcquire(entityManager, singleton, 7UL));
        }
    }
}
