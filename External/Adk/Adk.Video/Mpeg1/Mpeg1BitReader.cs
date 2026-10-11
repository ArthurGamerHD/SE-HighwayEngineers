namespace Adk.Video.Mpeg1
{
    /// <summary>Most-significant-bit-first reader over part of a byte array; reads past the end yield zeros.</summary>
    sealed class Mpeg1BitReader
    {
        byte[] _data;
        int _end;
        int _bit;
        int _endBit;

        public void Reset(byte[] data, int offset, int length)
        {
            _data = data;
            _bit = offset * 8;
            _end = offset + length;
            _endBit = _end * 8;
        }

        public bool HasMore => _bit < _endBit;

        public uint Peek32()
        {
            int index = _bit >> 3;
            int shift = _bit & 7;
            uint value = ((uint)Byte(index) << 24) | ((uint)Byte(index + 1) << 16) |
                         ((uint)Byte(index + 2) << 8) | (uint)Byte(index + 3);
            return shift == 0 ? value : (value << shift) | (uint)(Byte(index + 4) >> (8 - shift));
        }

        public int Read(int count)
        {
            int value = (int)(Peek32() >> (32 - count));
            _bit += count;
            return value;
        }

        public bool ReadBit()
        {
            bool value = (Byte(_bit >> 3) & (0x80 >> (_bit & 7))) != 0;
            _bit++;
            return value;
        }

        public void Skip(int count)
        {
            _bit += count;
        }

        /// <summary>True while the next 23 bits are not all zero, i.e. no start code follows.</summary>
        public bool NextBitsNonZero23()
        {
            return Peek32() >> 9 != 0;
        }

        /// <summary>
        /// Moves past the next <c>00 00 01 xx</c> start code at or after the current byte and
        /// returns <c>xx</c>, or -1 when the data holds no further start code.
        /// </summary>
        public int NextStartCode()
        {
            int index = (_bit + 7) >> 3;
            for (; index + 3 < _end; index++)
            {
                if (_data[index] == 0 && _data[index + 1] == 0 && _data[index + 2] == 1)
                {
                    _bit = (index + 4) * 8;
                    return _data[index + 3];
                }
            }

            _bit = _endBit;
            return -1;
        }

        int Byte(int index)
        {
            return index < _end ? _data[index] : 0;
        }
    }
}
