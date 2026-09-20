using System;
using System.Collections.Generic;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 두 landmark 인덱스를 잇는 MediaPipe 기본 연결선.
    /// </summary>
    public readonly struct LandmarkConnection : IEquatable<LandmarkConnection>
    {
        public LandmarkConnection(int start, int end)
        {
            Start = start;
            End = end;
        }

        public int Start { get; }
        public int End { get; }

        public bool Equals(LandmarkConnection other) => Start == other.Start && End == other.End;
        public override bool Equals(object obj) => obj is LandmarkConnection other && Equals(other);
        public override int GetHashCode() => (Start * 397) ^ End;
        public override string ToString() => $"({Start}, {End})";
    }

    /// <summary>
    /// MediaPipe HandLandmarker의 21개 landmark 인덱스.
    /// </summary>
    public enum HandLandmark
    {
        Wrist = 0,
        ThumbCmc = 1,
        ThumbMcp = 2,
        ThumbIp = 3,
        ThumbTip = 4,
        IndexFingerMcp = 5,
        IndexFingerPip = 6,
        IndexFingerDip = 7,
        IndexFingerTip = 8,
        MiddleFingerMcp = 9,
        MiddleFingerPip = 10,
        MiddleFingerDip = 11,
        MiddleFingerTip = 12,
        RingFingerMcp = 13,
        RingFingerPip = 14,
        RingFingerDip = 15,
        RingFingerTip = 16,
        PinkyMcp = 17,
        PinkyPip = 18,
        PinkyDip = 19,
        PinkyTip = 20,
    }

    /// <summary>
    /// MediaPipe HandLandmarker의 기본 연결선 순서.
    /// </summary>
    public static class HandLandmarks
    {
        private static readonly LandmarkConnection[] _connections =
        {
            new(0, 1), new(0, 5), new(9, 13), new(13, 17), new(5, 9), new(0, 17),
            new(1, 2), new(2, 3), new(3, 4),
            new(5, 6), new(6, 7), new(7, 8),
            new(9, 10), new(10, 11), new(11, 12),
            new(13, 14), new(14, 15), new(15, 16),
            new(17, 18), new(18, 19), new(19, 20),
        };

        public const int Count = 21;
        public static IReadOnlyList<LandmarkConnection> Connections => _connections;
    }


    /// <summary>
    /// MediaPipe PoseLandmarker의 33개 landmark 인덱스.
    /// </summary>
    public enum PoseLandmark
    {

        Nose = 0,
        LeftEyeInner = 1,
        LeftEye = 2,
        LeftEyeOuter = 3,
        RightEyeInner = 4,
        RightEye = 5,
        RightEyeOuter = 6,
        LeftEar = 7,
        RightEar = 8,
        MouthLeft = 9,
        MouthRight = 10,
        LeftShoulder = 11,
        RightShoulder = 12,
        LeftElbow = 13,
        RightElbow = 14,
        LeftWrist = 15,
        RightWrist = 16,
        LeftPinky = 17,
        RightPinky = 18,
        LeftIndex = 19,
        RightIndex = 20,
        LeftThumb = 21,
        RightThumb = 22,
        LeftHip = 23,
        RightHip = 24,
        LeftKnee = 25,
        RightKnee = 26,
        LeftAnkle = 27,
        RightAnkle = 28,
        LeftHeel = 29,
        RightHeel = 30,
        LeftFootIndex = 31,
        RightFootIndex = 32,
    }

    /// <summary>
    /// MediaPipe PoseLandmarker의 기본 연결선 순서.
    /// </summary>
    public static class PoseLandmarks
    {
        private static readonly LandmarkConnection[] _connections =
        {
            new(0, 4), new(4, 5), new(5, 6), new(6, 8),
            new(0, 1), new(1, 2), new(2, 3), new(3, 7),
            new(10, 9), new(12, 11),
            new(12, 14), new(14, 16), new(16, 18), new(16, 20), new(16, 22), new(18, 20),
            new(11, 13), new(13, 15), new(15, 17), new(15, 19), new(15, 21), new(17, 19),
            new(12, 24), new(11, 23), new(24, 23),
            new(24, 26), new(23, 25), new(26, 28), new(25, 27),
            new(28, 30), new(27, 29), new(30, 32), new(29, 31), new(28, 32), new(27, 31),
        };

        public const int Count = 33;
        public static IReadOnlyList<LandmarkConnection> Connections => _connections;
    }


    /// <summary>
    /// MediaPipe FaceLandmarker graph가 출력하는 blendshape 순서(52개).
    /// </summary>
    public static class FaceBlendshapeNames

    {
        private static readonly string[] _names =
        {
            "_neutral", "browDownLeft", "browDownRight", "browInnerUp",
            "browOuterUpLeft", "browOuterUpRight", "cheekPuff", "cheekSquintLeft",
            "cheekSquintRight", "eyeBlinkLeft", "eyeBlinkRight", "eyeLookDownLeft",
            "eyeLookDownRight", "eyeLookInLeft", "eyeLookInRight", "eyeLookOutLeft",
            "eyeLookOutRight", "eyeLookUpLeft", "eyeLookUpRight", "eyeSquintLeft",
            "eyeSquintRight", "eyeWideLeft", "eyeWideRight", "jawForward", "jawLeft",
            "jawOpen", "jawRight", "mouthClose", "mouthDimpleLeft", "mouthDimpleRight",
            "mouthFrownLeft", "mouthFrownRight", "mouthFunnel", "mouthLeft",
            "mouthLowerDownLeft", "mouthLowerDownRight", "mouthPressLeft", "mouthPressRight",
            "mouthPucker", "mouthRight", "mouthRollLower", "mouthRollUpper", "mouthShrugLower",
            "mouthShrugUpper", "mouthSmileLeft", "mouthSmileRight", "mouthStretchLeft",
            "mouthStretchRight", "mouthUpperUpLeft", "mouthUpperUpRight", "noseSneerLeft",
            "noseSneerRight",
        };

        public const int Count = 52;
        public static IReadOnlyList<string> Names => _names;

        public static int GetIndex(string name)
        {
            var index = Array.IndexOf(_names, name);
            if (index < 0)
                throw new ArgumentException("알 수 없는 Face blendshape 이름입니다.", nameof(name));
            return index;
        }

        public static string GetName(int index)
        {
            if (index < 0 || index >= _names.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return _names[index];
        }
    }
}
