// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.Text;
using Adk.Image.Png;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;
using ZipCrc32 = Adk.Compression.Zip.Crc32;

namespace Adk.Video.Apng
{
    /// <summary>
    /// Indexed Animated PNG (APNG) stream. Loading only walks the chunk layout;
    /// frame pixels are inflated on demand by <see cref="ApngFrameDecoder"/>, so a
    /// long video costs its compressed size plus one canvas in memory.
    /// A plain PNG loads as a single frame video. Audio and captions ride along in the
    /// private chunks described by <see cref="ApngMediaChunks"/>.
    /// </summary>
    public sealed class ApngVideo
    {
        readonly byte[] _data;
        readonly ApngFrameInfo[] _frames;

        public int Width { get; private set; }
        public int Height { get; private set; }

        /// <summary>Times the animation is meant to play; 0 means loop forever.</summary>
        public int PlayCount { get; private set; }

        public int FrameCount => _frames.Length;

        /// <summary>Sum of every frame delay, in seconds.</summary>
        public double Duration { get; private set; }

        /// <summary>Audio muxed in <c>vaAh</c>/<c>vaAu</c> chunks, or null for a silent video.</summary>
        public ApngAudioTrack Audio { get; private set; }

        /// <summary>Caption cues from <c>vaCp</c> chunks, ordered by start time.</summary>
        public ApngCaption[] Captions { get; private set; }

        internal int BitDepth { get; private set; }
        internal int ColorType { get; private set; }
        internal int InterlaceMethod { get; private set; }
        internal byte[] Palette { get; private set; }
        internal byte[] Transparency { get; private set; }
        internal byte[] Data => _data;

        ApngVideo(byte[] data, ApngFrameInfo[] frames)
        {
            _data = data;
            _frames = frames;
        }

        public ApngFrameInfo GetFrame(int index)
        {
            if ((uint)index >= (uint)_frames.Length)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _frames[index];
        }

        /// <summary>Index of the frame showing at <paramref name="seconds"/> into one play of the animation.</summary>
        public int FrameAtTime(double seconds)
        {
            if (seconds <= 0 || _frames.Length == 1)
                return 0;

            for (int i = 0; i < _frames.Length; i++)
            {
                ApngFrameInfo frame = _frames[i];
                if (seconds < frame.StartTime + frame.Delay)
                    return i;
            }

            return _frames.Length - 1;
        }

        /// <summary>Text of the caption showing at <paramref name="seconds"/>, or null when none is.</summary>
        public string GetCaptionAt(double seconds)
        {
            // Cues may overlap; the latest one to start wins.
            string text = null;
            for (int i = 0; i < Captions.Length && Captions[i].Start <= seconds; i++)
            {
                if (seconds < Captions[i].End)
                    text = Captions[i].Text;
            }

            return text;
        }

        public ApngFrameDecoder CreateDecoder()
        {
            return new ApngFrameDecoder(this);
        }

        /// <summary>
        /// Reads the chunk layout of an APNG (or PNG) file. The array is kept, not copied,
        /// and must not be changed while the video is in use.
        /// </summary>
        public static ApngVideo Load(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            byte[] signature = PngDecoder.PngSignature;
            if (data.Length < signature.Length)
                throw new InvalidDataException("The data is not a PNG image.");

            for (int i = 0; i < signature.Length; i++)
            {
                if (data[i] != signature[i])
                    throw new InvalidDataException("The data is not a PNG image.");
            }

            var frames = new List<ApngFrameInfo>();
            ApngFrameInfo current = null;
            int width = 0;
            int height = 0;
            int bitDepth = 0;
            int colorType = -1;
            int interlaceMethod = -1;
            byte[] palette = null;
            byte[] transparency = null;
            bool sawHeader = false;
            bool sawAnimationControl = false;
            bool sawImageData = false;
            bool sawEnd = false;
            int declaredFrames = 0;
            int playCount = 0;
            int expectedSequence = 0;
            var defaultImage = new List<ChunkRange>();
            ApngAudioTrack audio = null;
            var captions = new List<ApngCaption>();

            int position = signature.Length;
            while (!sawEnd)
            {
                if (position > data.Length - 12)
                    throw new InvalidDataException("APNG ended before its IEND chunk.");

                uint unsignedLength = PngDecoder.ReadUInt32BigEndian(data, position);
                if (unsignedLength > (uint)(data.Length - position - 12))
                    throw new InvalidDataException("APNG chunk runs past the end of the data.");

                int length = (int)unsignedLength;
                int typeOffset = position + 4;
                int dataOffset = position + 8;
                string type = ReadType(data, typeOffset);

                if (type == "IHDR")
                {
                    if (sawHeader || length != 13)
                        throw new InvalidDataException("Invalid PNG IHDR chunk.");

                    VerifyCrc(data, position, length);
                    width = checked((int)PngDecoder.ReadUInt32BigEndian(data, dataOffset));
                    height = checked((int)PngDecoder.ReadUInt32BigEndian(data, dataOffset + 4));
                    bitDepth = data[dataOffset + 8];
                    colorType = data[dataOffset + 9];
                    interlaceMethod = data[dataOffset + 12];

                    if (width <= 0 || height <= 0)
                        throw new InvalidDataException("Invalid PNG dimensions.");

                    PngDecoder.ValidateFormat(bitDepth, colorType);
                    if (data[dataOffset + 10] != 0 || data[dataOffset + 11] != 0)
                        throw new NotSupportedException("Unsupported PNG compression or filter method.");

                    if (interlaceMethod != 0 && interlaceMethod != 1)
                        throw new NotSupportedException("Unsupported PNG interlace method: " + interlaceMethod + ".");

                    sawHeader = true;
                }
                else if (!sawHeader)
                {
                    throw new InvalidDataException("PNG chunk " + type + " appeared before IHDR.");
                }
                else if (type == "acTL")
                {
                    if (sawAnimationControl || sawImageData || length != 8)
                        throw new InvalidDataException("Invalid APNG acTL chunk.");

                    VerifyCrc(data, position, length);
                    declaredFrames = checked((int)PngDecoder.ReadUInt32BigEndian(data, dataOffset));
                    playCount = checked((int)PngDecoder.ReadUInt32BigEndian(data, dataOffset + 4));
                    sawAnimationControl = true;
                }
                else if (type == "fcTL")
                {
                    if (length != 26)
                        throw new InvalidDataException("Invalid APNG fcTL chunk.");

                    VerifyCrc(data, position, length);
                    ReadSequence(data, dataOffset, ref expectedSequence);
                    current = ReadFrameControl(data, dataOffset + 4, width, height, frames.Count);
                    frames.Add(current);
                }
                else if (type == "PLTE")
                {
                    if (length == 0 || length > 768 || (length % 3) != 0)
                        throw new InvalidDataException("Invalid PNG PLTE chunk.");

                    VerifyCrc(data, position, length);
                    palette = Slice(data, dataOffset, length);
                }
                else if (type == "tRNS")
                {
                    VerifyCrc(data, position, length);
                    transparency = Slice(data, dataOffset, length);
                }
                else if (type == "IDAT")
                {
                    // An fcTL ahead of IDAT makes the default image the first frame;
                    // without one the default image is a fallback the animation skips.
                    if (current != null && frames.Count == 1)
                        current.Chunks.Add(new ChunkRange(position, dataOffset, length));
                    else if (frames.Count == 0)
                        defaultImage.Add(new ChunkRange(position, dataOffset, length));
                    else
                        throw new InvalidDataException("PNG IDAT appeared after APNG frame data.");

                    sawImageData = true;
                }
                else if (type == "fdAT")
                {
                    if (current == null || length < 4)
                        throw new InvalidDataException("APNG fdAT appeared before its fcTL.");

                    ReadSequence(data, dataOffset, ref expectedSequence);
                    current.Chunks.Add(new ChunkRange(position, dataOffset + 4, length - 4));
                }
                else if (type == ApngMediaChunks.AudioHeader)
                {
                    if (audio != null)
                        throw new InvalidDataException("APNG has more than one vaAh chunk.");

                    VerifyCrc(data, position, length);
                    audio = new ApngAudioTrack(data);
                    audio.ReadFormat(dataOffset, length);
                }
                else if (type == ApngMediaChunks.AudioData)
                {
                    if (audio == null || length < 4)
                        throw new InvalidDataException("APNG vaAu appeared before its vaAh.");

                    audio.AddBlock(position, dataOffset, length);
                }
                else if (type == ApngMediaChunks.Caption)
                {
                    if (length < 8)
                        throw new InvalidDataException("Invalid APNG vaCp chunk.");

                    VerifyCrc(data, position, length);
                    captions.Add(new ApngCaption(
                        PngDecoder.ReadUInt32BigEndian(data, dataOffset) / 1000.0,
                        PngDecoder.ReadUInt32BigEndian(data, dataOffset + 4) / 1000.0,
                        Encoding.UTF8.GetString(data, dataOffset + 8, length - 8)));
                }
                else if (type == "IEND")
                {
                    sawEnd = true;
                }
                else if (PngDecoder.IsCriticalChunk(new[]
                         {
                             data[typeOffset], data[typeOffset + 1], data[typeOffset + 2], data[typeOffset + 3]
                         }))
                {
                    throw new NotSupportedException("Unsupported critical PNG chunk: " + type);
                }

                position = dataOffset + length + 4;
            }

            if (!sawHeader || !sawImageData)
                throw new InvalidDataException("PNG is missing IHDR or IDAT data.");

            if (colorType == 3 && palette == null)
                throw new InvalidDataException("Indexed-color PNG is missing its PLTE palette.");

            PngDecoder.ValidateTransparency(colorType, palette, transparency);

            if (!sawAnimationControl || frames.Count == 0)
            {
                // Plain PNG, or an APNG whose animation is unusable: show the default image.
                var still = new ApngFrameInfo(0, width, height, 0, 0, 0, ApngDisposeOp.None, ApngBlendOp.Source);
                still.Chunks.AddRange(defaultImage.Count > 0 ? defaultImage : frames[0].Chunks);
                frames.Clear();
                frames.Add(still);
                playCount = 0;
            }
            else if (frames.Count != declaredFrames)
            {
                throw new InvalidDataException(
                    "APNG declares " + declaredFrames + " frames but contains " + frames.Count + ".");
            }

            double time = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                if (frames[i].Chunks.Count == 0)
                    throw new InvalidDataException("APNG frame " + i + " has no image data.");

                frames[i].StartTime = time;
                time += frames[i].Delay;
            }

            // The spec treats a PREVIOUS dispose on the first frame as BACKGROUND.
            if (frames[0].DisposeOp == ApngDisposeOp.Previous)
                frames[0].DisposeOp = ApngDisposeOp.Background;

            frames[0].IsKeyFrame = true;
            for (int i = 1; i < frames.Count; i++)
            {
                ApngFrameInfo frame = frames[i];
                ApngFrameInfo previous = frames[i - 1];

                // Blending over a canvas the previous frame cleared is the same as replacing it.
                frame.IsKeyFrame = CoversCanvas(frame, width, height) &&
                                   (frame.BlendOp == ApngBlendOp.Source ||
                                    previous.DisposeOp == ApngDisposeOp.Background &&
                                    CoversCanvas(previous, width, height));
            }

            return new ApngVideo(data, frames.ToArray())
            {
                Width = width,
                Height = height,
                PlayCount = playCount,
                Duration = time,
                Audio = audio,
                Captions = SortCaptions(captions),
                BitDepth = bitDepth,
                ColorType = colorType,
                InterlaceMethod = interlaceMethod,
                Palette = palette,
                Transparency = transparency
            };
        }

        static ApngFrameInfo ReadFrameControl(byte[] data, int offset, int canvasWidth, int canvasHeight, int index)
        {
            int width = checked((int)PngDecoder.ReadUInt32BigEndian(data, offset));
            int height = checked((int)PngDecoder.ReadUInt32BigEndian(data, offset + 4));
            int x = checked((int)PngDecoder.ReadUInt32BigEndian(data, offset + 8));
            int y = checked((int)PngDecoder.ReadUInt32BigEndian(data, offset + 12));
            int delayNumerator = PngDecoder.ReadUInt16BigEndian(data, offset + 16);
            int delayDenominator = PngDecoder.ReadUInt16BigEndian(data, offset + 18);
            byte dispose = data[offset + 20];
            byte blend = data[offset + 21];

            if (width <= 0 || height <= 0 || x > canvasWidth - width || y > canvasHeight - height)
                throw new InvalidDataException("APNG frame " + index + " lies outside the canvas.");

            if (dispose > 2 || blend > 1)
                throw new InvalidDataException("APNG frame " + index + " has an invalid dispose or blend op.");

            // A zero denominator means hundredths of a second.
            double delay = delayNumerator / (double)(delayDenominator == 0 ? 100 : delayDenominator);
            return new ApngFrameInfo(index, width, height, x, y, delay, (ApngDisposeOp)dispose, (ApngBlendOp)blend);
        }

        static ApngCaption[] SortCaptions(List<ApngCaption> captions)
        {
            // Insertion sort keeps muxing order for cues that start together.
            ApngCaption[] sorted = captions.ToArray();
            for (int i = 1; i < sorted.Length; i++)
            {
                ApngCaption cue = sorted[i];
                int j = i - 1;
                for (; j >= 0 && sorted[j].Start > cue.Start; j--)
                    sorted[j + 1] = sorted[j];
                sorted[j + 1] = cue;
            }

            return sorted;
        }

        static bool CoversCanvas(ApngFrameInfo frame, int width, int height)
        {
            return frame.X == 0 && frame.Y == 0 && frame.Width == width && frame.Height == height;
        }

        static void ReadSequence(byte[] data, int offset, ref int expected)
        {
            uint sequence = PngDecoder.ReadUInt32BigEndian(data, offset);
            if (sequence != (uint)expected)
                throw new InvalidDataException("APNG chunk sequence " + sequence + " is out of order.");

            expected++;
        }

        internal static void VerifyCrc(byte[] data, int chunkOffset, int length)
        {
            uint crc = ZipCrc32.Update(0xFFFFFFFFu, data, chunkOffset + 4, length + 4);
            uint expected = PngDecoder.ReadUInt32BigEndian(data, chunkOffset + 8 + length);
            if (ZipCrc32.Finish(crc) != expected)
                throw new InvalidDataException("PNG chunk CRC check failed.");
        }

        static string ReadType(byte[] data, int offset)
        {
            return new string(new[]
            {
                (char)data[offset], (char)data[offset + 1], (char)data[offset + 2], (char)data[offset + 3]
            });
        }

        static byte[] Slice(byte[] data, int offset, int length)
        {
            byte[] slice = new byte[length];
            Buffer.BlockCopy(data, offset, slice, 0, length);
            return slice;
        }
    }

    public enum ApngDisposeOp : byte
    {
        None = 0,
        Background = 1,
        Previous = 2
    }

    public enum ApngBlendOp : byte
    {
        Source = 0,
        Over = 1
    }

    /// <summary>Placement and timing of one APNG frame, as read from its fcTL chunk.</summary>
    public sealed class ApngFrameInfo
    {
        internal readonly List<ChunkRange> Chunks = new List<ChunkRange>(1);

        public int Index { get; private set; }
        public int Width { get; private set; }
        public int Height { get; private set; }
        public int X { get; private set; }
        public int Y { get; private set; }

        /// <summary>How long the frame stays on screen, in seconds.</summary>
        public double Delay { get; private set; }

        /// <summary>Offset of the frame from the start of the animation, in seconds.</summary>
        public double StartTime { get; internal set; }

        public ApngDisposeOp DisposeOp { get; internal set; }
        public ApngBlendOp BlendOp { get; private set; }

        /// <summary>
        /// True when the frame replaces the whole canvas, so decoding can start here
        /// without the frames before it.
        /// </summary>
        public bool IsKeyFrame { get; internal set; }

        internal ApngFrameInfo(
            int index,
            int width,
            int height,
            int x,
            int y,
            double delay,
            ApngDisposeOp disposeOp,
            ApngBlendOp blendOp)
        {
            Index = index;
            Width = width;
            Height = height;
            X = x;
            Y = y;
            Delay = delay;
            DisposeOp = disposeOp;
            BlendOp = blendOp;
        }
    }

    /// <summary>Location of one IDAT or fdAT payload inside the source data.</summary>
    internal struct ChunkRange
    {
        public readonly int ChunkOffset;
        public readonly int DataOffset;
        public readonly int Length;

        public ChunkRange(int chunkOffset, int dataOffset, int length)
        {
            ChunkOffset = chunkOffset;
            DataOffset = dataOffset;
            Length = length;
        }
    }
}
