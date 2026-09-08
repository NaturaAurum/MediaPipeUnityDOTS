using System.Reflection;
using MediaPipeUnityDots.Runtime.Tracking;
using NUnit.Framework;
using Unity.Entities;
using UnityEngine;

namespace MediaPipeUnityDots.Tests.EditMode
{
    public sealed class DepthQueryLifetimeTests
    {
        [Test]
        public void DestroyedQueryWorld_DisableDoesNotDisposeDeadQueries()
        {
            var go = new GameObject("Depth query lifetime");
            go.SetActive(false);
            var provider = go.AddComponent<DepthFrameProvider>();
            var world = new World("Depth query owner");
            try
            {
                var type = typeof(DepthFrameProvider);
                type.GetField("_ecsWorld", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(provider, world);
                type.GetMethod("EnsureQueries", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(provider, new object[] { world.EntityManager });
                world.Dispose();
                // 원래 World가 먼저 파괴되는 Play 종료 순서를 재현한다.
                var disable = type.GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic);
                disable.Invoke(provider, null);
                disable.Invoke(provider, null);
                Assert.IsFalse((bool)type.GetField("_queriesCreated", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(provider));
            }
            finally
            {
                if (world.IsCreated) world.Dispose();
                Object.DestroyImmediate(go);
            }
        }
    }
}
