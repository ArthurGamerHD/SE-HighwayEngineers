// ReSharper disable RedundantUsingDirective
using System;
using Adk.Image;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Mpeg1
{
    /// <summary>One decoded MPEG-1 picture as macroblock-aligned 4:2:0 planes.</summary>
    public sealed class Mpeg1Picture
    {
        public readonly byte[] Luma;
        public readonly byte[] Cb;
        public readonly byte[] Cr;
        public readonly int LumaStride;
        public readonly int ChromaStride;

        /// <summary>Displayed size from the sequence header; the planes are padded to whole macroblocks.</summary>
        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>1 = I, 2 = P, 3 = B.</summary>
        public int PictureType { get; internal set; }

        /// <summary>The time passed to <see cref="Mpeg1VideoDecoder.Decode"/> with the data of this picture.</summary>
        public double Time { get; internal set; }

        internal readonly int PlaneHeight;
        internal readonly int ChromaHeight;

        internal Mpeg1Picture(int width, int height, int macroblocksWide, int macroblocksHigh)
        {
            Width = width;
            Height = height;
            LumaStride = macroblocksWide * 16;
            ChromaStride = macroblocksWide * 8;
            PlaneHeight = macroblocksHigh * 16;
            ChromaHeight = macroblocksHigh * 8;
            Luma = new byte[LumaStride * PlaneHeight];
            Cb = new byte[ChromaStride * ChromaHeight];
            Cr = new byte[ChromaStride * ChromaHeight];

            // Start as black rather than green, in case a stream begins without its reference.
            for (int i = 0; i < Luma.Length; i++)
                Luma[i] = 16;
            for (int i = 0; i < Cb.Length; i++)
            {
                Cb[i] = 128;
                Cr[i] = 128;
            }
        }

        /// <summary>Converts to opaque RGBA8, <c>Width * Height * 4</c> bytes.</summary>
        public void ToRgba(byte[] rgba)
        {
            ToRgba(rgba, 0, Height);
        }

        /// <summary>
        /// Converts rows <paramref name="firstRow"/> to <paramref name="endRow"/> (exclusive) of the
        /// <c>Width * Height * 4</c> RGBA8 image. Disjoint row ranges may be converted in parallel.
        /// </summary>
        public void ToRgba(byte[] rgba, int firstRow, int endRow)
        {
            YCbCrConverter.ToRgba(Layout, Luma, LumaStride, Cb, Cr, ChromaStride, rgba, firstRow, endRow);
        }

        // Shared by every picture of the same size; built on first use.
        YCbCrLayout Layout
        {
            get
            {
                if (_layout == null)
                    _layout = new YCbCrLayout((Width + 1) / 2, (Height + 1) / 2, 2, 2, Width, Height, true);

                return _layout;
            }
        }

        YCbCrLayout _layout;
    }

    /// <summary>
    /// ISO/IEC 11172-2 (MPEG-1) video decoder for I, P and B pictures (D pictures are skipped).
    /// Feed it one access unit at a time (for Matroska, one frame), in decode order.
    /// <para>
    /// With <see cref="LowDelay"/> set, every I/P picture is returned as soon as it is
    /// decoded, which is display order for streams without B pictures. Otherwise reference
    /// pictures are held back by one so pictures come out in display order; call
    /// <see cref="Flush"/> at the end for the last one.
    /// </para>
    /// Returned pictures are reused buffers, valid until the next call.
    /// </summary>
    public sealed class Mpeg1VideoDecoder
    {
        const int SequenceHeaderCode = 0xB3;
        const int PictureStartCode = 0x00;
        const int SliceFirst = 0x01;
        const int SliceLast = 0xAF;

        const int TypeIntra = 1;
        const int TypePredictive = 2;
        const int TypeBidirectional = 3;

        readonly Mpeg1BitReader _reader = new Mpeg1BitReader();
        readonly byte[] _intraQuant = new byte[64];
        readonly byte[] _nonIntraQuant = new byte[64];
        readonly int[] _block = new int[64];
        readonly int[] _dcPredictor = new int[3];

        int _width;
        int _height;
        int _macroblocksWide;
        int _macroblocksHigh;
        int _macroblockCount;
        bool _hasSequence;

        Mpeg1Picture _current;
        Mpeg1Picture _forward;
        Mpeg1Picture _backward;
        int _referenceCount;

        int _pictureType;
        bool _forwardFullPel;
        bool _backwardFullPel;
        int _forwardRSize;
        int _backwardRSize;

        int _quantizerScale;
        int _macroblockAddress;
        int _macroblockRow;
        int _macroblockColumn;
        bool _sliceBegin;
        bool _macroblockIntra;
        bool _forwardSet;
        bool _backwardSet;
        int _forwardH;
        int _forwardV;
        int _backwardH;
        int _backwardV;

        /// <summary>Return I/P pictures immediately instead of reordering for B pictures. On by default.</summary>
        public bool LowDelay { get; set; }

        public int Width => _width;
        public int Height => _height;
        public bool HasSequenceHeader => _hasSequence;

        /// <param name="codecPrivate">Optional data holding a sequence header, such as Matroska's CodecPrivate.</param>
        public Mpeg1VideoDecoder(byte[] codecPrivate = null)
        {
            LowDelay = true;
            if (codecPrivate != null && codecPrivate.Length > 0)
                Decode(codecPrivate, 0, codecPrivate.Length, 0);
        }

        /// <summary>Forgets the reference pictures, e.g. before decoding from a new key frame.</summary>
        public void Reset()
        {
            _referenceCount = 0;
        }

        /// <summary>Returns the held-back reference picture when <see cref="LowDelay"/> is off, or null.</summary>
        public Mpeg1Picture Flush()
        {
            if (LowDelay || _referenceCount == 0)
                return null;

            _referenceCount = 0;
            return _backward;
        }

        /// <summary>
        /// Decodes the pictures in one access unit and returns the one to display now, or null.
        /// <paramref name="time"/> is stored on the picture it belongs to.
        /// </summary>
        public Mpeg1Picture Decode(byte[] data, int offset, int length, double time)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            if (offset < 0 || length < 0 || offset > data.Length - length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            _reader.Reset(data, offset, length);
            Mpeg1Picture output = null;
            int code = _reader.NextStartCode();
            while (code >= 0)
            {
                if (code == SequenceHeaderCode)
                {
                    ReadSequenceHeader();
                    code = _reader.NextStartCode();
                }
                else if (code == PictureStartCode && _hasSequence)
                {
                    Mpeg1Picture picture;
                    code = DecodePicture(time, out picture);
                    if (picture != null)
                        output = picture;
                }
                else
                {
                    code = _reader.NextStartCode();
                }
            }

            return output;
        }

        /// <summary>Picture coding type (1 = I, 2 = P, 3 = B) of the first picture in an access unit, or 0.</summary>
        public static int GetPictureType(byte[] data, int offset, int length)
        {
            int end = offset + length - 5;
            for (int i = offset; i < end; i++)
            {
                if (data[i] == 0 && data[i + 1] == 0 && data[i + 2] == 1 && data[i + 3] == PictureStartCode)
                    return (data[i + 5] >> 3) & 7;
            }

            return 0;
        }

        void ReadSequenceHeader()
        {
            int width = _reader.Read(12);
            int height = _reader.Read(12);

            // Aspect ratio, picture rate, bit rate, marker, VBV buffer size, constrained flag.
            _reader.Skip(4 + 4 + 18 + 1 + 10 + 1);

            if (_reader.ReadBit())
            {
                for (int i = 0; i < 64; i++)
                    _intraQuant[InverseDct.ZigZag[i]] = (byte)_reader.Read(8);
            }
            else
            {
                Buffer.BlockCopy(Mpeg1Tables.DefaultIntraQuant, 0, _intraQuant, 0, 64);
            }

            if (_reader.ReadBit())
            {
                for (int i = 0; i < 64; i++)
                    _nonIntraQuant[InverseDct.ZigZag[i]] = (byte)_reader.Read(8);
            }
            else
            {
                for (int i = 0; i < 64; i++)
                    _nonIntraQuant[i] = 16;
            }

            if (width <= 0 || height <= 0)
                throw new InvalidDataException("MPEG-1 sequence header has no picture size.");

            if (_hasSequence && width == _width && height == _height)
                return;

            _width = width;
            _height = height;
            _macroblocksWide = (width + 15) >> 4;
            _macroblocksHigh = (height + 15) >> 4;
            _macroblockCount = _macroblocksWide * _macroblocksHigh;
            _current = new Mpeg1Picture(width, height, _macroblocksWide, _macroblocksHigh);
            _forward = new Mpeg1Picture(width, height, _macroblocksWide, _macroblocksHigh);
            _backward = new Mpeg1Picture(width, height, _macroblocksWide, _macroblocksHigh);
            _referenceCount = 0;
            _hasSequence = true;
        }

        int DecodePicture(double time, out Mpeg1Picture output)
        {
            output = null;
            _reader.Skip(10); // temporal_reference
            _pictureType = _reader.Read(3);
            _reader.Skip(16); // vbv_delay

            if (_pictureType == TypePredictive || _pictureType == TypeBidirectional)
            {
                _forwardFullPel = _reader.ReadBit();
                _forwardRSize = Math.Max(0, _reader.Read(3) - 1);
            }

            if (_pictureType == TypeBidirectional)
            {
                _backwardFullPel = _reader.ReadBit();
                _backwardRSize = Math.Max(0, _reader.Read(3) - 1);
            }

            while (_reader.ReadBit())
                _reader.Skip(8); // extra_information_picture

            bool decodable = _pictureType == TypeIntra ||
                             (_pictureType == TypePredictive && _referenceCount >= 1) ||
                             (_pictureType == TypeBidirectional && _referenceCount >= 2);

            Mpeg1Picture free = _forward;
            if (decodable && _pictureType != TypeBidirectional)
                _forward = _backward;

            int code = _reader.NextStartCode();
            while (code >= SliceFirst && code <= SliceLast)
            {
                if (decodable)
                    DecodeSlice(code);

                code = _reader.NextStartCode();
            }

            if (!decodable)
                return code;

            _current.PictureType = _pictureType;
            _current.Time = time;

            if (_pictureType == TypeBidirectional)
            {
                output = _current;
                return code;
            }

            // The new reference replaces the oldest one.
            _backward = _current;
            _current = free;
            bool hadReference = _referenceCount > 0;
            _referenceCount = Math.Min(2, _referenceCount + 1);
            output = LowDelay ? _backward : hadReference ? _forward : null;
            return code;
        }

        void DecodeSlice(int verticalPosition)
        {
            _macroblockAddress = (verticalPosition - 1) * _macroblocksWide - 1;
            _quantizerScale = _reader.Read(5);
            while (_reader.ReadBit())
                _reader.Skip(8); // extra_information_slice

            _dcPredictor[0] = 128;
            _dcPredictor[1] = 128;
            _dcPredictor[2] = 128;
            _forwardH = _forwardV = _backwardH = _backwardV = 0;
            _sliceBegin = true;

            try
            {
                while (_reader.HasMore && _reader.NextBitsNonZero23())
                {
                    if (!DecodeMacroblock())
                        break;
                }
            }
            catch (InvalidDataException)
            {
                // A damaged slice keeps what it decoded; the next slice resynchronizes.
            }
        }

        bool DecodeMacroblock()
        {
            int increment = 0;
            int code = Mpeg1Tables.MacroblockAddressIncrement.Read(_reader);
            while (code == Mpeg1Tables.AddressStuffing)
                code = Mpeg1Tables.MacroblockAddressIncrement.Read(_reader);

            while (code == Mpeg1Tables.AddressEscape)
            {
                increment += 33;
                code = Mpeg1Tables.MacroblockAddressIncrement.Read(_reader);
            }

            increment += code;

            if (_sliceBegin)
            {
                _sliceBegin = false;
                _macroblockAddress += increment;
            }
            else
            {
                if (_macroblockAddress + increment >= _macroblockCount)
                    return false;

                if (increment > 1)
                {
                    // Skipped macroblocks reset the DC predictors, and in P pictures the motion vectors.
                    _dcPredictor[0] = 128;
                    _dcPredictor[1] = 128;
                    _dcPredictor[2] = 128;
                    if (_pictureType == TypePredictive)
                        _forwardH = _forwardV = 0;
                }

                // Skipped macroblocks repeat the prediction with the current vectors.
                for (; increment > 1; increment--)
                {
                    _macroblockAddress++;
                    UpdatePosition();
                    PredictMacroblock();
                }

                _macroblockAddress++;
            }

            if ((uint)_macroblockAddress >= (uint)_macroblockCount)
                return false;

            UpdatePosition();

            int type = Mpeg1Tables.MacroblockType(_pictureType).Read(_reader);
            _macroblockIntra = (type & Mpeg1Tables.Intra) != 0;
            _forwardSet = (type & Mpeg1Tables.Forward) != 0;
            _backwardSet = (type & Mpeg1Tables.Backward) != 0;

            if ((type & Mpeg1Tables.Quant) != 0)
                _quantizerScale = _reader.Read(5);

            if (_macroblockIntra)
            {
                _forwardH = _forwardV = _backwardH = _backwardV = 0;
            }
            else
            {
                _dcPredictor[0] = 128;
                _dcPredictor[1] = 128;
                _dcPredictor[2] = 128;
                DecodeMotionVectors();
                PredictMacroblock();
            }

            int pattern = (type & Mpeg1Tables.Pattern) != 0
                ? Mpeg1Tables.CodedBlockPattern.Read(_reader)
                : _macroblockIntra ? 0x3F : 0;

            for (int block = 0, mask = 0x20; block < 6; block++, mask >>= 1)
            {
                if ((pattern & mask) != 0)
                    DecodeBlock(block);
            }

            return true;
        }

        void UpdatePosition()
        {
            _macroblockRow = _macroblockAddress / _macroblocksWide;
            _macroblockColumn = _macroblockAddress % _macroblocksWide;
        }

        void DecodeMotionVectors()
        {
            if (_forwardSet)
            {
                _forwardH = DecodeMotionVector(_forwardRSize, _forwardH);
                _forwardV = DecodeMotionVector(_forwardRSize, _forwardV);
            }
            else if (_pictureType == TypePredictive)
            {
                // A P macroblock without forward motion predicts from the same place.
                _forwardH = _forwardV = 0;
            }

            if (_backwardSet)
            {
                _backwardH = DecodeMotionVector(_backwardRSize, _backwardH);
                _backwardV = DecodeMotionVector(_backwardRSize, _backwardV);
            }
        }

        int DecodeMotionVector(int rSize, int previous)
        {
            int scale = 1 << rSize;
            int code = Mpeg1Tables.MotionCode.Read(_reader);
            int delta;
            if (code != 0 && scale != 1)
            {
                int residual = _reader.Read(rSize);
                delta = ((Math.Abs(code) - 1) << rSize) + residual + 1;
                if (code < 0)
                    delta = -delta;
            }
            else
            {
                delta = code;
            }

            // Vectors wrap within [-16 * scale, 16 * scale).
            int vector = previous + delta;
            if (vector > (scale << 4) - 1)
                vector -= scale << 5;
            else if (vector < -(scale << 4))
                vector += scale << 5;

            return vector;
        }

        void PredictMacroblock()
        {
            int forwardH = _forwardFullPel ? _forwardH << 1 : _forwardH;
            int forwardV = _forwardFullPel ? _forwardV << 1 : _forwardV;

            if (_pictureType != TypeBidirectional)
            {
                Predict(_forward, forwardH, forwardV, false);
                return;
            }

            int backwardH = _backwardFullPel ? _backwardH << 1 : _backwardH;
            int backwardV = _backwardFullPel ? _backwardV << 1 : _backwardV;
            if (_forwardSet)
            {
                Predict(_forward, forwardH, forwardV, false);
                if (_backwardSet)
                    Predict(_backward, backwardH, backwardV, true);
            }
            else
            {
                Predict(_backward, backwardH, backwardV, false);
            }
        }

        void Predict(Mpeg1Picture reference, int motionH, int motionV, bool average)
        {
            PredictBlock(reference.Luma, _current.Luma, reference.LumaStride, reference.PlaneHeight, 16,
                _macroblockColumn * 16, _macroblockRow * 16, motionH, motionV, average);

            // Chroma vectors are half the luma ones, truncated toward zero.
            int chromaH = motionH / 2;
            int chromaV = motionV / 2;
            PredictBlock(reference.Cb, _current.Cb, reference.ChromaStride, reference.ChromaHeight, 8,
                _macroblockColumn * 8, _macroblockRow * 8, chromaH, chromaV, average);
            PredictBlock(reference.Cr, _current.Cr, reference.ChromaStride, reference.ChromaHeight, 8,
                _macroblockColumn * 8, _macroblockRow * 8, chromaH, chromaV, average);
        }

        static void PredictBlock(
            byte[] source,
            byte[] destination,
            int stride,
            int planeHeight,
            int size,
            int x,
            int y,
            int motionH,
            int motionV,
            bool average)
        {
            int halfH = motionH & 1;
            int halfV = motionV & 1;
            int sourceX = x + (motionH >> 1);
            int sourceY = y + (motionV >> 1);

            // Valid streams stay inside the picture; clamp so damaged ones cannot read outside it.
            sourceX = Math.Max(0, Math.Min(sourceX, stride - size - halfH));
            sourceY = Math.Max(0, Math.Min(sourceY, planeHeight - size - halfV));

            int mode = (halfV << 1) | halfH;
            for (int row = 0; row < size; row++)
            {
                int s = (sourceY + row) * stride + sourceX;
                int d = (y + row) * stride + x;
                for (int column = 0; column < size; column++, s++, d++)
                {
                    int value;
                    switch (mode)
                    {
                        case 0:
                            value = source[s];
                            break;
                        case 1:
                            value = (source[s] + source[s + 1] + 1) >> 1;
                            break;
                        case 2:
                            value = (source[s] + source[s + stride] + 1) >> 1;
                            break;
                        default:
                            value = (source[s] + source[s + 1] + source[s + stride] + source[s + stride + 1] + 2) >> 2;
                            break;
                    }

                    destination[d] = average ? (byte)((destination[d] + value + 1) >> 1) : (byte)value;
                }
            }
        }

        void DecodeBlock(int blockIndex)
        {
            int[] block = _block;
            Array.Clear(block, 0, 64);

            byte[] quant;
            int n = 0;
            if (_macroblockIntra)
            {
                int plane = blockIndex > 3 ? blockIndex - 3 : 0;
                int size = (plane == 0 ? Mpeg1Tables.DcSizeLuminance : Mpeg1Tables.DcSizeChrominance).Read(_reader);
                int dc = _dcPredictor[plane];
                if (size > 0)
                {
                    int differential = _reader.Read(size);
                    if ((differential & (1 << (size - 1))) == 0)
                        differential -= (1 << size) - 1;

                    dc += differential;
                }

                _dcPredictor[plane] = dc;
                block[0] = dc * 8;
                quant = _intraQuant;
                n = 1;
            }
            else
            {
                quant = _nonIntraQuant;
            }

            while (true)
            {
                int coefficient = Mpeg1Tables.DctCoefficient.Read(_reader);

                // "10" ends the block once a coefficient has been read.
                if (coefficient == 0x0001 && n > 0 && !_reader.ReadBit())
                    break;

                int run;
                int level;
                if (coefficient == Mpeg1Tables.CoefficientEscape)
                {
                    run = _reader.Read(6);
                    level = _reader.Read(8);
                    if (level == 0)
                        level = _reader.Read(8);
                    else if (level == 128)
                        level = _reader.Read(8) - 256;
                    else if (level > 128)
                        level -= 256;
                }
                else
                {
                    run = coefficient >> 8;
                    level = coefficient & 0xFF;
                    if (_reader.ReadBit())
                        level = -level;
                }

                n += run;
                if (n > 63)
                    throw new InvalidDataException("MPEG-1 block has more than 64 coefficients.");

                int index = InverseDct.ZigZag[n++];

                // Dequantize on the magnitude so division truncates toward zero, then make it
                // odd toward zero (MPEG-1 mismatch control) and saturate.
                int magnitude = level < 0 ? -level : level;
                int value = _macroblockIntra
                    ? (2 * magnitude * _quantizerScale * quant[index]) >> 4
                    : ((2 * magnitude + 1) * _quantizerScale * quant[index]) >> 4;
                if ((value & 1) == 0 && value != 0)
                    value--;

                if (level < 0)
                    value = -value;

                block[index] = value > 2047 ? 2047 : value < -2048 ? -2048 : value;
            }

            InverseDct.Transform(block, 0);

            byte[] destination;
            int stride;
            int offset;
            if (blockIndex < 4)
            {
                destination = _current.Luma;
                stride = _current.LumaStride;
                offset = (_macroblockRow * 16 + (blockIndex >> 1) * 8) * stride +
                         _macroblockColumn * 16 + (blockIndex & 1) * 8;
            }
            else
            {
                destination = blockIndex == 4 ? _current.Cb : _current.Cr;
                stride = _current.ChromaStride;
                offset = _macroblockRow * 8 * stride + _macroblockColumn * 8;
            }

            for (int row = 0, i = 0; row < 8; row++, offset += stride)
            {
                for (int column = 0; column < 8; column++, i++)
                {
                    int value = _macroblockIntra ? block[i] : destination[offset + column] + block[i];
                    destination[offset + column] = value < 0 ? (byte)0 : value > 255 ? (byte)255 : (byte)value;
                }
            }
        }
    }
}
