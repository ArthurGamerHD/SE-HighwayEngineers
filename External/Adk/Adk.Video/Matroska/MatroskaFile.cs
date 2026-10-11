// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.Text;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Matroska
{
    public enum MatroskaTrackType
    {
        Unknown = 0,
        Video = 1,
        Audio = 2,
        Subtitle = 17
    }

    /// <summary>
    /// Indexed Matroska / WebM file. Loading walks the EBML tree once and records where
    /// every frame lives; frame payloads stay in the source array until read.
    /// Laced blocks are split into frames. Tracks using ContentEncodings (compression
    /// or encryption) are rejected.
    /// </summary>
    public sealed class MatroskaFile
    {
        const uint EbmlHeaderId = 0x1A45DFA3;
        const uint DocTypeId = 0x4282;
        const uint SegmentId = 0x18538067;
        const uint InfoId = 0x1549A966;
        const uint TimestampScaleId = 0x2AD7B1;
        const uint DurationId = 0x4489;
        const uint TracksId = 0x1654AE6B;
        const uint TrackEntryId = 0xAE;
        const uint TrackNumberId = 0xD7;
        const uint TrackTypeId = 0x83;
        const uint CodecIdId = 0x86;
        const uint CodecPrivateId = 0x63A2;
        const uint DefaultDurationId = 0x23E383;
        const uint ContentEncodingsId = 0x6D80;
        const uint VideoId = 0xE0;
        const uint PixelWidthId = 0xB0;
        const uint PixelHeightId = 0xBA;
        const uint AudioId = 0xE1;
        const uint SamplingFrequencyId = 0xB5;
        const uint ChannelsId = 0x9F;
        const uint BitDepthId = 0x6264;
        const uint ClusterId = 0x1F43B675;
        const uint ClusterTimestampId = 0xE7;
        const uint SimpleBlockId = 0xA3;
        const uint BlockGroupId = 0xA0;
        const uint BlockId = 0xA1;
        const uint BlockDurationId = 0x9B;
        const uint ReferenceBlockId = 0xFB;

        // Level-1 elements: an unknown-size cluster ends where one of these starts.
        static readonly uint[] SegmentChildren =
        {
            0x114D9B74, InfoId, TracksId, ClusterId, 0x1C53BB6B, 0x1043A770, 0x1254C367, 0x1941A469
        };

        readonly byte[] _data;
        readonly List<MatroskaTrack> _tracks = new List<MatroskaTrack>();
        long _timestampScale = 1000000;
        double _durationTicks = -1;

        public string DocType { get; private set; }

        /// <summary>Declared duration in seconds, or the end of the last frame when the file has none.</summary>
        public double Duration { get; private set; }

        public int TrackCount => _tracks.Count;

        internal byte[] Data => _data;

        MatroskaFile(byte[] data)
        {
            _data = data;
        }

        public MatroskaTrack GetTrack(int index)
        {
            if ((uint)index >= (uint)_tracks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _tracks[index];
        }

        /// <summary>First track of <paramref name="type"/> whose codec is <paramref name="codecId"/> (any codec when null), or null.</summary>
        public MatroskaTrack FindTrack(MatroskaTrackType type, string codecId = null)
        {
            for (int i = 0; i < _tracks.Count; i++)
            {
                MatroskaTrack track = _tracks[i];
                if (track.Type == type && (codecId == null || track.CodecId == codecId))
                    return track;
            }

            return null;
        }

        /// <summary>Copies one frame's payload out of the file.</summary>
        public byte[] ReadFrame(MatroskaFrame frame)
        {
            byte[] payload = new byte[frame.Length];
            Buffer.BlockCopy(_data, frame.Offset, payload, 0, frame.Length);
            return payload;
        }

        /// <summary>
        /// Indexes a Matroska or WebM file. The array is kept, not copied, and must not be
        /// changed while the file is in use.
        /// </summary>
        public static MatroskaFile Load(byte[] data)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            var file = new MatroskaFile(data);
            file.Parse();
            return file;
        }

        void Parse()
        {
            int position = 0;
            uint id;
            long size;
            int payload;
            ReadElementHeader(ref position, _data.Length, out id, out size, out payload);
            if (id != EbmlHeaderId || size < 0)
                throw new InvalidDataException("The data is not a Matroska file.");

            int headerEnd = payload + (int)size;
            ReadEbmlHeader(payload, headerEnd);
            position = headerEnd;

            bool sawSegment = false;
            while (position < _data.Length && !sawSegment)
            {
                ReadElementHeader(ref position, _data.Length, out id, out size, out payload);
                int end = size < 0 ? _data.Length : payload + (int)size;
                if (id == SegmentId)
                {
                    ReadSegment(payload, end);
                    sawSegment = true;
                }

                position = end;
            }

            if (!sawSegment)
                throw new InvalidDataException("Matroska file has no Segment.");

            double lastFrameEnd = 0;
            for (int i = 0; i < _tracks.Count; i++)
            {
                MatroskaTrack track = _tracks[i];
                if (track.Frames.Count == 0)
                    continue;

                MatroskaFrame last = track.Frames[track.Frames.Count - 1];
                double frameDuration = last.Duration >= 0 ? last.Duration : Math.Max(0, track.DefaultDuration);
                lastFrameEnd = Math.Max(lastFrameEnd, last.Time + frameDuration);
            }

            Duration = _durationTicks >= 0 ? _durationTicks * _timestampScale / 1e9 : lastFrameEnd;
        }

        void ReadEbmlHeader(int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == DocTypeId)
                    DocType = ReadString(payload, (int)size);

                position = payload + (int)size;
            }

            if (DocType != "matroska" && DocType != "webm")
                throw new NotSupportedException("Unsupported EBML document type: " + DocType + ".");
        }

        void ReadSegment(int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);

                if (id == ClusterId)
                {
                    int clusterEnd = size < 0 ? FindClusterEnd(payload, end) : payload + (int)size;
                    ReadCluster(payload, clusterEnd);
                    position = clusterEnd;
                    continue;
                }

                if (size < 0)
                    throw new NotSupportedException("Unknown-size Matroska element 0x" + id.ToString("X") + ".");

                int elementEnd = payload + (int)size;
                if (id == InfoId)
                    ReadInfo(payload, elementEnd);
                else if (id == TracksId)
                    ReadTracks(payload, elementEnd);

                position = elementEnd;
            }
        }

        void ReadInfo(int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == TimestampScaleId)
                    _timestampScale = (long)ReadUnsigned(payload, (int)size);
                else if (id == DurationId)
                    _durationTicks = ReadFloat(payload, (int)size);

                position = payload + (int)size;
            }
        }

        void ReadTracks(int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == TrackEntryId)
                    _tracks.Add(ReadTrackEntry(payload, payload + (int)size));

                position = payload + (int)size;
            }
        }

        MatroskaTrack ReadTrackEntry(int position, int end)
        {
            var track = new MatroskaTrack();
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                int length = (int)size;
                switch (id)
                {
                    case TrackNumberId:
                        track.Number = (long)ReadUnsigned(payload, length);
                        break;
                    case TrackTypeId:
                        track.Type = (MatroskaTrackType)ReadUnsigned(payload, length);
                        break;
                    case CodecIdId:
                        track.CodecId = ReadString(payload, length);
                        break;
                    case CodecPrivateId:
                        track.CodecPrivate = new byte[length];
                        Buffer.BlockCopy(_data, payload, track.CodecPrivate, 0, length);
                        break;
                    case DefaultDurationId:
                        track.DefaultDuration = ReadUnsigned(payload, length) / 1e9;
                        break;
                    case ContentEncodingsId:
                        track.HasContentEncoding = true;
                        break;
                    case VideoId:
                        ReadVideo(track, payload, payload + length);
                        break;
                    case AudioId:
                        ReadAudio(track, payload, payload + length);
                        break;
                }

                position = payload + length;
            }

            return track;
        }

        void ReadVideo(MatroskaTrack track, int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == PixelWidthId)
                    track.PixelWidth = (int)ReadUnsigned(payload, (int)size);
                else if (id == PixelHeightId)
                    track.PixelHeight = (int)ReadUnsigned(payload, (int)size);

                position = payload + (int)size;
            }
        }

        void ReadAudio(MatroskaTrack track, int position, int end)
        {
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == SamplingFrequencyId)
                    track.SamplingFrequency = ReadFloat(payload, (int)size);
                else if (id == ChannelsId)
                    track.Channels = (int)ReadUnsigned(payload, (int)size);
                else if (id == BitDepthId)
                    track.BitDepth = (int)ReadUnsigned(payload, (int)size);

                position = payload + (int)size;
            }
        }

        void ReadCluster(int position, int end)
        {
            long clusterTimestamp = 0;
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (size < 0)
                    throw new NotSupportedException("Unknown-size element inside a Matroska cluster.");

                int elementEnd = payload + (int)size;
                if (id == ClusterTimestampId)
                    clusterTimestamp = (long)ReadUnsigned(payload, (int)size);
                else if (id == SimpleBlockId)
                    ReadBlock(payload, elementEnd, clusterTimestamp, -1, true, false);
                else if (id == BlockGroupId)
                    ReadBlockGroup(payload, elementEnd, clusterTimestamp);

                position = elementEnd;
            }
        }

        void ReadBlockGroup(int position, int end, long clusterTimestamp)
        {
            int blockStart = -1;
            int blockEnd = -1;
            long duration = -1;
            bool referencesOthers = false;
            while (position < end)
            {
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (id == BlockId)
                {
                    blockStart = payload;
                    blockEnd = payload + (int)size;
                }
                else if (id == BlockDurationId)
                {
                    duration = (long)ReadUnsigned(payload, (int)size);
                }
                else if (id == ReferenceBlockId)
                {
                    referencesOthers = true;
                }

                position = payload + (int)size;
            }

            if (blockStart >= 0)
                ReadBlock(blockStart, blockEnd, clusterTimestamp, duration, false, !referencesOthers);
        }

        void ReadBlock(int position, int end, long clusterTimestamp, long durationTicks, bool simpleBlock, bool groupKeyFrame)
        {
            long trackNumber = ReadVint(ref position, end, false);
            if (position > end - 3)
                throw new InvalidDataException("Matroska block header is truncated.");

            short relative = (short)((_data[position] << 8) | _data[position + 1]);
            byte flags = _data[position + 2];
            position += 3;

            MatroskaTrack track = null;
            for (int i = 0; i < _tracks.Count; i++)
            {
                if (_tracks[i].Number == trackNumber)
                    track = _tracks[i];
            }

            if (track == null)
                return;

            if (track.HasContentEncoding)
                throw new NotSupportedException("Matroska track " + trackNumber + " uses ContentEncodings.");

            double scale = _timestampScale / 1e9;
            double time = (clusterTimestamp + relative) * scale;
            double duration = durationTicks >= 0 ? durationTicks * scale : -1;

            // SimpleBlock carries its own key flag; a BlockGroup is key when it references nothing.
            bool keyFrame = simpleBlock ? (flags & 0x80) != 0 : groupKeyFrame;
            int lacing = (flags >> 1) & 3;

            if (lacing == 0)
            {
                track.Frames.Add(new MatroskaFrame(time, duration, keyFrame, position, end - position));
                return;
            }

            int count = _data[position++] + 1;
            var sizes = new int[count];
            int known = 0;
            if (lacing == 1)
            {
                // Xiph lacing: each size is a run of 255s plus a final byte.
                for (int i = 0; i < count - 1; i++)
                {
                    int value;
                    do
                    {
                        value = _data[position++];
                        sizes[i] += value;
                    }
                    while (value == 255);

                    known += sizes[i];
                }
            }
            else if (lacing == 3)
            {
                // EBML lacing: the first size is a vint, the rest are signed vint deltas.
                sizes[0] = (int)ReadVint(ref position, end, false);
                known = sizes[0];
                for (int i = 1; i < count - 1; i++)
                {
                    int start = position;
                    long raw = ReadVint(ref position, end, false);
                    int length = position - start;
                    long bias = (1L << (7 * length - 1)) - 1;
                    sizes[i] = (int)(sizes[i - 1] + raw - bias);
                    known += sizes[i];
                }
            }
            else
            {
                // Fixed-size lacing.
                int each = (end - position) / count;
                for (int i = 0; i < count - 1; i++)
                    sizes[i] = each;

                known = each * (count - 1);
            }

            sizes[count - 1] = end - position - known;
            if (sizes[count - 1] < 0)
                throw new InvalidDataException("Matroska lace sizes exceed the block.");

            double step = track.DefaultDuration > 0 ? track.DefaultDuration : 0;
            for (int i = 0; i < count; i++)
            {
                track.Frames.Add(new MatroskaFrame(time + i * step, duration >= 0 ? duration / count : -1, keyFrame, position, sizes[i]));
                position += sizes[i];
            }
        }

        int FindClusterEnd(int position, int end)
        {
            while (position < end)
            {
                int start = position;
                uint id;
                long size;
                int payload;
                ReadElementHeader(ref position, end, out id, out size, out payload);
                if (Array.IndexOf(SegmentChildren, id) >= 0)
                    return start;

                if (size < 0)
                    throw new NotSupportedException("Unknown-size element inside a Matroska cluster.");

                position = payload + (int)size;
            }

            return end;
        }

        void ReadElementHeader(ref int position, int end, out uint id, out long size, out int payload)
        {
            id = (uint)ReadVint(ref position, end, true);
            size = ReadVint(ref position, end, false);
            payload = position;
            if (size >= 0 && size > end - payload)
                throw new InvalidDataException("Matroska element 0x" + id.ToString("X") + " runs past its parent.");
        }

        /// <summary>Reads an EBML variable-length integer; -1 is returned for an unknown size.</summary>
        long ReadVint(ref int position, int end, bool keepMarker)
        {
            if (position >= end)
                throw new InvalidDataException("Matroska data ended inside an element header.");

            int first = _data[position];
            int length = 1;
            int marker = 0x80;
            while (length <= 8 && (first & marker) == 0)
            {
                marker >>= 1;
                length++;
            }

            if (length > 8 || position > end - length)
                throw new InvalidDataException("Invalid Matroska variable-length integer.");

            long value = keepMarker ? first : first & (marker - 1);
            bool allOnes = (first & (marker - 1)) == marker - 1;
            for (int i = 1; i < length; i++)
            {
                byte next = _data[position + i];
                value = (value << 8) | next;
                allOnes &= next == 0xFF;
            }

            position += length;
            return !keepMarker && allOnes ? -1 : value;
        }

        ulong ReadUnsigned(int position, int length)
        {
            ulong value = 0;
            for (int i = 0; i < length; i++)
                value = (value << 8) | _data[position + i];

            return value;
        }

        double ReadFloat(int position, int length)
        {
            if (length == 4)
            {
                int bits = (int)ReadUnsigned(position, 4);
                return BitConverter.ToSingle(BitConverter.GetBytes(bits), 0);
            }

            if (length == 8)
                return BitConverter.Int64BitsToDouble((long)ReadUnsigned(position, 8));

            return 0;
        }

        string ReadString(int position, int length)
        {
            // EBML strings may be zero-padded.
            while (length > 0 && _data[position + length - 1] == 0)
                length--;

            return Encoding.UTF8.GetString(_data, position, length);
        }
    }

    public sealed class MatroskaTrack
    {
        internal readonly List<MatroskaFrame> Frames = new List<MatroskaFrame>();
        internal bool HasContentEncoding;

        public long Number { get; internal set; }
        public MatroskaTrackType Type { get; internal set; }
        public string CodecId { get; internal set; }
        public byte[] CodecPrivate { get; internal set; }

        /// <summary>Nominal frame duration in seconds, or 0 when not declared.</summary>
        public double DefaultDuration { get; internal set; }

        public int PixelWidth { get; internal set; }
        public int PixelHeight { get; internal set; }
        public double SamplingFrequency { get; internal set; }
        public int Channels { get; internal set; }
        public int BitDepth { get; internal set; }

        public int FrameCount => Frames.Count;

        /// <summary>Frames in file (decode) order.</summary>
        public MatroskaFrame GetFrame(int index)
        {
            if ((uint)index >= (uint)Frames.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return Frames[index];
        }
    }

    /// <summary>Where one frame's payload lives in the file, and when it plays.</summary>
    public struct MatroskaFrame
    {
        /// <summary>Presentation time in seconds.</summary>
        public readonly double Time;

        /// <summary>Duration in seconds from BlockDuration, or -1 when the block has none.</summary>
        public readonly double Duration;

        public readonly bool IsKeyFrame;
        public readonly int Offset;
        public readonly int Length;

        public MatroskaFrame(double time, double duration, bool isKeyFrame, int offset, int length)
        {
            Time = time;
            Duration = duration;
            IsKeyFrame = isKeyFrame;
            Offset = offset;
            Length = length;
        }
    }
}
