// ReSharper disable RedundantUsingDirective
using System;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace Adk.Image
{
    /// <summary>
    /// Precomputed conversion of one planar 8-bit YCbCr layout (BT.601 matrix) to opaque
    /// top-to-bottom RGBA8: chroma upsampling taps and colour coefficients. Immutable, so
    /// one layout can convert different row ranges on several threads at once.
    /// Chroma sample <c>i</c> covers luma samples <c>i * factor</c> to <c>(i + 1) * factor - 1</c>
    /// and is upsampled bilinearly with centred siting, where both JPEG (JFIF) and MPEG-1 put it.
    /// </summary>
    public sealed class YCbCrLayout
    {
        internal const int FractionBits = 16;
        internal const int Half = 1 << (FractionBits - 1);

        internal readonly int[] ColumnIndex;
        internal readonly int[] ColumnWeight;
        internal readonly int[] RowIndex;
        internal readonly int[] RowWeight;
        internal readonly int LumaScale;
        internal readonly int LumaOffset;
        internal readonly int CrToR;
        internal readonly int CbToG;
        internal readonly int CrToG;
        internal readonly int CbToB;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int ChromaWidth { get; private set; }
        public int ChromaHeight { get; private set; }

        /// <param name="horizontalFactor">Luma samples per chroma sample across, e.g. 2 for 4:2:0.</param>
        /// <param name="verticalFactor">Luma rows per chroma row, e.g. 2 for 4:2:0.</param>
        /// <param name="studioRange">
        /// True for MPEG video levels (Y 16-235, chroma 16-240); false for JPEG's full 0-255 range.
        /// </param>
        public YCbCrLayout(
            int chromaWidth,
            int chromaHeight,
            int horizontalFactor,
            int verticalFactor,
            int width,
            int height,
            bool studioRange)
        {
            if (width <= 0 || height <= 0 || chromaWidth <= 0 || chromaHeight <= 0 ||
                horizontalFactor <= 0 || verticalFactor <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            Width = width;
            Height = height;
            ChromaWidth = chromaWidth;
            ChromaHeight = chromaHeight;

            LumaScale = studioRange ? Fixed(255.0 / 219.0) : 1 << FractionBits;
            LumaOffset = studioRange ? 16 : 0;
            double chromaScale = studioRange ? 255.0 / 224.0 : 1.0;
            CrToR = Fixed(1.402 * chromaScale);
            CbToG = Fixed(0.344136 * chromaScale);
            CrToG = Fixed(0.714136 * chromaScale);
            CbToB = Fixed(1.772 * chromaScale);

            BuildTaps(chromaWidth, horizontalFactor, width, out ColumnIndex, out ColumnWeight);
            BuildTaps(chromaHeight, verticalFactor, height, out RowIndex, out RowWeight);
        }

        /// <summary>
        /// For each output position, the chroma sample to its left/top and the 8-bit weight of
        /// the next one, mapping centres: <c>source = (x + 0.5) / factor - 0.5</c>.
        /// </summary>
        static void BuildTaps(int chromaSize, int factor, int fullSize, out int[] index, out int[] weight)
        {
            index = new int[fullSize];
            weight = new int[fullSize];
            for (int x = 0; x < fullSize; x++)
            {
                // In 1/256 chroma samples.
                long position = ((2L * x + 1) * 256) / (2L * factor) - 128;
                if (position < 0)
                    position = 0;

                int whole = (int)(position >> 8);
                if (whole >= chromaSize - 1)
                {
                    index[x] = chromaSize - 1;
                    weight[x] = 0;
                }
                else
                {
                    index[x] = whole;
                    weight[x] = (int)(position & 255);
                }
            }
        }

        internal static int Fixed(double value)
        {
            return (int)Math.Round(value * (1 << FractionBits));
        }
    }

    /// <summary>
    /// Converts planar 8-bit YCbCr to opaque top-to-bottom RGBA8, shared by the JPEG and
    /// MPEG-1 decoders. See <see cref="YCbCrLayout"/> for the sampling rules.
    /// </summary>
    public static class YCbCrConverter
    {
        const int FractionBits = YCbCrLayout.FractionBits;
        const int Half = YCbCrLayout.Half;

        /// <summary>Converts a whole image; builds a <see cref="YCbCrLayout"/> for the call.</summary>
        public static void ToRgba(
            byte[] luma,
            int lumaStride,
            byte[] cb,
            byte[] cr,
            int chromaStride,
            int chromaWidth,
            int chromaHeight,
            int horizontalFactor,
            int verticalFactor,
            int width,
            int height,
            bool studioRange,
            byte[] rgba)
        {
            var layout = new YCbCrLayout(chromaWidth, chromaHeight, horizontalFactor, verticalFactor, width, height, studioRange);
            ToRgba(layout, luma, lumaStride, cb, cr, chromaStride, rgba, 0, height);
        }

        /// <summary>
        /// Converts rows <paramref name="firstRow"/> to <paramref name="endRow"/> (exclusive) into
        /// <paramref name="rgba"/>, a <c>Width * Height * 4</c> image. Disjoint row ranges may run in parallel.
        /// </summary>
        public static void ToRgba(
            YCbCrLayout layout,
            byte[] luma,
            int lumaStride,
            byte[] cb,
            byte[] cr,
            int chromaStride,
            byte[] rgba,
            int firstRow,
            int endRow)
        {
            if (layout == null || luma == null || cb == null || cr == null || rgba == null)
                throw new ArgumentNullException(layout == null ? nameof(layout) : nameof(rgba));

            int width = layout.Width;
            if (firstRow < 0 || endRow > layout.Height || firstRow > endRow || rgba.Length < width * layout.Height * 4)
                throw new ArgumentOutOfRangeException(nameof(firstRow));

            int[] columnIndex = layout.ColumnIndex;
            int[] columnWeight = layout.ColumnWeight;
            int chromaWidth = layout.ChromaWidth;
            int lumaScale = layout.LumaScale;
            int lumaOffset = layout.LumaOffset;
            int crToR = layout.CrToR;
            int cbToG = layout.CbToG;
            int crToG = layout.CrToG;
            int cbToB = layout.CbToB;

            for (int y = firstRow; y < endRow; y++)
            {
                int top = layout.RowIndex[y] * chromaStride;
                int bottom = top + (layout.RowIndex[y] + 1 < layout.ChromaHeight ? chromaStride : 0);
                int vertical = layout.RowWeight[y];
                int lumaRow = y * lumaStride;
                int output = y * width * 4;

                for (int x = 0; x < width; x++, output += 4)
                {
                    int left = columnIndex[x];
                    int right = left + 1 < chromaWidth ? left + 1 : left;
                    int horizontal = columnWeight[x];

                    int blue = Sample(cb, top + left, top + right, bottom + left, bottom + right, horizontal, vertical) - 128;
                    int red = Sample(cr, top + left, top + right, bottom + left, bottom + right, horizontal, vertical) - 128;
                    int l = (luma[lumaRow + x] - lumaOffset) * lumaScale + Half;

                    rgba[output] = Clamp((l + red * crToR) >> FractionBits);
                    rgba[output + 1] = Clamp((l - blue * cbToG - red * crToG) >> FractionBits);
                    rgba[output + 2] = Clamp((l + blue * cbToB) >> FractionBits);
                    rgba[output + 3] = 255;
                }
            }
        }

        /// <summary>Converts one full-range JPEG YCbCr triple, for images without subsampling planes.</summary>
        public static void ToRgb(int luma, int cb, int cr, out byte r, out byte g, out byte b)
        {
            int l = (luma << FractionBits) + Half;
            cb -= 128;
            cr -= 128;
            r = Clamp((l + cr * YCbCrLayout.Fixed(1.402)) >> FractionBits);
            g = Clamp((l - cb * YCbCrLayout.Fixed(0.344136) - cr * YCbCrLayout.Fixed(0.714136)) >> FractionBits);
            b = Clamp((l + cb * YCbCrLayout.Fixed(1.772)) >> FractionBits);
        }

        static int Sample(byte[] plane, int topLeft, int topRight, int bottomLeft, int bottomRight, int horizontal, int vertical)
        {
            // Weights are 8-bit fractions of the right/bottom neighbour.
            int top = (plane[topLeft] << 8) + (plane[topRight] - plane[topLeft]) * horizontal;
            int bottom = (plane[bottomLeft] << 8) + (plane[bottomRight] - plane[bottomLeft]) * horizontal;
            return ((top << 8) + (bottom - top) * vertical + (1 << 15)) >> 16;
        }

        static byte Clamp(int value)
        {
            return value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
        }
    }
}
