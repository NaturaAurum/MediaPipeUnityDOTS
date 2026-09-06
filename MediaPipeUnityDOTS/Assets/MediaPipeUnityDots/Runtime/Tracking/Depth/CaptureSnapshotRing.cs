using System;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 동일 캡처의 Hand/Pose 랜드마크 스냅샷 보관. 깊이 완료 시 캡처 ID+세대로 조회한다.
    /// 배열 슬롯은 영속 ID가 아니므로 슬롯별 handedness도 함께 보관한다.
    /// 깊이 지연을 흡수하도록 약 1초분(32 캡처)을 유지한다.
    /// </summary>
    public sealed class CaptureSnapshotRing
    {
        public const int Capacity = 32;
        public const int MaxHands = 2;
        public const int HandLandmarks = 21;
        public const int PoseLandmarks = 33;

        public struct Snapshot
        {
            public long CaptureId;
            public long CaptureEpoch;
            public int SrcWidth;
            public int SrcHeight;
            public int HandCount;
            public int[] Handedness;
            public float[] HandXY;
            public int PoseCount;
            public float[] PoseXY;
        }

        private readonly Snapshot[] _slots = new Snapshot[Capacity];
        private int _next;

        public CaptureSnapshotRing()
        {
            for (var i = 0; i < Capacity; i++)
            {
                _slots[i] = new Snapshot
                {
                    Handedness = new int[MaxHands],
                    HandXY = new float[MaxHands * HandLandmarks * 2],
                    PoseXY = new float[PoseLandmarks * 2],
                };
            }
        }

        public void Add(long captureId, long captureEpoch, int srcWidth, int srcHeight, int handCount, int[] handedness, float[] handXY, int poseCount, float[] poseXY)
        {
            var slotIndex = -1;
            for (var i = 0; i < Capacity; i++)
            {
                if (_slots[i].CaptureId == captureId && _slots[i].CaptureEpoch == captureEpoch && captureId != 0)
                {
                    slotIndex = i;
                    break;
                }
            }

            if (slotIndex < 0)
            {
                slotIndex = _next;
                _next = (_next + 1) % Capacity;
            }

            var slot = _slots[slotIndex];
            slot.CaptureId = captureId;
            slot.CaptureEpoch = captureEpoch;
            slot.SrcWidth = srcWidth;
            slot.SrcHeight = srcHeight;
            slot.HandCount = Math.Max(0, Math.Min(handCount, MaxHands));
            Array.Clear(slot.Handedness, 0, slot.Handedness.Length);
            Array.Clear(slot.HandXY, 0, slot.HandXY.Length);
            if (handedness != null && handXY != null)
            {
                Array.Copy(handedness, slot.Handedness, Math.Min(handedness.Length, slot.Handedness.Length));
                Array.Copy(handXY, slot.HandXY, Math.Min(handXY.Length, slot.HandXY.Length));
            }

            slot.PoseCount = Math.Max(0, poseCount);
            Array.Clear(slot.PoseXY, 0, slot.PoseXY.Length);
            if (poseXY != null)
            {
                Array.Copy(poseXY, slot.PoseXY, Math.Min(poseXY.Length, slot.PoseXY.Length));
            }

            _slots[slotIndex] = slot;
        }

        public bool TryGet(long captureId, long captureEpoch, out Snapshot snapshot)
        {
            for (var i = 0; i < Capacity; i++)
            {
                if (_slots[i].CaptureId == captureId && _slots[i].CaptureEpoch == captureEpoch && captureId != 0)
                {
                    snapshot = _slots[i];
                    return true;
                }
            }

            snapshot = default;
            return false;
        }
    }
}
