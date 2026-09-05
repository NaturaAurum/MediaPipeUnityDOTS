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
    public unsafe struct MpudFaceResult
    {
        public const int MaxFaces = 2;
        public const int LandmarksPerFace = 478;

        // 얼굴당: landmarkCount(int) + 2390 floats = 2391 floats.
        // C MpudFaceResult와 바이트 일치(faceCount int + pad + timestamp long + 2×9564).
        // 네이티브 헤더의 static assert와 쌍을 이룸. ExpectedSize 불일치는 ABI 드리프트다.
        public const int ExpectedSize = 19144;

        private const int FloatsPerFace = 2391;

        public int faceCount;
        public long timestampUs;
        public fixed float faceData[MaxFaces * FloatsPerFace];

        public int GetFaceLandmarkCount(int face)
        {
            CheckFace(face);
            fixed (float* data = faceData)
            {
                return *(int*)(data + face * FloatsPerFace);
            }
        }

        public MpudNormalizedLandmark GetFaceLandmark(int face, int i)
        {
            CheckFace(face);
            if (i < 0 || i >= GetFaceLandmarkCount(face))
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = face * FloatsPerFace + 1 + i * 5;
            return new MpudNormalizedLandmark
            {
                x = faceData[offset],
                y = faceData[offset + 1],
                z = faceData[offset + 2],
                visibility = faceData[offset + 3],
                presence = faceData[offset + 4],
            };
        }

        private static void CheckFace(int face)
        {
            if (face < 0 || face >= MaxFaces)
            {
                throw new ArgumentOutOfRangeException(nameof(face));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpudFaceTrackerConfig
    {
        public IntPtr modelAssetPath;
        public int numFaces;
        public float minDetectionConfidence;
        public float minTrackingConfidence;
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

    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MpudPoseResult
    {
        public const int MaxPoses = 2;
        public const int LandmarksPerPose = 33;

        // 포즈당: landmarkCount(int) + 165 floats = 166 floats.
        // C MpudPoseResult와 바이트 일치(poseCount int + pad + timestamp long + 2×664).
        // 네이티브 헤더의 static assert와 쌍을 이룸. ExpectedSize 불일치는 ABI 드리프트다.
        public const int ExpectedSize = 1344;

        private const int FloatsPerPose = 166;

        public int poseCount;
        public long timestampUs;
        public fixed float poseData[MaxPoses * FloatsPerPose];

        public int GetPoseLandmarkCount(int pose)
        {
            CheckPose(pose);
            fixed (float* data = poseData)
            {
                return *(int*)(data + pose * FloatsPerPose);
            }
        }

        public MpudNormalizedLandmark GetPoseLandmark(int pose, int i)
        {
            CheckPose(pose);
            if (i < 0 || i >= GetPoseLandmarkCount(pose))
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = pose * FloatsPerPose + 1 + i * 5;
            return new MpudNormalizedLandmark
            {
                x = poseData[offset],
                y = poseData[offset + 1],
                z = poseData[offset + 2],
                visibility = poseData[offset + 3],
                presence = poseData[offset + 4],
            };
        }

        private static void CheckPose(int pose)
        {
            if (pose < 0 || pose >= MaxPoses)
            {
                throw new ArgumentOutOfRangeException(nameof(pose));
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MpudPoseTrackerConfig
    {
        public IntPtr modelAssetPath;
        public int numPoses;
        public float minDetectionConfidence;
        public float minTrackingConfidence;
    }
}
