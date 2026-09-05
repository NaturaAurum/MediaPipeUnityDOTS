using System.Runtime.InteropServices;
using MediaPipeUnityDots.Runtime.Interop;
using NUnit.Framework;

namespace MediaPipeUnityDots.Tests.EditMode
{
    /// <summary>
    /// C# 미러 구조체와 네이티브 static_assert 쌍방이 같은 크기를 보는지 검증한다.
    /// fixed 배열 증설 누락 같은 ABI 드리프트를 머지 전에 잡는다.
    /// </summary>
    public sealed class NativeAbiTests
    {
        [Test]
        public void ResultSizes_MatchExpected()
        {
            Assert.AreEqual(MpudHandResult.ExpectedSize, Marshal.SizeOf<MpudHandResult>(), nameof(MpudHandResult));
            Assert.AreEqual(MpudFaceResult.ExpectedSize, Marshal.SizeOf<MpudFaceResult>(), nameof(MpudFaceResult));
            Assert.AreEqual(MpudPoseResult.ExpectedSize, Marshal.SizeOf<MpudPoseResult>(), nameof(MpudPoseResult));
            Assert.AreEqual(MpudHolisticResult.ExpectedSize, Marshal.SizeOf<MpudHolisticResult>(), nameof(MpudHolisticResult));
        }
    }
}
