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

        // 손당: landmarkCount(int) + handedness(int) + score(float) + 105 floats + 105 world floats = 213 floats.
        // C MpudHandResult와 바이트 일치(handCount int + pad + timestamp long + 4×852).
        // 네이티브 헤더의 static assert와 쌍을 이룸. ExpectedSize 불일치는 ABI 드리프트다.
        public const int ExpectedSize = 3424;

        private const int FloatsPerHand = 213;

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

        public MpudNormalizedLandmark GetHandWorldLandmark(int hand, int i)
        {
            CheckHand(hand);
            if (i < 0 || i >= GetHandLandmarkCount(hand))
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = hand * FloatsPerHand + 3 + LandmarksPerHand * 5 + i * 5;
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

        // 포즈당: landmarkCount(int) + 165 floats + 165 world floats = 331 floats.
        // C MpudPoseResult와 바이트 일치(poseCount int + pad + timestamp long + 2×1324).
        // 네이티브 헤더의 static assert와 쌍을 이룸. ExpectedSize 불일치는 ABI 드리프트다.
        public const int ExpectedSize = 2664;

        private const int FloatsPerPose = 331;

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

        public MpudNormalizedLandmark GetPoseWorldLandmark(int pose, int i)
        {
            CheckPose(pose);
            if (i < 0 || i >= GetPoseLandmarkCount(pose))
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = pose * FloatsPerPose + 1 + LandmarksPerPose * 5 + i * 5;
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
    [StructLayout(LayoutKind.Sequential)]
    public unsafe struct MpudHolisticResult
    {
        public const int FaceLandmarks = 478;
        public const int PoseLandmarks = 33;
        public const int HandLandmarks = 21;

        // 부위별: count(int) 4개 + timestamp(long) + 2765 floats + 375 world floats = 12584.
        // C MpudHolisticResult와 바이트 일치 (8바이트 정렬, 끝 패딩 없음).
        public const int ExpectedSize = 12584;

        private const int FaceFloats = FaceLandmarks * 5;
        private const int PoseFloats = PoseLandmarks * 5;
        private const int HandFloats = HandLandmarks * 5;
        private const int PoseWorldFloats = PoseLandmarks * 5;
        private const int HandWorldFloats = HandLandmarks * 5;

        public int faceLandmarkCount;
        public int poseLandmarkCount;
        public int leftHandLandmarkCount;
        public int rightHandLandmarkCount;
        public long timestampUs;
        public fixed float landmarkData[
            FaceFloats + PoseFloats + HandFloats * 2 + PoseWorldFloats + HandWorldFloats * 2];
        public MpudNormalizedLandmark GetFaceLandmark(int i)
        {
            return GetLandmark(0, FaceLandmarks, faceLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetPoseLandmark(int i)
        {
            return GetLandmark(FaceFloats, PoseLandmarks, poseLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetLeftHandLandmark(int i)
        {
            return GetLandmark(FaceFloats + PoseFloats, HandLandmarks, leftHandLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetRightHandLandmark(int i)
        {
            return GetLandmark(FaceFloats + PoseFloats + HandFloats, HandLandmarks, rightHandLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetPoseWorldLandmark(int i)
        {
            return GetLandmark(FaceFloats + PoseFloats + HandFloats * 2, PoseLandmarks, poseLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetLeftHandWorldLandmark(int i)
        {
            return GetLandmark(
                FaceFloats + PoseFloats + HandFloats * 2 + PoseWorldFloats,
                HandLandmarks, leftHandLandmarkCount, i);
        }

        public MpudNormalizedLandmark GetRightHandWorldLandmark(int i)
        {
            return GetLandmark(
                FaceFloats + PoseFloats + HandFloats * 2 + PoseWorldFloats + HandWorldFloats,
                HandLandmarks, rightHandLandmarkCount, i);
        }

        private MpudNormalizedLandmark GetLandmark(int baseOffset, int capacity, int count, int i)
        {
            if (i < 0 || i >= count || i >= capacity)
            {
                throw new ArgumentOutOfRangeException(nameof(i));
            }

            var offset = baseOffset + i * 5;
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
    public struct MpudHolisticTrackerConfig
    {
        public IntPtr modelAssetPath;
        public float minDetectionConfidence;
        public float minPresenceConfidence;
    }
}
