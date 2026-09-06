using System;
using UnityEngine;

namespace MediaPipeUnityDots.Runtime.Tracking
{
    /// <summary>
    /// 깊이 입력 좌표 변환. 원본→패딩 입력→깊이맵 역변환을 함께 보존한다.
    /// padded = src * Scale + Pad 이며, 출력은 패딩 크기와 동일하다(14 배수).
    /// </summary>
    public struct DepthInputMap
    {
        public int PaddedWidth;
        public int PaddedHeight;
        public float Scale;
        public float PadX;
        public float PadY;
    }

    /// <summary>
    /// DPT 전처리(1/255·ImageNet 정규화·종횡비 유지·14 배수 패딩)와 양선형 샘플링 순수 함수.
    /// </summary>
    public static class DepthSampler
    {
        public static readonly float[] ImageMean = { 0.485f, 0.456f, 0.406f };
        public static readonly float[] ImageStd = { 0.229f, 0.224f, 0.225f };

        public static DepthInputMap ComputeMap(int srcWidth, int srcHeight, int targetMax, int multiple)
        {
            if (srcWidth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(srcWidth));
            }

            if (srcHeight <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(srcHeight));
            }

            var scale = targetMax / (float)Math.Max(srcWidth, srcHeight);
            var scaledWidth = Math.Max(1, (int)(srcWidth * scale));
            var scaledHeight = Math.Max(1, (int)(srcHeight * scale));
            return new DepthInputMap
            {
                PaddedWidth = (scaledWidth + multiple - 1) / multiple * multiple,
                PaddedHeight = (scaledHeight + multiple - 1) / multiple * multiple,
                Scale = scale,
                PadX = 0f,
                PadY = 0f,
            };
        }

        public static void Preprocess(
            Color32[] src, int srcWidth, int srcHeight, bool flipVertically,
            float[] dst, int dstWidth, int dstHeight, out DepthInputMap map)
        {
            if (src == null)
            {
                throw new ArgumentNullException(nameof(src));
            }

            map = ComputeMap(srcWidth, srcHeight, Math.Max(dstWidth, dstHeight), 14);
            if (dst == null || dst.Length < 3 * dstWidth * dstHeight)
            {
                throw new ArgumentException("dst must hold 3 * dstWidth * dstHeight.", nameof(dst));
            }

            var scaledWidth = Math.Min((int)(srcWidth * map.Scale), dstWidth);
            var scaledHeight = Math.Min((int)(srcHeight * map.Scale), dstHeight);
            for (var c = 0; c < 3; c++)
            {
                var plane = c * dstWidth * dstHeight;
                for (var y = 0; y < dstHeight; y++)
                {
                    for (var x = 0; x < dstWidth; x++)
                    {
                        float value = 0f;
                        if (x < scaledWidth && y < scaledHeight)
                        {
                            var srcX = Math.Min((int)(x / map.Scale), srcWidth - 1);
                            var srcY = Math.Min((int)(y / map.Scale), srcHeight - 1);
                            if (flipVertically)
                            {
                                srcY = srcHeight - 1 - srcY;
                            }

                            var pixel = src[srcY * srcWidth + srcX];
                            var channel = c == 0 ? pixel.r : c == 1 ? pixel.g : pixel.b;
                            value = (channel / 255f - ImageMean[c]) / ImageStd[c];
                        }

                        dst[plane + y * dstWidth + x] = value;
                    }
                }
            }
        }

        public static bool TryMapToDepth(
            float normX, float normY, int srcWidth, int srcHeight,
            in DepthInputMap map, int depthWidth, int depthHeight,
            out float depthX, out float depthY)
        {
            depthX = 0f;
            depthY = 0f;
            if (!(normX >= 0f) || !(normX < 1f) || !(normY >= 0f) || !(normY < 1f))
            {
                return false;
            }

            var paddedX = normX * srcWidth * map.Scale + map.PadX;
            var paddedY = normY * srcHeight * map.Scale + map.PadY;
            depthX = paddedX * depthWidth / map.PaddedWidth;
            depthY = paddedY * depthHeight / map.PaddedHeight;
            return depthX >= 0f && depthX <= depthWidth - 1 && depthY >= 0f && depthY <= depthHeight - 1;
        }

        public static bool TrySample(float[] depth, int width, int height, float x, float y, out float value)
        {
            value = 0f;
            if (depth == null || x < 0f || y < 0f || x > width - 1 || y > height - 1)
            {
                return false;
            }

            var x0 = (int)x;
            var y0 = (int)y;
            var x1 = Math.Min(x0 + 1, width - 1);
            var y1 = Math.Min(y0 + 1, height - 1);
            var fx = x - x0;
            var fy = y - y0;
            var v00 = depth[y0 * width + x0];
            var v10 = depth[y0 * width + x1];
            var v01 = depth[y1 * width + x0];
            var v11 = depth[y1 * width + x1];
            if (!IsFinite(v00) || !IsFinite(v10) || !IsFinite(v01) || !IsFinite(v11))
            {
                return false;
            }

            value = v00 * (1f - fx) * (1f - fy) + v10 * fx * (1f - fy)
                + v01 * (1f - fx) * fy + v11 * fx * fy;
            return true;
        }

        public static bool TryMedian(float[] values, int count, out float median)
        {
            median = 0f;
            if (values == null || count <= 0)
            {
                return false;
            }

            var finite = 0;
            for (var i = 0; i < count && i < values.Length; i++)
            {
                if (IsFinite(values[i]))
                {
                    values[finite++] = values[i];
                }
            }

            if (finite == 0)
            {
                return false;
            }

            Array.Sort(values, 0, finite);
            median = finite % 2 == 1
                ? values[finite / 2]
                : (values[finite / 2 - 1] + values[finite / 2]) * 0.5f;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
