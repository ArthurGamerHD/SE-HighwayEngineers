// ReSharper disable RedundantUsingDirective
using System;
using System.IO;
using Adk.Image.Dds;
// ReSharper disable NotAccessedOutParameterVariable

namespace Adk.Image
{
    /// <summary>
    /// Average visible RGB of a single DDS mip. Header parsing and pixel
    /// decoding are shared with <see cref="DdsDecoder"/>, so every format it
    /// supports (BC1, BC3, BC4, all BC7 modes, 24/32-bit RGB) works here.
    /// </summary>
    public static class DdsAverageColor
    {
        const int DDS_HEADER_BYTES = 128;
        const int DDS_DX10_HEADER_BYTES = 20;
        const uint DDPF_FOURCC = 0x00000004u;
        const uint FOURCC_DX10 = 0x30315844u;

        public static bool TryAverageFirstMip(byte[] data, out byte r, out byte g, out byte b)
        {
            return TryAverageArrayMip(data, false, out r, out g, out b);
        }

        /// <summary>
        /// Reads the smallest stored DDS mip and returns its average visible RGB.
        /// Color-metal voxel textures normally provide a complete chain down to
        /// 1x1, making this a cheap representative far-distance color.
        /// </summary>
        public static bool TryAverageSmallestMip(byte[] data, out byte r, out byte g, out byte b)
        {
            return TryAverageArrayMip(data, true, out r, out g, out b);
        }

        public static bool TryAverageFirstMip(Stream input, out byte r, out byte g, out byte b)
        {
            string failureReason;
            return TryAverageFirstMip(input, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageFirstMip(
            Stream input,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            return TryAverageStreamMip(input, false, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageSmallestMip(Stream input, out byte r, out byte g, out byte b)
        {
            string failureReason;
            return TryAverageSmallestMip(input, out r, out g, out b, out failureReason);
        }

        public static bool TryAverageSmallestMip(
            Stream input,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            return TryAverageStreamMip(input, true, out r, out g, out b, out failureReason);
        }

        static bool TryAverageArrayMip(
            byte[] data,
            bool useSmallestMip,
            out byte r,
            out byte g,
            out byte b)
        {
            r = 0;
            g = 0;
            b = 0;

            DdsDecoder.DdsInfo info;
            string failureReason;
            if (!DdsDecoder.TryReadInfo(data, out info, out failureReason))
                return false;

            int offset;
            int width;
            int height;
            int targetMip;
            if (!TryLocateMip(info, useSmallestMip, out offset, out width, out height,
                    out targetMip, out failureReason))
            {
                return false;
            }

            return TryAverageMip(data, offset, info, width, height, out r, out g, out b, out failureReason);
        }

        static bool TryAverageStreamMip(
            Stream input,
            bool useSmallestMip,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            r = 0;
            g = 0;
            b = 0;
            failureReason = null;

            if (input == null)
            {
                failureReason = "Texture stream is null.";
                return false;
            }

            if (!input.CanRead)
            {
                failureReason = "Texture stream is not readable.";
                return false;
            }

            bool restorePosition = false;
            long start = 0;

            try
            {
                if (input.CanSeek)
                {
                    start = input.Position;
                    restorePosition = true;
                }

                // Read only the fixed DDS header. DX10 textures append another
                // 20-byte header, which can be detected from the base header.
                byte[] header = new byte[DDS_HEADER_BYTES + DDS_DX10_HEADER_BYTES];
                if (!DdsDecoder.TryReadExactly(input, header, 0, DDS_HEADER_BYTES))
                {
                    failureReason = "DDS header is truncated before 128 bytes.";
                    return false;
                }

                int consumedBytes = DDS_HEADER_BYTES;
                uint pixelFormatFlags = ReadUInt32(header, 80);
                uint fourCc = ReadUInt32(header, 84);
                if ((pixelFormatFlags & DDPF_FOURCC) != 0 && fourCc == FOURCC_DX10)
                {
                    if (!DdsDecoder.TryReadExactly(
                            input,
                            header,
                            DDS_HEADER_BYTES,
                            DDS_DX10_HEADER_BYTES))
                    {
                        failureReason = "DDS DX10 header is truncated before 20 bytes.";
                        return false;
                    }

                    consumedBytes += DDS_DX10_HEADER_BYTES;
                }

                DdsDecoder.DdsInfo info;
                if (!DdsDecoder.TryReadInfo(header, out info, out failureReason))
                    return false;

                int offset;
                int width;
                int height;
                int targetMip;
                if (!TryLocateMip(info, useSmallestMip, out offset, out width, out height,
                        out targetMip, out failureReason))
                {
                    return false;
                }

                int targetBytes;
                if (!DdsDecoder.TryGetMipByteCount(info, width, height, out targetBytes))
                {
                    failureReason = "Could not calculate target DDS mip byte count (" +
                                    width + "x" + height + ", " + info.Encoding + ").";
                    return false;
                }

                if (offset < consumedBytes)
                {
                    failureReason = "DDS target mip offset precedes the consumed header: offset " +
                                    offset + ", header " + consumedBytes + ".";
                    return false;
                }

                // Workshop archive streams may be forward-only. Discard the
                // earlier mip bytes instead of copying the entire DDS or seeking.
                int bytesToSkip = offset - consumedBytes;
                if (!TrySkipForward(input, bytesToSkip))
                {
                    failureReason = "Texture stream ended while skipping " + bytesToSkip +
                                    " bytes to DDS mip " + targetMip + ".";
                    return false;
                }

                byte[] mipData = new byte[targetBytes];
                if (!DdsDecoder.TryReadExactly(input, mipData, 0, mipData.Length))
                {
                    failureReason = "Texture stream ended while reading DDS mip " + targetMip +
                                    ": expected " + targetBytes + " bytes.";
                    return false;
                }

                if (!TryAverageMip(mipData, 0, info, width, height, out r, out g, out b, out failureReason))
                {
                    failureReason = "DDS mip " + targetMip + ": " + failureReason;
                    return false;
                }

                return true;
            }
            catch (NotSupportedException error)
            {
                failureReason = "Stream operation is not supported while reading DDS: " + error.Message;
                return false;
            }
            catch (Exception error)
            {
                failureReason = error.GetType().Name.Contains("IOException") ? "I/O error while reading DDS: " : (error.GetType().Name + " while reading DDS: ") + error.Message;
                return false;
            }
            finally
            {
                if (restorePosition)
                {
                    try
                    {
                        input.Position = start;
                    }
                    catch
                    {
                        // A stream that advertised seeking but stopped supporting
                        // it should not turn a failed color probe into a game error.
                    }
                }
            }
        }

        /// <summary>
        /// Finds the absolute offset and size of the first or smallest stored mip.
        /// </summary>
        static bool TryLocateMip(
            DdsDecoder.DdsInfo info,
            bool useSmallestMip,
            out int offset,
            out int width,
            out int height,
            out int targetMip,
            out string failureReason)
        {
            failureReason = null;
            offset = info.DataOffset;
            width = info.Width;
            height = info.Height;
            targetMip = useSmallestMip ? info.MipCount - 1 : 0;

            for (int mip = 0; mip < targetMip; mip++)
            {
                int mipBytes;
                if (!DdsDecoder.TryGetMipByteCount(info, width, height, out mipBytes))
                {
                    failureReason = "Could not calculate byte count for DDS mip " + mip +
                                    " (" + width + "x" + height + ", " + info.Encoding + ").";
                    return false;
                }

                if (offset > int.MaxValue - mipBytes)
                {
                    failureReason = "DDS mip offset overflow before target mip " + targetMip + ".";
                    return false;
                }

                offset += mipBytes;
                width = width > 1 ? width / 2 : 1;
                height = height > 1 ? height / 2 : 1;
            }

            return true;
        }

        static bool TryAverageMip(
            byte[] data,
            int offset,
            DdsDecoder.DdsInfo info,
            int width,
            int height,
            out byte r,
            out byte g,
            out byte b,
            out string failureReason)
        {
            r = 0;
            g = 0;
            b = 0;

            int stride = width * 4;
            byte[] pixels;
            try
            {
                pixels = new byte[(long)stride * height];
            }
            catch (Exception error)
            {
                failureReason = "Could not allocate DDS mip pixels: " + error.Message;
                return false;
            }

            if (!DdsDecoder.TryDecodeMip(data, offset, info, width, height, pixels, stride, out failureReason))
                return false;

            // Alpha is coverage only for BC1 punch-through and RGB(A) textures.
            // Color-metal BC3/BC7 textures store metalness in alpha, so every
            // texel counts towards their color.
            bool skipTransparent =
                info.Encoding == DdsDecoder.DdsPixelEncoding.Bc1 ||
                info.Encoding == DdsDecoder.DdsPixelEncoding.Rgb;

            long rSum = 0;
            long gSum = 0;
            long bSum = 0;
            long count = 0;
            for (int i = 0; i < pixels.Length; i += 4)
            {
                if (skipTransparent && pixels[i + 3] == 0)
                    continue;

                rSum += pixels[i];
                gSum += pixels[i + 1];
                bSum += pixels[i + 2];
                count++;
            }

            if (count <= 0)
            {
                failureReason = "Mip contains no visible pixels.";
                return false;
            }

            r = (byte)((rSum + count / 2) / count);
            g = (byte)((gSum + count / 2) / count);
            b = (byte)((bSum + count / 2) / count);
            return true;
        }

        static bool TrySkipForward(Stream input, int count)
        {
            if (count < 0)
                return false;
            if (count == 0)
                return true;

            byte[] scratch = new byte[4096];
            while (count > 0)
            {
                int request = Math.Min(count, scratch.Length);
                int read = input.Read(scratch, 0, request);
                if (read <= 0)
                    return false;

                count -= read;
            }

            return true;
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            return (uint)(data[offset] |
                          (data[offset + 1] << 8) |
                          (data[offset + 2] << 16) |
                          (data[offset + 3] << 24));
        }
    }
}
