// ReSharper disable RedundantUsingDirective
using System;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Image.Jpeg
{
    /// <summary>
    /// Decodes JPEG (JFIF/Exif/Adobe) images to top-to-bottom RGBA8.
    /// Supported: 8-bit baseline and extended sequential (SOF0/SOF1) and progressive (SOF2)
    /// Huffman coding, grayscale, YCbCr and Adobe RGB, any sampling factors with matching
    /// chroma planes, and restart intervals. Not supported: arithmetic coding, lossless,
    /// hierarchical, 12-bit precision and CMYK/YCCK. Exif orientation is not applied.
    /// </summary>
    public static class JpegDecoder
    {
        public static RawRgbaBitmap Load(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            return new JpegReader(data).Decode();
        }
    }

    sealed class JpegReader
    {
        sealed class Component
        {
            public int Id;
            public int H;
            public int V;
            public int QuantTable;
            public int DcTable;
            public int AcTable;
            public int BlocksPerLine;
            public int BlocksPerColumn;
            public short[] Coefficients;
            public int DcPrediction;

            /// <summary>Block offset into <see cref="Coefficients"/>.</summary>
            public int BlockOffset(int row, int column)
            {
                return (row * BlocksPerLine + column) * 64;
            }
        }

        readonly byte[] _data;
        int _position;

        readonly ushort[][] _quantTables = new ushort[4][];
        readonly HuffmanTable[] _dcTables = new HuffmanTable[4];
        readonly HuffmanTable[] _acTables = new HuffmanTable[4];

        Component[] _components;
        int _width;
        int _height;
        bool _progressive;
        int _maxH;
        int _maxV;
        int _mcusPerLine;
        int _mcusPerColumn;
        int _restartInterval;
        int _adobeTransform = -1;
        int _eobRun;

        // Entropy-coded segment reader: bits are kept MSB-aligned in _bitBuffer.
        uint _bitBuffer;
        int _bitCount;
        bool _hitMarker;

        public JpegReader(byte[] data)
        {
            _data = data;
        }

        public RawRgbaBitmap Decode()
        {
            if (_data.Length < 4 || _data[0] != 0xFF || _data[1] != 0xD8)
                throw new InvalidDataException("The data is not a JPEG image.");

            _position = 2;
            bool sawFrame = false;
            bool sawScan = false;

            while (true)
            {
                int marker = NextMarker();
                if (marker == 0xD9)
                    break;

                if (marker == 0xD8 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
                    continue;

                int length = ReadUInt16();
                if (length < 2 || _position + length - 2 > _data.Length)
                    throw new InvalidDataException("JPEG segment runs past the end of the data.");

                int segment = _position;
                int end = _position + length - 2;

                switch (marker)
                {
                    case 0xC0:
                    case 0xC1:
                    case 0xC2:
                        if (sawFrame)
                            throw new NotSupportedException("JPEG files with more than one frame are not supported.");

                        ReadFrame(segment, marker == 0xC2);
                        sawFrame = true;
                        break;
                    case 0xC3:
                    case 0xC5:
                    case 0xC6:
                    case 0xC7:
                    case 0xC9:
                    case 0xCA:
                    case 0xCB:
                    case 0xCD:
                    case 0xCE:
                    case 0xCF:
                        throw new NotSupportedException(
                            "Unsupported JPEG coding (marker 0x" + marker.ToString("X2") +
                            "): only baseline and progressive Huffman are supported.");
                    case 0xC4:
                        ReadHuffmanTables(segment, end);
                        break;
                    case 0xDB:
                        ReadQuantTables(segment, end);
                        break;
                    case 0xDD:
                        _restartInterval = (_data[segment] << 8) | _data[segment + 1];
                        break;
                    case 0xEE:
                        if (length >= 14 && _data[segment] == (byte)'A' && _data[segment + 1] == (byte)'d' &&
                            _data[segment + 2] == (byte)'o' && _data[segment + 3] == (byte)'b' &&
                            _data[segment + 4] == (byte)'e')
                        {
                            _adobeTransform = _data[segment + 11];
                        }

                        break;
                    case 0xDA:
                        if (!sawFrame)
                            throw new InvalidDataException("JPEG scan appeared before its frame header.");

                        _position = end;
                        ReadScan(segment);
                        sawScan = true;
                        continue;
                }

                _position = end;
            }

            if (!sawFrame || !sawScan)
                throw new InvalidDataException("JPEG is missing its frame or scan data.");

            return BuildBitmap();
        }

        int NextMarker()
        {
            // Skips anything that is not a marker, including fill bytes.
            while (_position < _data.Length - 1)
            {
                if (_data[_position] == 0xFF)
                {
                    int marker = _data[_position + 1];
                    if (marker != 0x00 && marker != 0xFF)
                    {
                        _position += 2;
                        return marker;
                    }
                }

                _position++;
            }

            // A truncated file ends here, as if it had its EOI.
            return 0xD9;
        }

        int ReadUInt16()
        {
            if (_position > _data.Length - 2)
                throw new InvalidDataException("JPEG ended inside a segment header.");

            int value = (_data[_position] << 8) | _data[_position + 1];
            _position += 2;
            return value;
        }

        void ReadFrame(int offset, bool progressive)
        {
            if (_data[offset] != 8)
                throw new NotSupportedException("Only 8-bit JPEG images are supported.");

            _height = (_data[offset + 1] << 8) | _data[offset + 2];
            _width = (_data[offset + 3] << 8) | _data[offset + 4];
            int count = _data[offset + 5];
            if (_width == 0 || _height == 0)
                throw new NotSupportedException("JPEG images without a declared height are not supported.");

            if (count != 1 && count != 3)
                throw new NotSupportedException("Unsupported JPEG component count: " + count + ".");

            _progressive = progressive;
            _components = new Component[count];
            _maxH = 1;
            _maxV = 1;
            for (int i = 0; i < count; i++)
            {
                int c = offset + 6 + i * 3;
                var component = new Component
                {
                    Id = _data[c],
                    H = _data[c + 1] >> 4,
                    V = _data[c + 1] & 15,
                    QuantTable = _data[c + 2] & 3
                };

                if (component.H < 1 || component.H > 4 || component.V < 1 || component.V > 4)
                    throw new InvalidDataException("Invalid JPEG sampling factors.");

                _maxH = Math.Max(_maxH, component.H);
                _maxV = Math.Max(_maxV, component.V);
                _components[i] = component;
            }

            _mcusPerLine = (_width + 8 * _maxH - 1) / (8 * _maxH);
            _mcusPerColumn = (_height + 8 * _maxV - 1) / (8 * _maxV);
            for (int i = 0; i < count; i++)
            {
                Component component = _components[i];
                component.BlocksPerLine = _mcusPerLine * component.H;
                component.BlocksPerColumn = _mcusPerColumn * component.V;
                component.Coefficients = new short[checked(component.BlocksPerLine * component.BlocksPerColumn * 64)];
            }
        }

        void ReadQuantTables(int offset, int end)
        {
            while (offset < end)
            {
                int precision = _data[offset] >> 4;
                int id = _data[offset] & 3;
                offset++;
                var table = new ushort[64];
                for (int k = 0; k < 64; k++)
                {
                    int value;
                    if (precision == 0)
                    {
                        value = _data[offset++];
                    }
                    else
                    {
                        value = (_data[offset] << 8) | _data[offset + 1];
                        offset += 2;
                    }

                    table[InverseDct.ZigZag[k]] = (ushort)value;
                }

                _quantTables[id] = table;
            }
        }

        void ReadHuffmanTables(int offset, int end)
        {
            while (offset < end)
            {
                int tableClass = _data[offset] >> 4;
                int id = _data[offset] & 3;
                var counts = new byte[17];
                int total = 0;
                for (int i = 1; i <= 16; i++)
                {
                    counts[i] = _data[offset + i];
                    total += counts[i];
                }

                offset += 17;
                if (total > 256 || offset + total > end)
                    throw new InvalidDataException("Invalid JPEG Huffman table.");

                var symbols = new byte[total];
                Buffer.BlockCopy(_data, offset, symbols, 0, total);
                offset += total;

                var table = new HuffmanTable(counts, symbols);
                if (tableClass == 0)
                    _dcTables[id] = table;
                else
                    _acTables[id] = table;
            }
        }

        void ReadScan(int offset)
        {
            int count = _data[offset];
            if (count < 1 || count > 4)
                throw new InvalidDataException("Invalid JPEG scan component count.");

            var scanComponents = new Component[count];
            for (int i = 0; i < count; i++)
            {
                int id = _data[offset + 1 + i * 2];
                int tables = _data[offset + 2 + i * 2];
                Component component = null;
                for (int c = 0; c < _components.Length; c++)
                {
                    if (_components[c].Id == id)
                        component = _components[c];
                }

                if (component == null)
                    throw new InvalidDataException("JPEG scan references unknown component " + id + ".");

                component.DcTable = tables >> 4;
                component.AcTable = tables & 15;
                scanComponents[i] = component;
            }

            int p = offset + 1 + count * 2;
            int spectralStart = _data[p];
            int spectralEnd = _data[p + 1];
            int approximationHigh = _data[p + 2] >> 4;
            int approximationLow = _data[p + 2] & 15;

            if (!_progressive)
            {
                spectralStart = 0;
                spectralEnd = 63;
                approximationHigh = 0;
                approximationLow = 0;
            }
            else if (spectralEnd > 63 || spectralStart > spectralEnd || (spectralStart == 0 && spectralEnd != 0) ||
                     (spectralStart > 0 && count != 1))
            {
                throw new InvalidDataException("Invalid progressive JPEG scan parameters.");
            }

            for (int i = 0; i < count; i++)
            {
                scanComponents[i].DcPrediction = 0;
                if (spectralStart == 0 && approximationHigh == 0 && GetTable(_dcTables, scanComponents[i].DcTable) == null)
                    throw new InvalidDataException("JPEG scan uses a missing DC table.");

                if (spectralEnd > 0 && GetTable(_acTables, scanComponents[i].AcTable) == null)
                    throw new InvalidDataException("JPEG scan uses a missing AC table.");
            }

            _eobRun = 0;
            ResetBits();

            if (count == 1)
            {
                // Non-interleaved: one block per MCU, covering only the component's real blocks.
                Component component = scanComponents[0];
                int blocksWide = (CeilDiv(_width * component.H, _maxH) + 7) / 8;
                int blocksHigh = (CeilDiv(_height * component.V, _maxV) + 7) / 8;
                int restartsLeft = _restartInterval;
                for (int row = 0; row < blocksHigh; row++)
                {
                    for (int column = 0; column < blocksWide; column++)
                    {
                        if (_restartInterval > 0 && restartsLeft == 0)
                        {
                            Restart(scanComponents);
                            restartsLeft = _restartInterval;
                        }

                        DecodeBlock(component, component.BlockOffset(row, column), spectralStart, spectralEnd,
                            approximationHigh, approximationLow);
                        restartsLeft--;
                    }
                }
            }
            else
            {
                int restartsLeft = _restartInterval;
                for (int mcuRow = 0; mcuRow < _mcusPerColumn; mcuRow++)
                {
                    for (int mcuColumn = 0; mcuColumn < _mcusPerLine; mcuColumn++)
                    {
                        if (_restartInterval > 0 && restartsLeft == 0)
                        {
                            Restart(scanComponents);
                            restartsLeft = _restartInterval;
                        }

                        for (int i = 0; i < count; i++)
                        {
                            Component component = scanComponents[i];
                            for (int v = 0; v < component.V; v++)
                            {
                                for (int h = 0; h < component.H; h++)
                                {
                                    DecodeBlock(
                                        component,
                                        component.BlockOffset(mcuRow * component.V + v, mcuColumn * component.H + h),
                                        spectralStart,
                                        spectralEnd,
                                        approximationHigh,
                                        approximationLow);
                                }
                            }
                        }

                        restartsLeft--;
                    }
                }
            }

            // Leave _position at the marker that ends the entropy-coded data.
            ResetBits();
        }

        void DecodeBlock(Component component, int offset, int start, int end, int high, int low)
        {
            short[] coefficients = component.Coefficients;
            if (start == 0)
            {
                if (high == 0)
                {
                    int category = GetTable(_dcTables, component.DcTable).Decode(this);
                    int difference = category == 0 ? 0 : ReceiveExtend(category);
                    component.DcPrediction += difference;
                    coefficients[offset] = (short)(component.DcPrediction << low);
                }
                else if (ReadBits(1) != 0)
                {
                    coefficients[offset] |= (short)(1 << low);
                }

                if (_progressive)
                    return;

                start = 1;
            }

            HuffmanTable ac = GetTable(_acTables, component.AcTable);
            if (high == 0)
                DecodeAcFirst(ac, coefficients, offset, start, end, low);
            else
                DecodeAcRefine(ac, coefficients, offset, start, end, low);
        }

        void DecodeAcFirst(HuffmanTable ac, short[] coefficients, int offset, int start, int end, int low)
        {
            if (_eobRun > 0)
            {
                _eobRun--;
                return;
            }

            for (int k = start; k <= end; k++)
            {
                int symbol = ac.Decode(this);
                int run = symbol >> 4;
                int size = symbol & 15;
                if (size == 0)
                {
                    if (run < 15)
                    {
                        _eobRun = (1 << run) - 1;
                        if (run > 0)
                            _eobRun += ReadBits(run);

                        return;
                    }

                    k += 15;
                    continue;
                }

                k += run;
                if (k > 63)
                    throw new InvalidDataException("JPEG block has more than 64 coefficients.");

                coefficients[offset + InverseDct.ZigZag[k]] = (short)(ReceiveExtend(size) * (1 << low));
            }
        }

        void DecodeAcRefine(HuffmanTable ac, short[] coefficients, int offset, int start, int end, int low)
        {
            int plus = 1 << low;
            int minus = -1 << low;
            int k = start;

            if (_eobRun == 0)
            {
                for (; k <= end; k++)
                {
                    int symbol = ac.Decode(this);
                    int run = symbol >> 4;
                    int value = symbol & 15;
                    if (value != 0)
                    {
                        value = ReadBits(1) != 0 ? plus : minus;
                    }
                    else if (run != 15)
                    {
                        _eobRun = 1 << run;
                        if (run > 0)
                            _eobRun += ReadBits(run);

                        break;
                    }

                    // Refine nonzero coefficients on the way to the run's target zero.
                    while (k <= end)
                    {
                        int index = offset + InverseDct.ZigZag[k];
                        if (coefficients[index] != 0)
                        {
                            RefineCoefficient(coefficients, index, plus, minus);
                        }
                        else
                        {
                            if (--run < 0)
                                break;
                        }

                        k++;
                    }

                    if (value != 0 && k <= end)
                        coefficients[offset + InverseDct.ZigZag[k]] = (short)value;
                }
            }

            if (_eobRun > 0)
            {
                for (; k <= end; k++)
                {
                    int index = offset + InverseDct.ZigZag[k];
                    if (coefficients[index] != 0)
                        RefineCoefficient(coefficients, index, plus, minus);
                }

                _eobRun--;
            }
        }

        void RefineCoefficient(short[] coefficients, int index, int plus, int minus)
        {
            if (ReadBits(1) != 0 && (coefficients[index] & plus) == 0)
                coefficients[index] = (short)(coefficients[index] + (coefficients[index] >= 0 ? plus : minus));
        }

        void Restart(Component[] scanComponents)
        {
            ResetBits();

            // Padding bits the reader never fetched may sit ahead of the RSTn marker; a
            // different marker means the interval is damaged, so leave it for the caller.
            while (_position < _data.Length - 1)
            {
                if (_data[_position] == 0xFF)
                {
                    int marker = _data[_position + 1];
                    if (marker >= 0xD0 && marker <= 0xD7)
                    {
                        _position += 2;
                        break;
                    }

                    if (marker != 0x00 && marker != 0xFF)
                        break;
                }

                _position++;
            }

            for (int i = 0; i < scanComponents.Length; i++)
                scanComponents[i].DcPrediction = 0;

            _eobRun = 0;
        }

        /// <summary>Drops buffered bits; the bytes they came from are already consumed.</summary>
        void ResetBits()
        {
            _bitBuffer = 0;
            _bitCount = 0;
            _hitMarker = false;
        }

        void Fill()
        {
            while (_bitCount <= 24)
            {
                int value = 0;
                if (!_hitMarker && _position < _data.Length)
                {
                    value = _data[_position];
                    if (value == 0xFF)
                    {
                        int next = _position + 1 < _data.Length ? _data[_position + 1] : 0xD9;
                        if (next == 0x00)
                        {
                            _position += 2;
                        }
                        else if (next == 0xFF)
                        {
                            // Fill byte ahead of a marker.
                            _position++;
                            continue;
                        }
                        else
                        {
                            // A marker ends the data; decoders feed zeros past it.
                            _hitMarker = true;
                            value = 0;
                        }
                    }
                    else
                    {
                        _position++;
                    }
                }

                _bitBuffer |= (uint)value << (24 - _bitCount);
                _bitCount += 8;
            }
        }

        internal uint PeekBits()
        {
            Fill();
            return _bitBuffer;
        }

        internal void SkipBits(int count)
        {
            _bitBuffer <<= count;
            _bitCount -= count;
        }

        int ReadBits(int count)
        {
            Fill();
            int value = (int)(_bitBuffer >> (32 - count));
            SkipBits(count);
            return value;
        }

        int ReceiveExtend(int size)
        {
            int value = ReadBits(size);
            return value < 1 << (size - 1) ? value - (1 << size) + 1 : value;
        }

        static HuffmanTable GetTable(HuffmanTable[] tables, int id)
        {
            return id < tables.Length ? tables[id] : null;
        }

        static int CeilDiv(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        RawRgbaBitmap BuildBitmap()
        {
            var planes = new byte[_components.Length][];
            var block = new int[64];
            for (int c = 0; c < _components.Length; c++)
            {
                Component component = _components[c];
                ushort[] quant = _quantTables[component.QuantTable];
                if (quant == null)
                    throw new InvalidDataException("JPEG component uses a missing quantization table.");

                int stride = component.BlocksPerLine * 8;
                byte[] plane = new byte[checked(stride * component.BlocksPerColumn * 8)];
                for (int row = 0; row < component.BlocksPerColumn; row++)
                {
                    for (int column = 0; column < component.BlocksPerLine; column++)
                    {
                        int offset = component.BlockOffset(row, column);
                        for (int i = 0; i < 64; i++)
                            block[i] = component.Coefficients[offset + i] * quant[i];

                        InverseDct.Transform(block, 0);

                        int output = row * 8 * stride + column * 8;
                        for (int y = 0; y < 8; y++, output += stride)
                        {
                            for (int x = 0; x < 8; x++)
                            {
                                int value = block[y * 8 + x] + 128;
                                plane[output + x] = value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
                            }
                        }
                    }
                }

                planes[c] = plane;
                component.Coefficients = null;
            }

            var bitmap = new RawRgbaBitmap(_width, _height);
            byte[] pixels = bitmap.Pixels;

            if (_components.Length == 1)
            {
                // Grayscale is never subsampled relative to itself.
                Component gray = _components[0];
                int stride = gray.BlocksPerLine * 8;
                for (int y = 0; y < _height; y++)
                {
                    for (int x = 0; x < _width; x++)
                    {
                        byte value = planes[0][y * stride + x];
                        int p = (y * _width + x) * 4;
                        pixels[p] = value;
                        pixels[p + 1] = value;
                        pixels[p + 2] = value;
                        pixels[p + 3] = 255;
                    }
                }

                return bitmap;
            }

            Component first = _components[0];
            Component second = _components[1];
            Component third = _components[2];
            if (first.H != _maxH || first.V != _maxV || second.H != third.H || second.V != third.V ||
                _maxH % second.H != 0 || _maxV % second.V != 0)
                throw new NotSupportedException("Unsupported JPEG sampling layout.");

            int fullStride = first.BlocksPerLine * 8;
            int chromaStride = second.BlocksPerLine * 8;
            int chromaWidth = CeilDiv(_width * second.H, _maxH);
            int chromaHeight = CeilDiv(_height * second.V, _maxV);

            // Adobe transform 0 (or R/G/B component ids without an Adobe marker) means RGB, not YCbCr.
            bool isRgb = _adobeTransform == 0 ||
                         (_adobeTransform < 0 && first.Id == 'R' && second.Id == 'G' && third.Id == 'B');
            if (!isRgb)
            {
                YCbCrConverter.ToRgba(
                    planes[0],
                    fullStride,
                    planes[1],
                    planes[2],
                    chromaStride,
                    chromaWidth,
                    chromaHeight,
                    _maxH / second.H,
                    _maxV / second.V,
                    _width,
                    _height,
                    false,
                    pixels);
                return bitmap;
            }

            if (second.H != first.H || second.V != first.V)
                throw new NotSupportedException("Subsampled RGB JPEG images are not supported.");

            for (int y = 0; y < _height; y++)
            {
                for (int x = 0; x < _width; x++)
                {
                    int source = y * fullStride + x;
                    int p = (y * _width + x) * 4;
                    pixels[p] = planes[0][source];
                    pixels[p + 1] = planes[1][source];
                    pixels[p + 2] = planes[2][source];
                    pixels[p + 3] = 255;
                }
            }

            return bitmap;
        }

        /// <summary>Canonical JPEG Huffman table with a 9-bit lookahead for short codes.</summary>
        sealed class HuffmanTable
        {
            const int LookaheadBits = 9;

            readonly short[] _lookup = new short[1 << LookaheadBits];
            readonly byte[] _lookupLength = new byte[1 << LookaheadBits];
            readonly int[] _maxCode = new int[18];
            readonly int[] _valueOffset = new int[17];
            readonly byte[] _symbols;

            public HuffmanTable(byte[] counts, byte[] symbols)
            {
                _symbols = symbols;
                int code = 0;
                int index = 0;
                for (int length = 1; length <= 16; length++)
                {
                    _valueOffset[length] = index - code;
                    for (int i = 0; i < counts[length]; i++, index++, code++)
                    {
                        if (length <= LookaheadBits)
                        {
                            int first = code << (LookaheadBits - length);
                            int span = 1 << (LookaheadBits - length);
                            for (int j = 0; j < span; j++)
                            {
                                _lookup[first + j] = symbols[index];
                                _lookupLength[first + j] = (byte)length;
                            }
                        }
                    }

                    _maxCode[length] = counts[length] > 0 ? code - 1 : -1;
                    code <<= 1;
                }

                _maxCode[17] = int.MaxValue;
            }

            public int Decode(JpegReader reader)
            {
                uint bits = reader.PeekBits();
                int peek = (int)(bits >> (32 - LookaheadBits));
                int length = _lookupLength[peek];
                if (length != 0)
                {
                    reader.SkipBits(length);
                    return _lookup[peek];
                }

                for (length = LookaheadBits + 1; length <= 16; length++)
                {
                    int code = (int)(bits >> (32 - length));
                    if (code <= _maxCode[length])
                    {
                        reader.SkipBits(length);
                        return _symbols[code + _valueOffset[length]];
                    }
                }

                throw new InvalidDataException("Invalid JPEG Huffman code.");
            }
        }
    }
}
