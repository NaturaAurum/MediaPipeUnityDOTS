using System;
using MediaPipeUnityDots.Runtime.Interop;
using Unity.Entities;

namespace MediaPipeUnityDots.Runtime.Ecs
{
    /// <summary>
    /// 필터 시스템이 게시하는 트래커별 프레임 메타데이터다. managed 필터 참조는 포함하지 않는다.
    /// </summary>
    public struct FilteredLandmarkStatus : IComponentData
    {
        public LandmarkTracker Tracker;
        public ulong StreamId;
        public int IsValid;
        public int Count;
        public long TimestampUs;
        public long CaptureTimestampUs;
        public long FrameCount;
        public long CaptureId;
        public long CaptureEpoch;
    }
    /// <summary>
    /// 소비자가 raw tracker singleton에 추가할 수 있는 대상별 연속성 토큰이다.
    /// buffer[i]는 해당 프레임의 target i를 뜻하며 0은 연속성 미증명으로 매 프레임 상태를 reset한다.
    /// 토큰 생성·유효성은 소비자가 소유하고, 필터 시스템은 target ordinal을 ID로 사용하지 않는다.
    /// </summary>
    [InternalBufferCapacity(4)]
    public struct LandmarkContinuityElement : IBufferElementData
    {
        public ulong Token;
    }


    /// <summary>
    /// 정규화 이미지 좌표 filtered 결과. raw 버퍼와 별도 소유권을 가진다.
    /// </summary>
    [InternalBufferCapacity(84)]
    public struct FilteredLandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public float Visibility;
        public float Presence;
        public int Target;
        public int Index;
    }

    /// <summary>
    /// 모델 world 좌표 filtered 결과. 손·포즈에서만 채워질 수 있으며, Unity world 좌표가 아니다.
    /// </summary>
    [InternalBufferCapacity(84)]
    public struct FilteredWorldLandmarkElement : IBufferElementData
    {
        public float X;
        public float Y;
        public float Z;
        public float Visibility;
        public float Presence;
        public int Target;
        public int Index;
    }

    /// <summary>
    /// 기본 ECS 필터의 비관리 점별 상태. managed 인터페이스를 저장하지 않는다.
    /// </summary>
    [InternalBufferCapacity(84)]
    public struct FilteredLandmarkStateElement : IBufferElementData
    {
        public LandmarkFilterState State;
        public int Target;
        public int Index;
        public ulong ContinuityToken;
    }

    /// <summary>
    /// 렌더러 없이 ECS filtered 버퍼를 caller-owned span으로 복사하는 최소 reader다.
    /// </summary>
    public static class FilteredLandmarkReader
    {
        public static bool TryCopy(
            in FilteredLandmarkStatus status,
            DynamicBuffer<FilteredLandmarkElement> source,
            Span<MpudNormalizedLandmark> destination,
            out int count)
        {
            count = 0;
            destination.Clear();
            if (status.IsValid == 0 || status.Count < 0 || source.Length < status.Count
                || destination.Length < status.Count)
            {
                return false;
            }

            for (var i = 0; i < status.Count; i++)
            {
                var value = source[i];
                destination[i] = new MpudNormalizedLandmark
                {
                    x = value.X,
                    y = value.Y,
                    z = value.Z,
                    visibility = value.Visibility,
                    presence = value.Presence,
                };
            }

            count = status.Count;
            return true;
        }

        public static bool TryCopyWorld(
            in FilteredLandmarkStatus status,
            DynamicBuffer<FilteredWorldLandmarkElement> source,
            Span<MpudNormalizedLandmark> destination,
            out int count)
        {
            count = 0;
            destination.Clear();
            if (status.IsValid == 0 || status.Count < 0 || source.Length < status.Count
                || destination.Length < status.Count)
            {
                return false;
            }

            for (var i = 0; i < status.Count; i++)
            {
                var value = source[i];
                destination[i] = new MpudNormalizedLandmark
                {
                    x = value.X,
                    y = value.Y,
                    z = value.Z,
                    visibility = value.Visibility,
                    presence = value.Presence,
                };
            }

            count = status.Count;
            return true;
        }
    }
}
