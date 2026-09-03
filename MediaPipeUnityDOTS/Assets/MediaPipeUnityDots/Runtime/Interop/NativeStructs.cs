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
        public int isValid;
        public int landmarkCount;
        public int handedness;
        public float score;
        public long timestampUs;

        // 21 landmarks × 5 floats (x, y, z, visibility, presence) = 105 floats
        // C 측 MpudNormalizedLandmark landmarks[21] 과 동일한 420 bytes
        public fixed float landmarkData[105];

        public MpudNormalizedLandmark GetLandmark(int i)
        {
            if (i < 0 || i >= landmarkCount)
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = i * 5;
            return new MpudNormalizedLandmark
            {
                x = landmarkData[offset],
                y = landmarkData[offset + 1],
                z = landmarkData[offset + 2],
                visibility = landmarkData[offset + 3],
                presence = landmarkData[offset + 4],
            };

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
