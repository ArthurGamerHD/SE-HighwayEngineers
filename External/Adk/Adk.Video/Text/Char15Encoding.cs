// ReSharper disable RedundantUsingDirective
using System;
using System.Text;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Text
{
    /// <summary>
    /// Packs binary data into text at 15 bits per UTF-16 char (93.75% of each char, against
    /// 37.5% for base64), the scheme Movie Night streams use so a video fits game string limits.
    /// <para>
    /// Layout: an optional header ending in <see cref="Start"/> (e.g. a Movie Night style INI
    /// block), two chars holding the byte count (high 15 bits, then low 15 bits), then the
    /// bytes as a most-significant-bit-first stream cut into 15-bit chars, the last one
    /// zero-padded. Values 0x00-0x1F are not valid in XML saves, so they are written as
    /// <c>value + 0x8000</c>; 15-bit data never reaches 0x8000 on its own, which leaves
    /// U+8000 and up free for markers. Payload chars therefore never surrogate-pair either.
    /// </para>
    /// </summary>
    public static class Char15Encoding
    {
        /// <summary>Separates an optional text header from the payload.</summary>
        public const char Start = (char)0x8020;

        const int BannedOffset = 0x8000;
        const int BannedCount = 32;

        // Legacy Movie Night encoders also offset 0x20-0x23; accept them when reading.
        const int LegacyBannedCount = 36;

        /// <summary>Largest payload the 30-bit length prefix can describe.</summary>
        public const int MaxByteCount = (1 << 30) - 1;

        /// <summary>Number of chars <see cref="Encode(byte[])"/> produces for <paramref name="byteCount"/> bytes.</summary>
        public static int GetCharCount(int byteCount)
        {
            if (byteCount < 0 || byteCount > MaxByteCount)
                throw new ArgumentOutOfRangeException(nameof(byteCount));

            return 2 + (int)(((long)byteCount * 8 + 14) / 15);
        }

        public static string Encode(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var builder = new StringBuilder(GetCharCount(data.Length));
            Encode(data, 0, data.Length, builder);
            return builder.ToString();
        }

        /// <summary>Appends the length prefix and packed bytes, without a header, to <paramref name="output"/>.</summary>
        public static void Encode(byte[] data, int offset, int count, StringBuilder output)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (output == null)
                throw new ArgumentNullException(nameof(output));

            if (offset < 0 || count < 0 || offset > data.Length - count)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count > MaxByteCount)
                throw new ArgumentOutOfRangeException(nameof(count));

            output.Append(ToChar((count >> 15) & 0x7FFF));
            output.Append(ToChar(count & 0x7FFF));

            int bits = 0;
            int bitCount = 0;
            int end = offset + count;
            for (int i = offset; i < end; i++)
            {
                bits = (bits << 8) | data[i];
                bitCount += 8;
                if (bitCount >= 15)
                {
                    bitCount -= 15;
                    output.Append(ToChar((bits >> bitCount) & 0x7FFF));
                    bits &= (1 << bitCount) - 1;
                }
            }

            if (bitCount > 0)
                output.Append(ToChar((bits << (15 - bitCount)) & 0x7FFF));
        }

        /// <summary>
        /// Decodes text written by <see cref="Encode(byte[])"/>. Anything up to and including
        /// the first <see cref="Start"/> is treated as a header and skipped.
        /// </summary>
        public static byte[] Decode(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            int start = text.IndexOf(Start);
            return Decode(text, start < 0 ? 0 : start + 1);
        }

        /// <summary>Decodes a payload (length prefix and packed bytes) starting at <paramref name="index"/>.</summary>
        public static byte[] Decode(string text, int index)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            if (index < 0 || index > text.Length - 2)
                throw new InvalidDataException("15-bit text is too short to hold its length prefix.");

            int length = (FromChar(text[index]) << 15) | FromChar(text[index + 1]);
            int position = index + 2;
            if (text.Length - position < (int)(((long)length * 8 + 14) / 15))
                throw new InvalidDataException("15-bit text ends before its declared " + length + " bytes.");

            byte[] data = new byte[length];
            int bits = 0;
            int bitCount = 0;
            for (int i = 0; i < length; i++)
            {
                if (bitCount < 8)
                {
                    bits = ((bits << 15) | FromChar(text[position++])) & 0x3FFFFF;
                    bitCount += 15;
                }

                bitCount -= 8;
                data[i] = (byte)(bits >> bitCount);
            }

            return data;
        }

        /// <summary>Text up to the first <see cref="Start"/>, or null when there is no header.</summary>
        public static string GetHeader(string text)
        {
            if (text == null)
                throw new ArgumentNullException(nameof(text));

            int start = text.IndexOf(Start);
            if (start < 0)
                return null;

            var header = new StringBuilder(text, 0, start, start);
            for (int i = 0; i < header.Length; i++)
            {
                int value = header[i] - BannedOffset;
                if (value >= 0 && value < LegacyBannedCount)
                    header[i] = (char)value;
            }

            return header.ToString();
        }

        static char ToChar(int value)
        {
            return (char)(value < BannedCount ? value + BannedOffset : value);
        }

        static int FromChar(char c)
        {
            int value = c;
            if (value < BannedOffset)
                return value;

            value -= BannedOffset;
            if (value < LegacyBannedCount)
                return value;

            throw new InvalidDataException("Unexpected marker U+" + ((int)c).ToString("X4") + " inside 15-bit data.");
        }
    }
}
