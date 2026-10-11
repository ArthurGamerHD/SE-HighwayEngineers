// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.Text;
using Adk.Compression.Zip;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using ZipCrc32 = Adk.Compression.Zip.Crc32;
using ZipZlib = Adk.Compression.Zip.Zlib;

namespace Adk.Image.Png
{
    /// <summary>
    /// Lossless RGBA8 to PNG encoder that searches for the smallest output:
    /// it reduces the color type (palette, grayscale, RGB) when that is exact,
    /// ranks scanline filter strategies with a fast trial compression, and
    /// compresses the winning strategy at the requested level.
    /// </summary>
    public static class PngEncoder
    {
        const int MaxPaletteSize = 256;

        enum FilterStrategy
        {
            None,
            Sub,
            Up,
            Average,
            Paeth,
            MinimumSum,
            MinimumEntropy
        }

        static readonly FilterStrategy[] Strategies =
        {
            FilterStrategy.None,
            FilterStrategy.Sub,
            FilterStrategy.Up,
            FilterStrategy.Average,
            FilterStrategy.Paeth,
            FilterStrategy.MinimumSum,
            FilterStrategy.MinimumEntropy
        };

        public static byte[] Encode(RawRgbaBitmap bitmap, DeflateLevel level)
        {
            if (bitmap == null)
                throw new ArgumentNullException(nameof(bitmap));

            return Encode(bitmap.Pixels, bitmap.Width, bitmap.Height, level);
        }

        public static byte[] Encode(byte[] rgba, int width, int height, DeflateLevel level)
        {
            if (rgba == null)
                throw new ArgumentNullException(nameof(rgba));
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));
            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));
            if (rgba.Length != checked(width * height * 4))
                throw new ArgumentException("Unexpected RGBA buffer length.", nameof(rgba));

            byte[] best = null;
            List<PngLayout> layouts = CreateLayouts(rgba, width, height);
            for (int i = 0; i < layouts.Count; i++)
            {
                byte[] encoded = EncodeLayout(layouts[i], level);
                if (best == null || encoded.Length < best.Length)
                    best = encoded;
            }

            return best;
        }

        static byte[] EncodeLayout(PngLayout layout, DeflateLevel level)
        {
            FilterStrategy bestStrategy = FilterStrategy.None;
            int bestSize = int.MaxValue;

            for (int i = 0; i < Strategies.Length; i++)
            {
                int size = ZipZlib.DeflateRaw(Filter(layout, Strategies[i]), DeflateLevel.Fast).Length;
                if (size < bestSize)
                {
                    bestSize = size;
                    bestStrategy = Strategies[i];
                }
            }

            byte[] compressed = ZipZlib.DeflateZlib(Filter(layout, bestStrategy), level);
            return BuildPng(layout, compressed);
        }

        static List<PngLayout> CreateLayouts(byte[] rgba, int width, int height)
        {
            int pixelCount = width * height;
            bool opaque = true;
            bool gray = true;
            var colors = new Dictionary<uint, int>();

            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * 4;
                byte r = rgba[offset];
                byte g = rgba[offset + 1];
                byte b = rgba[offset + 2];
                byte a = rgba[offset + 3];

                if (a != 255)
                    opaque = false;
                if (r != g || g != b)
                    gray = false;

                if (colors != null && !colors.ContainsKey(Pack(r, g, b, a)))
                {
                    if (colors.Count == MaxPaletteSize)
                        colors = null;
                    else
                        colors.Add(Pack(r, g, b, a), 0);
                }
            }

            var layouts = new List<PngLayout>(2);
            if (colors != null)
                layouts.Add(CreatePaletteLayout(rgba, width, height, colors));

            if (gray)
                layouts.Add(CreateDirectLayout(rgba, width, height, opaque ? 0 : 4));
            else
                layouts.Add(CreateDirectLayout(rgba, width, height, opaque ? 2 : 6));

            return layouts;
        }

        static PngLayout CreateDirectLayout(byte[] rgba, int width, int height, int colorType)
        {
            int channels = colorType == 0 ? 1 : colorType == 4 ? 2 : colorType == 2 ? 3 : 4;
            int rowBytes = width * channels;
            var rows = new byte[rowBytes * height];
            int output = 0;

            for (int pixel = 0; pixel < width * height; pixel++)
            {
                int offset = pixel * 4;
                switch (colorType)
                {
                    case 0:
                        rows[output++] = rgba[offset];
                        break;
                    case 4:
                        rows[output++] = rgba[offset];
                        rows[output++] = rgba[offset + 3];
                        break;
                    case 2:
                        rows[output++] = rgba[offset];
                        rows[output++] = rgba[offset + 1];
                        rows[output++] = rgba[offset + 2];
                        break;
                    default:
                        rows[output++] = rgba[offset];
                        rows[output++] = rgba[offset + 1];
                        rows[output++] = rgba[offset + 2];
                        rows[output++] = rgba[offset + 3];
                        break;
                }
            }

            return new PngLayout
            {
                Width = width,
                Height = height,
                BitDepth = 8,
                ColorType = colorType,
                BytesPerPixel = channels,
                RowBytes = rowBytes,
                Rows = rows
            };
        }

        static PngLayout CreatePaletteLayout(byte[] rgba, int width, int height, Dictionary<uint, int> colors)
        {
            var ordered = new List<uint>(colors.Keys);
            ordered.Sort(ComparePaletteEntries);

            for (int i = 0; i < ordered.Count; i++)
                colors[ordered[i]] = i;

            int bitDepth = ordered.Count <= 2 ? 1 : ordered.Count <= 4 ? 2 : ordered.Count <= 16 ? 4 : 8;
            int rowBytes = (width * bitDepth + 7) / 8;
            var rows = new byte[rowBytes * height];
            int pixelsPerByte = 8 / bitDepth;

            for (int y = 0; y < height; y++)
            {
                int rowStart = y * rowBytes;
                for (int x = 0; x < width; x++)
                {
                    int offset = (y * width + x) * 4;
                    int index = colors[Pack(rgba[offset], rgba[offset + 1], rgba[offset + 2], rgba[offset + 3])];
                    int shift = 8 - bitDepth * (x % pixelsPerByte + 1);
                    rows[rowStart + x / pixelsPerByte] |= (byte)(index << shift);
                }
            }

            var palette = new byte[ordered.Count * 3];
            int transparentCount = 0;
            for (int i = 0; i < ordered.Count; i++)
            {
                uint color = ordered[i];
                palette[i * 3] = (byte)(color >> 24);
                palette[i * 3 + 1] = (byte)(color >> 16);
                palette[i * 3 + 2] = (byte)(color >> 8);
                if ((byte)color != 255)
                    transparentCount = i + 1;
            }

            byte[] transparency = null;
            if (transparentCount > 0)
            {
                transparency = new byte[transparentCount];
                for (int i = 0; i < transparentCount; i++)
                    transparency[i] = (byte)ordered[i];
            }

            return new PngLayout
            {
                Width = width,
                Height = height,
                BitDepth = bitDepth,
                ColorType = 3,
                BytesPerPixel = 1,
                RowBytes = rowBytes,
                Rows = rows,
                Palette = palette,
                Transparency = transparency
            };
        }

        static int ComparePaletteEntries(uint left, uint right)
        {
            bool leftOpaque = (byte)left == 255;
            bool rightOpaque = (byte)right == 255;
            if (leftOpaque != rightOpaque)
                return leftOpaque ? 1 : -1;

            return left.CompareTo(right);
        }

        static uint Pack(byte r, byte g, byte b, byte a)
        {
            return ((uint)r << 24) | ((uint)g << 16) | ((uint)b << 8) | a;
        }

        static byte[] Filter(PngLayout layout, FilterStrategy strategy)
        {
            int rowBytes = layout.RowBytes;
            var output = new byte[(rowBytes + 1) * layout.Height];
            var candidate = new byte[rowBytes];
            var best = new byte[rowBytes];
            var histogram = new int[256];

            for (int y = 0; y < layout.Height; y++)
            {
                int rowStart = y * rowBytes;
                int previousStart = y == 0 ? -1 : rowStart - rowBytes;
                int outputStart = y * (rowBytes + 1);

                int filterType;
                switch (strategy)
                {
                    case FilterStrategy.MinimumSum:
                    case FilterStrategy.MinimumEntropy:
                        filterType = 0;
                        double bestScore = double.MaxValue;
                        for (int type = 0; type <= 4; type++)
                        {
                            FilterRow(layout, rowStart, previousStart, type, candidate);
                            double score = strategy == FilterStrategy.MinimumSum
                                ? AbsoluteSum(candidate)
                                : Entropy(candidate, histogram);
                            if (score < bestScore)
                            {
                                bestScore = score;
                                filterType = type;
                                Buffer.BlockCopy(candidate, 0, best, 0, rowBytes);
                            }
                        }
                        break;

                    default:
                        filterType = (int)strategy;
                        FilterRow(layout, rowStart, previousStart, filterType, best);
                        break;
                }

                output[outputStart] = (byte)filterType;
                Buffer.BlockCopy(best, 0, output, outputStart + 1, rowBytes);
            }

            return output;
        }

        static void FilterRow(PngLayout layout, int rowStart, int previousStart, int filterType, byte[] output)
        {
            byte[] rows = layout.Rows;
            int bpp = layout.BytesPerPixel;

            for (int x = 0; x < layout.RowBytes; x++)
            {
                int raw = rows[rowStart + x];
                int left = x >= bpp ? rows[rowStart + x - bpp] : 0;
                int up = previousStart >= 0 ? rows[previousStart + x] : 0;
                int upLeft = previousStart >= 0 && x >= bpp ? rows[previousStart + x - bpp] : 0;

                int predicted;
                switch (filterType)
                {
                    case 1: predicted = left; break;
                    case 2: predicted = up; break;
                    case 3: predicted = (left + up) >> 1; break;
                    case 4: predicted = Paeth(left, up, upLeft); break;
                    default: predicted = 0; break;
                }

                output[x] = (byte)(raw - predicted);
            }
        }

        static int Paeth(int left, int up, int upLeft)
        {
            int estimate = left + up - upLeft;
            int distanceLeft = Math.Abs(estimate - left);
            int distanceUp = Math.Abs(estimate - up);
            int distanceUpLeft = Math.Abs(estimate - upLeft);

            if (distanceLeft <= distanceUp && distanceLeft <= distanceUpLeft)
                return left;

            return distanceUp <= distanceUpLeft ? up : upLeft;
        }

        static double AbsoluteSum(byte[] row)
        {
            long sum = 0;
            for (int i = 0; i < row.Length; i++)
            {
                int value = (sbyte)row[i];
                sum += value < 0 ? -value : value;
            }

            return sum;
        }

        static double Entropy(byte[] row, int[] histogram)
        {
            Array.Clear(histogram, 0, histogram.Length);
            for (int i = 0; i < row.Length; i++)
                histogram[row[i]]++;

            double bits = 0d;
            double total = row.Length;
            for (int i = 0; i < histogram.Length; i++)
            {
                int count = histogram[i];
                if (count > 0)
                    bits -= count * Math.Log(count / total, 2d);
            }

            return bits;
        }

        static byte[] BuildPng(PngLayout layout, byte[] compressed)
        {
            var header = new byte[13];
            int headerOffset = 0;
            WriteUInt32BigEndian(header, ref headerOffset, (uint)layout.Width);
            WriteUInt32BigEndian(header, ref headerOffset, (uint)layout.Height);
            header[headerOffset++] = (byte)layout.BitDepth;
            header[headerOffset] = (byte)layout.ColorType;

            int length = 8 + ChunkLength(header) + ChunkLength(compressed) + ChunkLength(null);
            if (layout.Palette != null)
                length += ChunkLength(layout.Palette);
            if (layout.Transparency != null)
                length += ChunkLength(layout.Transparency);

            var output = new byte[length];
            int position = 0;
            byte[] signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            Buffer.BlockCopy(signature, 0, output, 0, signature.Length);
            position += signature.Length;

            WriteChunk(output, ref position, "IHDR", header);
            if (layout.Palette != null)
                WriteChunk(output, ref position, "PLTE", layout.Palette);
            if (layout.Transparency != null)
                WriteChunk(output, ref position, "tRNS", layout.Transparency);
            WriteChunk(output, ref position, "IDAT", compressed);
            WriteChunk(output, ref position, "IEND", new byte[0]);
            return output;
        }

        static int ChunkLength(byte[] data)
        {
            return 12 + (data == null ? 0 : data.Length);
        }

        static void WriteChunk(byte[] output, ref int position, string type, byte[] data)
        {
            byte[] typeBytes = Encoding.ASCII.GetBytes(type);
            WriteUInt32BigEndian(output, ref position, (uint)data.Length);
            Buffer.BlockCopy(typeBytes, 0, output, position, typeBytes.Length);
            position += typeBytes.Length;
            Buffer.BlockCopy(data, 0, output, position, data.Length);
            position += data.Length;
            WriteUInt32BigEndian(output, ref position, ZipCrc32.Compute(typeBytes, data));
        }

        static void WriteUInt32BigEndian(byte[] data, ref int offset, uint value)
        {
            data[offset++] = (byte)(value >> 24);
            data[offset++] = (byte)(value >> 16);
            data[offset++] = (byte)(value >> 8);
            data[offset++] = (byte)value;
        }

        sealed class PngLayout
        {
            public int Width;
            public int Height;
            public int BitDepth;
            public int ColorType;
            public int BytesPerPixel;
            public int RowBytes;
            public byte[] Rows;
            public byte[] Palette;
            public byte[] Transparency;
        }
    }
}
