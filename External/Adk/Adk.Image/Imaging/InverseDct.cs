namespace Adk.Image
{
    /// <summary>
    /// 8x8 inverse DCT shared by the JPEG and MPEG-1 decoders. This is the accurate
    /// integer algorithm of libjpeg's <c>jidctint.c</c> (Loeffler, Ligtenberg and Moschytz),
    /// which meets the IEEE 1180 precision MPEG-1 requires.
    /// </summary>
    public static class InverseDct
    {
        /// <summary>Natural (row-major) index of the n-th coefficient in zigzag order; JPEG and MPEG-1 share it.</summary>
        public static readonly byte[] ZigZag =
        {
            0, 1, 8, 16, 9, 2, 3, 10,
            17, 24, 32, 25, 18, 11, 4, 5,
            12, 19, 26, 33, 40, 48, 41, 34,
            27, 20, 13, 6, 7, 14, 21, 28,
            35, 42, 49, 56, 57, 50, 43, 36,
            29, 22, 15, 23, 30, 37, 44, 51,
            58, 59, 52, 45, 38, 31, 39, 46,
            53, 60, 61, 54, 47, 55, 62, 63
        };

        const int ConstBits = 13;
        const int Pass1Bits = 2;

        const int Fix0298631336 = 2446;
        const int Fix0390180644 = 3196;
        const int Fix0541196100 = 4433;
        const int Fix0765366865 = 6270;
        const int Fix0899976223 = 7373;
        const int Fix1175875602 = 9633;
        const int Fix1501321110 = 12299;
        const int Fix1847759065 = 15137;
        const int Fix1961570560 = 16069;
        const int Fix2053119869 = 16819;
        const int Fix2562915447 = 20995;
        const int Fix3072711026 = 25172;

        /// <summary>
        /// Transforms 64 dequantized coefficients in natural order, starting at
        /// <paramref name="offset"/>, into spatial values in place. The result is neither
        /// level-shifted nor clamped: JPEG adds 128, MPEG-1 adds it to a prediction.
        /// </summary>
        public static void Transform(int[] block, int offset)
        {
            // Pass 1: columns, keeping Pass1Bits of extra precision.
            for (int column = 0; column < 8; column++)
            {
                int i = offset + column;
                if (block[i + 8] == 0 && block[i + 16] == 0 && block[i + 24] == 0 && block[i + 32] == 0 &&
                    block[i + 40] == 0 && block[i + 48] == 0 && block[i + 56] == 0)
                {
                    int dc = block[i] << Pass1Bits;
                    for (int row = 0; row < 64; row += 8)
                        block[i + row] = dc;

                    continue;
                }

                Butterfly(block, i, 8, ConstBits - Pass1Bits);
            }

            // Pass 2: rows, removing the scaling of both passes and the 8x8 normalization.
            for (int row = 0; row < 64; row += 8)
                Butterfly(block, offset + row, 1, ConstBits + Pass1Bits + 3);
        }

        static void Butterfly(int[] block, int i, int step, int shift)
        {
            int rounding = 1 << (shift - 1);

            // Even part.
            int z2 = block[i + 2 * step];
            int z3 = block[i + 6 * step];
            int z1 = (z2 + z3) * Fix0541196100;
            int tmp2 = z1 - z3 * Fix1847759065;
            int tmp3 = z1 + z2 * Fix0765366865;

            int tmp0 = (block[i] + block[i + 4 * step]) << ConstBits;
            int tmp1 = (block[i] - block[i + 4 * step]) << ConstBits;

            int tmp10 = tmp0 + tmp3;
            int tmp13 = tmp0 - tmp3;
            int tmp11 = tmp1 + tmp2;
            int tmp12 = tmp1 - tmp2;

            // Odd part.
            tmp0 = block[i + 7 * step];
            tmp1 = block[i + 5 * step];
            tmp2 = block[i + 3 * step];
            tmp3 = block[i + step];

            z1 = tmp0 + tmp3;
            z2 = tmp1 + tmp2;
            z3 = tmp0 + tmp2;
            int z4 = tmp1 + tmp3;
            int z5 = (z3 + z4) * Fix1175875602;

            tmp0 *= Fix0298631336;
            tmp1 *= Fix2053119869;
            tmp2 *= Fix3072711026;
            tmp3 *= Fix1501321110;
            z1 *= -Fix0899976223;
            z2 *= -Fix2562915447;
            z3 = z3 * -Fix1961570560 + z5;
            z4 = z4 * -Fix0390180644 + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;

            block[i] = (tmp10 + tmp3 + rounding) >> shift;
            block[i + 7 * step] = (tmp10 - tmp3 + rounding) >> shift;
            block[i + step] = (tmp11 + tmp2 + rounding) >> shift;
            block[i + 6 * step] = (tmp11 - tmp2 + rounding) >> shift;
            block[i + 2 * step] = (tmp12 + tmp1 + rounding) >> shift;
            block[i + 5 * step] = (tmp12 - tmp1 + rounding) >> shift;
            block[i + 3 * step] = (tmp13 + tmp0 + rounding) >> shift;
            block[i + 4 * step] = (tmp13 - tmp0 + rounding) >> shift;
        }
    }
}
