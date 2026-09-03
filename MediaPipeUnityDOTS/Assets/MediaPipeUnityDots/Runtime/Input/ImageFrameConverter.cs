using System;
using System.Runtime.InteropServices;
using MediaPipeUnityDots.Runtime.Interop;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Input
{
    /// <summary>
    /// Color32[] 프레임을 MpudImageFrame으로 변환하는 유틸.
    /// 호출자가 GCHandle과 flip 버퍼의 수명을 관리한다.
    /// </summary>
    public static class ImageFrameConverter
    {
        private const int BytesPerPixel = 4;

        /// <summary>
        /// Color32[]을 상하 반전하여 destination에 복사한다.
        /// source와 destination은 같은 길이(width * height)여야 한다.
        /// </summary>
        public static void FlipVertical(Color32[] source, Color32[] destination, int width, int height)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (ReferenceEquals(source, destination))
            {
                throw new ArgumentException("source and destination must be different arrays.", nameof(destination));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            var expectedLength = checked(width * height);
            if (source.Length != expectedLength)
            {
                throw new ArgumentException("source length must match width * height.", nameof(source));
            }

            if (destination.Length != expectedLength)
            {
                throw new ArgumentException("destination length must match width * height.", nameof(destination));
            }

            for (var row = 0; row < height; row++)
            {
                var sourceIndex = (height - 1 - row) * width;
                var destinationIndex = row * width;
                Array.Copy(source, sourceIndex, destination, destinationIndex, width);
            }
        }

        /// <summary>
        /// pinned Color32[]로부터 MpudImageFrame을 구성한다.
        /// pinnedHandle은 caller가 GCHandle.Alloc(array, Pinned)로 잡아야 하며,
        /// submit 완료 후 caller가 해제한다.
        /// </summary>
        public static MpudImageFrame CreateFrame(GCHandle pinnedHandle, int width, int height, long timestampUs)
        {
            if (!pinnedHandle.IsAllocated)
            {
                throw new ArgumentException("pinnedHandle must be allocated.", nameof(pinnedHandle));
            }

            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            return new MpudImageFrame
            {
                data = pinnedHandle.AddrOfPinnedObject(),
                width = width,
                height = height,
                strideBytes = checked(width * BytesPerPixel),
                pixelFormat = MpudPixelFormat.Srgba,
                timestampUs = timestampUs,
            };
        }
    }
}
