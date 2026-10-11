// ReSharper disable RedundantUsingDirective
using System;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Mpeg1
{
    /// <summary>
    /// Variable-length code table expanded into a direct lookup on the next
    /// <see cref="MaxLength"/> bits. Entries pack the code length above a 16-bit value.
    /// </summary>
    sealed class Mpeg1Vlc
    {
        readonly int[] _lookup;

        public readonly int MaxLength;

        /// <param name="codes">Pairs of a bit string and the value it decodes to.</param>
        public Mpeg1Vlc(string[] codes, int[] values)
        {
            for (int i = 0; i < codes.Length; i++)
                MaxLength = Math.Max(MaxLength, codes[i].Length);

            _lookup = new int[1 << MaxLength];
            for (int i = 0; i < codes.Length; i++)
            {
                string code = codes[i];
                int bits = 0;
                for (int b = 0; b < code.Length; b++)
                    bits = (bits << 1) | (code[b] - '0');

                int shift = MaxLength - code.Length;
                int first = bits << shift;
                int entry = (code.Length << 16) | (values[i] & 0xFFFF);
                for (int j = 0; j < 1 << shift; j++)
                {
                    if (_lookup[first + j] != 0)
                        throw new InvalidOperationException("MPEG-1 VLC table is not prefix-free at code " + code + ".");

                    _lookup[first + j] = entry;
                }
            }
        }

        public int Read(Mpeg1BitReader reader)
        {
            int entry = _lookup[(int)(reader.Peek32() >> (32 - MaxLength))];
            int length = entry >> 16;
            if (length == 0)
                throw new InvalidDataException("Invalid MPEG-1 variable-length code.");

            reader.Skip(length);
            return (short)(entry & 0xFFFF);
        }
    }

    /// <summary>Code tables from ISO/IEC 11172-2 Annex B.</summary>
    static class Mpeg1Tables
    {
        public const int AddressStuffing = 34;
        public const int AddressEscape = 35;

        // Macroblock type flags.
        public const int Intra = 0x01;
        public const int Pattern = 0x02;
        public const int Backward = 0x04;
        public const int Forward = 0x08;
        public const int Quant = 0x10;

        /// <summary>Marks the escape code in <see cref="DctCoefficient"/>.</summary>
        public const int CoefficientEscape = -1;

        public static readonly byte[] DefaultIntraQuant =
        {
            8, 16, 19, 22, 26, 27, 29, 34,
            16, 16, 22, 24, 27, 29, 34, 37,
            19, 22, 26, 27, 29, 34, 34, 38,
            22, 22, 26, 27, 29, 34, 37, 40,
            22, 26, 27, 29, 32, 35, 40, 48,
            26, 27, 29, 32, 35, 40, 48, 58,
            26, 27, 29, 34, 38, 46, 56, 69,
            27, 29, 35, 38, 46, 56, 69, 83
        };

        /// <summary>B.1: 1-33, plus stuffing (34) and escape (35).</summary>
        public static readonly Mpeg1Vlc MacroblockAddressIncrement = new Mpeg1Vlc(
            new[]
            {
                "1", "011", "010", "0011", "0010", "00011", "00010", "0000111", "0000110", "00001011", "00001010",
                "00001001", "00001000", "00000111", "00000110", "0000010111", "0000010110", "0000010101",
                "0000010100", "0000010011", "0000010010", "00000100011", "00000100010", "00000100001",
                "00000100000", "00000011111", "00000011110", "00000011101", "00000011100", "00000011011",
                "00000011010", "00000011001", "00000011000", "00000001111", "00000001000"
            },
            new[]
            {
                1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27,
                28, 29, 30, 31, 32, 33, AddressStuffing, AddressEscape
            });

        /// <summary>B.2a: intra-coded (I) pictures.</summary>
        static readonly Mpeg1Vlc IntraMacroblockType = new Mpeg1Vlc(
            new[] { "1", "01" },
            new[] { Intra, Intra | Quant });

        /// <summary>B.2b: predictive-coded (P) pictures.</summary>
        static readonly Mpeg1Vlc PredictiveMacroblockType = new Mpeg1Vlc(
            new[] { "1", "01", "001", "00011", "00010", "00001", "000001" },
            new[]
            {
                Forward | Pattern, Pattern, Forward, Intra, Forward | Pattern | Quant, Pattern | Quant, Intra | Quant
            });

        /// <summary>B.2c: bidirectionally-predictive-coded (B) pictures.</summary>
        static readonly Mpeg1Vlc BidirectionalMacroblockType = new Mpeg1Vlc(
            new[] { "10", "11", "010", "011", "0010", "0011", "00011", "00010", "000011", "000010", "000001" },
            new[]
            {
                Forward | Backward, Forward | Backward | Pattern, Backward, Backward | Pattern, Forward,
                Forward | Pattern, Intra, Forward | Backward | Pattern | Quant, Forward | Pattern | Quant,
                Backward | Pattern | Quant, Intra | Quant
            });

        public static Mpeg1Vlc MacroblockType(int pictureType)
        {
            return pictureType == 1 ? IntraMacroblockType : pictureType == 2 ? PredictiveMacroblockType : BidirectionalMacroblockType;
        }

        /// <summary>B.3: coded block pattern, values 1-63.</summary>
        public static readonly Mpeg1Vlc CodedBlockPattern = new Mpeg1Vlc(
            new[]
            {
                "111", "1101", "1100", "1011", "1010", "10011", "10010", "10001", "10000", "01111", "01110", "01101",
                "01100", "01011", "01010", "01001", "01000", "001111", "001110", "001101", "001100", "0010111",
                "0010110", "0010101", "0010100", "0010011", "0010010", "0010001", "0010000", "00011111", "00011110",
                "00011101", "00011100", "00011011", "00011010", "00011001", "00011000", "00010111", "00010110",
                "00010101", "00010100", "00010011", "00010010", "00010001", "00010000", "00001111", "00001110",
                "00001101", "00001100", "00001011", "00001010", "00001001", "00001000", "00000111", "00000110",
                "00000101", "00000100", "000000111", "000000110", "000000101", "000000100", "000000011", "000000010"
            },
            new[]
            {
                60, 4, 8, 16, 32, 12, 48, 20, 40, 28, 44, 52, 56, 1, 61, 2, 62, 24, 36, 3, 63, 5, 9, 17, 33, 6, 10,
                18, 34, 7, 11, 19, 35, 13, 49, 21, 41, 14, 50, 22, 42, 15, 51, 23, 43, 25, 37, 26, 38, 29, 45, 53,
                57, 30, 46, 54, 58, 31, 47, 55, 59, 27, 39
            });

        /// <summary>B.4: motion code, -16 to 16. The last bit of each nonzero code is its sign.</summary>
        public static readonly Mpeg1Vlc MotionCode = BuildMotionCode();

        /// <summary>B.5a: dct_dc_size_luminance.</summary>
        public static readonly Mpeg1Vlc DcSizeLuminance = new Mpeg1Vlc(
            new[] { "100", "00", "01", "101", "110", "1110", "11110", "111110", "1111110" },
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 });

        /// <summary>B.5b: dct_dc_size_chrominance.</summary>
        public static readonly Mpeg1Vlc DcSizeChrominance = new Mpeg1Vlc(
            new[] { "00", "01", "10", "110", "1110", "11110", "111110", "1111110", "11111110" },
            new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 });

        /// <summary>
        /// B.5c-f: dct_coeff, sign bit excluded, value <c>(run &lt;&lt; 8) | level</c>. Code "1" is
        /// run 0 level 1 in its first-coefficient form; after the first coefficient a following
        /// 0 bit turns it into end-of-block ("10") and a 1 bit into the "11" form.
        /// </summary>
        public static readonly Mpeg1Vlc DctCoefficient = BuildDctCoefficient();

        static Mpeg1Vlc BuildMotionCode()
        {
            string[] magnitudes =
            {
                "01", "001", "0001", "000011", "0000101", "0000100", "0000011", "000001011", "000001010",
                "000001001", "0000010001", "0000010000", "0000001111", "0000001110", "0000001101", "0000001100"
            };

            var codes = new string[1 + magnitudes.Length * 2];
            var values = new int[codes.Length];
            codes[0] = "1";
            for (int i = 0; i < magnitudes.Length; i++)
            {
                codes[1 + i * 2] = magnitudes[i] + "0";
                values[1 + i * 2] = i + 1;
                codes[2 + i * 2] = magnitudes[i] + "1";
                values[2 + i * 2] = -(i + 1);
            }

            return new Mpeg1Vlc(codes, values);
        }

        static Mpeg1Vlc BuildDctCoefficient()
        {
            // Code, run, level.
            object[] table =
            {
                "1", 0, 1, "011", 1, 1, "0100", 0, 2, "0101", 2, 1, "00101", 0, 3, "00111", 3, 1, "00110", 4, 1,
                "000110", 1, 2, "000111", 5, 1, "000101", 6, 1, "000100", 7, 1, "0000110", 0, 4, "0000100", 2, 2,
                "0000111", 8, 1, "0000101", 9, 1, "00100110", 0, 5, "00100001", 0, 6, "00100101", 1, 3,
                "00100100", 3, 2, "00100111", 10, 1, "00100011", 11, 1, "00100010", 12, 1, "00100000", 13, 1,
                "0000001010", 0, 7, "0000001100", 1, 4, "0000001011", 2, 3, "0000001111", 4, 2, "0000001001", 5, 2,
                "0000001110", 14, 1, "0000001101", 15, 1, "0000001000", 16, 1,
                "000000011101", 0, 8, "000000011000", 0, 9, "000000010011", 0, 10, "000000010000", 0, 11,
                "000000011011", 1, 5, "000000010100", 2, 4, "000000011100", 3, 3, "000000010010", 4, 3,
                "000000011110", 6, 2, "000000010101", 7, 2, "000000010001", 8, 2, "000000011111", 17, 1,
                "000000011010", 18, 1, "000000011001", 19, 1, "000000010111", 20, 1, "000000010110", 21, 1,
                "0000000011010", 0, 12, "0000000011001", 0, 13, "0000000011000", 0, 14, "0000000010111", 0, 15,
                "0000000010110", 1, 6, "0000000010101", 1, 7, "0000000010100", 2, 5, "0000000010011", 3, 4,
                "0000000010010", 5, 3, "0000000010001", 9, 2, "0000000010000", 10, 2, "0000000011111", 22, 1,
                "0000000011110", 23, 1, "0000000011101", 24, 1, "0000000011100", 25, 1, "0000000011011", 26, 1,
                "00000000011111", 0, 16, "00000000011110", 0, 17, "00000000011101", 0, 18, "00000000011100", 0, 19,
                "00000000011011", 0, 20, "00000000011010", 0, 21, "00000000011001", 0, 22, "00000000011000", 0, 23,
                "00000000010111", 0, 24, "00000000010110", 0, 25, "00000000010101", 0, 26, "00000000010100", 0, 27,
                "00000000010011", 0, 28, "00000000010010", 0, 29, "00000000010001", 0, 30, "00000000010000", 0, 31,
                "000000000011000", 0, 32, "000000000010111", 0, 33, "000000000010110", 0, 34,
                "000000000010101", 0, 35, "000000000010100", 0, 36, "000000000010011", 0, 37,
                "000000000010010", 0, 38, "000000000010001", 0, 39, "000000000010000", 0, 40,
                "000000000011111", 1, 8, "000000000011110", 1, 9, "000000000011101", 1, 10,
                "000000000011100", 1, 11, "000000000011011", 1, 12, "000000000011010", 1, 13,
                "000000000011001", 1, 14,
                "0000000000010011", 1, 15, "0000000000010010", 1, 16, "0000000000010001", 1, 17,
                "0000000000010000", 1, 18, "0000000000010100", 6, 3, "0000000000011010", 11, 2,
                "0000000000011001", 12, 2, "0000000000011000", 13, 2, "0000000000010111", 14, 2,
                "0000000000010110", 15, 2, "0000000000010101", 16, 2, "0000000000011111", 27, 1,
                "0000000000011110", 28, 1, "0000000000011101", 29, 1, "0000000000011100", 30, 1,
                "0000000000011011", 31, 1
            };

            int count = table.Length / 3;
            var codes = new string[count + 1];
            var values = new int[count + 1];
            for (int i = 0; i < count; i++)
            {
                codes[i] = (string)table[i * 3];
                values[i] = ((int)table[i * 3 + 1] << 8) | (int)table[i * 3 + 2];
            }

            codes[count] = "000001";
            values[count] = CoefficientEscape;
            return new Mpeg1Vlc(codes, values);
        }
    }
}
