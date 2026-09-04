using System;
using System.Runtime.InteropServices;

namespace MediaPipeUnityDots.Runtime.Interop
{
    public static class MpudStatus
    {
        public const int Ok = 0;
        public const int Error = -1;
        public const int NoResult = -2;
    }

    public static class MpudPixelFormat
    {
        public const int Srgb = 0;
        public const int Srgba = 1;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpudNormalizedLandmark
    {
        public float x;
        public float y;
        public float z;
        public float visibility;
        public float presence;
    }

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MpudHandResult
    {
        public const int MaxHands = 4;
        public const int LandmarksPerHand = 21;

        // 손당: landmarkCount(int) + handedness(int) + score(float) + 105 floats = 108 floats.
        // C MpudHandResult와 바이트 일치(handCount int + pad + timestamp long + 4×432).
        // 네이티브 헤더의 static assert와 쌍을 이룸. ExpectedSize 불일치는 ABI 드리프트다.
        public const int ExpectedSize = 1744;

        private const int FloatsPerHand = 108;

        public int handCount;
        public long timestampUs;
        public fixed float handData[MaxHands * FloatsPerHand];

        public int GetHandLandmarkCount(int hand)
        {
            CheckHand(hand);
            fixed (float* data = handData)
            {
                return *(int*)(data + hand * FloatsPerHand);
            }
        }

        public int GetHandedness(int hand)
        {
            CheckHand(hand);
            fixed (float* data = handData)
            {
                return *(int*)(data + hand * FloatsPerHand + 1);
            }
        }

        public float GetHandScore(int hand)
        {
            CheckHand(hand);
            return handData[hand * FloatsPerHand + 2];
        }

        public MpudNormalizedLandmark GetHandLandmark(int hand, int i)
        {
            CheckHand(hand);
            if (i < 0 || i >= GetHandLandmarkCount(hand))
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = hand * FloatsPerHand + 3 + i * 5;
            return new MpudNormalizedLandmark
            {
                x = handData[offset],
                y = handData[offset + 1],
                z = handData[offset + 2],
                visibility = handData[offset + 3],
                presence = handData[offset + 4],
            };
        }

        private static void CheckHand(int hand)
        {
            if (hand < 0 || hand >= MaxHands)
            {
                throw new ArgumentOutOfRangeException(nameof(hand));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpudImageFrame
    {
        public IntPtr data;
        public int width;
        public int height;
        public int strideBytes;
        public int pixelFormat;
        public long timestampUs;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpudHandTrackerConfig
    {
        public IntPtr modelAssetPath;
        public int numHands;
        public float minDetectionConfidence;
        public float minTrackingConfidence;
        public int runningMode; // PoC: 무시됨. 내부적으로 VIDEO(1) 고정.
    }
}
