// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using System.IO;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Matroska
{
    /// <summary>A run of whole clusters starting at <see cref="Start"/> seconds.</summary>
    public struct MatroskaChunk
    {
        public readonly double Start;
        public readonly long Offset;
        public readonly int Length;

        public MatroskaChunk(double start, long offset, int length)
        {
            Start = start;
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// Splits a Matroska file into chunks of whole clusters, a few seconds each, by reading only element
    /// headers from a seekable stream, so a long file never has to be in memory. <see cref="BuildChunkFile"/>
    /// turns one chunk's bytes into a small Matroska file of its own (the shared header followed by those
    /// clusters) that <see cref="MatroskaFile.Load"/> can play. <see cref="Serialize"/> carries the index
    /// over the network, so a receiver needs only the chunks around its playhead.
    /// </summary>
    public sealed class MatroskaChunkIndex
    {
        public const double DefaultChunkSeconds = 5;

        const uint EbmlHeaderId = 0x1A45DFA3;
        const uint SegmentId = 0x18538067;
        const uint InfoId = 0x1549A966;
        const uint TracksId = 0x1654AE6B;
        const uint ClusterId = 0x1F43B675;
        const uint ClusterTimestampId = 0xE7;
        const uint SimpleBlockId = 0xA3;
        const uint BlockGroupId = 0xA0;
        const uint TimestampScaleId = 0x2AD7B1;
        const uint DurationId = 0x4489;
        const int MaxHeaderBytes = 1024 * 1024;
        const int SerializedVersion = 1;

        static readonly byte[] UnknownSize = { 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF };

        readonly List<MatroskaChunk> _chunks;

        MatroskaChunkIndex(byte[] header, double duration, List<MatroskaChunk> chunks, bool isDamaged = false)
        {
            Header = header;
            Duration = duration;
            _chunks = chunks;
            IsDamaged = isDamaged;
        }

        /// <summary>
        /// True when <see cref="Build"/> met unreadable or cut-off data and indexed only the clusters around it, the
        /// way media players play what they can of a damaged file.
        /// </summary>
        public bool IsDamaged { get; private set; }

        /// <summary>EBML header, an unknown-size Segment, Info and Tracks: the prefix of every chunk file.</summary>
        public byte[] Header { get; private set; }

        public double Duration { get; private set; }
        public int ChunkCount => _chunks.Count;

        public MatroskaChunk GetChunk(int index)
        {
            if ((uint)index >= (uint)_chunks.Count)
                throw new ArgumentException(nameof(index));

            return _chunks[index];
        }

        /// <summary>Seconds where chunk <paramref name="index"/> stops, the next chunk's start or the duration.</summary>
        public double GetChunkEnd(int index)
        {
            return index + 1 < _chunks.Count ? _chunks[index + 1].Start : Math.Max(Duration, GetChunk(index).Start);
        }

        /// <summary>Index of the chunk playing at <paramref name="seconds"/>, clamped to the first and last.</summary>
        public int ChunkAt(double seconds)
        {
            int low = 0;
            int high = _chunks.Count - 1;
            while (low < high)
            {
                int middle = (low + high + 1) >> 1;
                if (_chunks[middle].Start <= seconds)
                    low = middle;
                else
                    high = middle - 1;
            }

            return low;
        }

        public byte[] BuildChunkFile(byte[] clusters)
        {
            if (clusters == null)
                throw new ArgumentNullException(nameof(clusters));

            var file = new byte[Header.Length + clusters.Length];
            Buffer.BlockCopy(Header, 0, file, 0, Header.Length);
            Buffer.BlockCopy(clusters, 0, file, Header.Length, clusters.Length);
            return file;
        }

        /// <summary>
        /// Indexes <paramref name="stream"/>. Clusters are grouped until a chunk spans
        /// <paramref name="chunkSeconds"/>, and a chunk never grows past <paramref name="maxChunkBytes"/>.
        /// </summary>
        public static MatroskaChunkIndex Build(Stream stream, int maxChunkBytes, double chunkSeconds = DefaultChunkSeconds)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            var reader = new HeaderReader(stream);
            long length = stream.Length;
            uint id;
            long size;
            reader.Seek(0);
            if (!reader.TryReadElement(length, out id, out size) || id != EbmlHeaderId || size < 0)
                throw new InvalidDataException("The data is not a Matroska file.");

            long ebmlEnd = reader.Position + size;
            byte[] ebml = reader.ReadRange(0, (int)ebmlEnd);
            reader.Seek(ebmlEnd);

            long segmentEnd = -1;
            while (reader.Position < length)
            {
                if (!reader.TryReadElement(length, out id, out size))
                    break;

                if (id == SegmentId)
                {
                    segmentEnd = size < 0 ? length : Math.Min(length, reader.Position + size);
                    break;
                }

                if (size < 0)
                    throw new InvalidDataException("Unknown-size element before the Matroska Segment.");

                reader.Seek(reader.Position + size);
            }

            if (segmentEnd < 0)
                throw new InvalidDataException("Matroska file has no Segment.");

            byte[] info = null;
            byte[] tracks = null;
            long timestampScale = 1000000;
            double durationTicks = -1;
            var clusters = new List<KeyValuePair<long, int>>();
            var clusterTimes = new List<double>();
            bool damaged = false;
            while (reader.Position < segmentEnd)
            {
                long start = reader.Position;
                bool read;
                try
                {
                    read = reader.TryReadElement(segmentEnd, out id, out size);
                }
                catch (Exception)
                {
                    if (clusters.Count == 0)
                        throw;
                    damaged = true;
                    if (Resync(reader, start + 1, segmentEnd, maxChunkBytes, clusterTimes))
                        continue;
                    break;
                }

                if (!read)
                    break;

                if (size < 0)
                {
                    if (clusters.Count == 0)
                        throw new NotSupportedException("Unknown-size Matroska elements are not supported here; remux the file with ffmpeg.");
                    damaged = true;
                    if (Resync(reader, start + 1, segmentEnd, maxChunkBytes, clusterTimes))
                        continue;
                    break;
                }

                long payload = reader.Position;
                long end = payload + size;
                if (end > segmentEnd)
                {
                    if (clusters.Count == 0 || id == InfoId || id == TracksId)
                        throw new InvalidDataException("A Matroska element runs past the end of the file.");

                    // A cut-off tail, as left by an interrupted download or a recording still being written.
                    damaged = true;
                    if (Resync(reader, start + 1, segmentEnd, maxChunkBytes, clusterTimes))
                        continue;
                    break;
                }

                if (id == InfoId || id == TracksId)
                {
                    if (end - start > MaxHeaderBytes)
                        throw new InvalidDataException("The Matroska header is too large.");

                    byte[] element = reader.ReadRange(start, (int)(end - start));
                    if (id == InfoId)
                    {
                        info = element;
                        ReadInfo(element, (int)(payload - start), out timestampScale, out durationTicks);
                    }
                    else
                    {
                        tracks = element;
                    }
                }
                else if (id == ClusterId)
                {
                    if (end - start > maxChunkBytes)
                        throw new InvalidDataException("A cluster is " + (end - start) / 1024 + " KiB, above the " +
                                                       maxChunkBytes / 1024 + " KiB chunk limit.");

                    long clusterTimestamp;
                    if (!TryReadClusterTimestamp(reader, payload, end, out clusterTimestamp) ||
                        (clusterTimes.Count > 0 && clusterTimestamp < clusterTimes[clusterTimes.Count - 1]))
                    {
                        if (clusters.Count == 0)
                            throw new InvalidDataException("A Matroska cluster has no timestamp.");
                        damaged = true;
                        if (Resync(reader, start + 1, segmentEnd, maxChunkBytes, clusterTimes))
                            continue;
                        break;
                    }

                    clusters.Add(new KeyValuePair<long, int>(start, (int)(end - start)));
                    clusterTimes.Add(clusterTimestamp);
                }

                reader.Seek(end);
            }

            if (info == null || tracks == null)
                throw new InvalidDataException("The Matroska file has no Info or Tracks.");

            if (clusters.Count == 0)
                throw new InvalidDataException("The Matroska file has no clusters.");

            var chunks = new List<MatroskaChunk>();
            double scale = timestampScale / 1e9;
            int first = 0;
            for (int i = 1; i <= clusters.Count; i++)
            {
                long chunkOffset = clusters[first].Key;
                bool last = i == clusters.Count;
                long nextBytes = last ? 0 : clusters[i].Key + clusters[i].Value - chunkOffset;
                bool longEnough = !last && (clusterTimes[i] - clusterTimes[first]) * scale >= chunkSeconds;
                if (last || longEnough || nextBytes > maxChunkBytes)
                {
                    long chunkEnd = clusters[i - 1].Key + clusters[i - 1].Value;
                    chunks.Add(new MatroskaChunk(clusterTimes[first] * scale, chunkOffset, (int)(chunkEnd - chunkOffset)));
                    first = i;
                }
            }

            double duration = durationTicks >= 0 ? durationTicks * scale : 0;
            if (damaged || durationTicks < 0)
            {
                // The declared duration covers data that is gone, or there is none; end one average cluster after the
                // last readable one.
                double lastStart = clusterTimes[clusterTimes.Count - 1] * scale;
                double average = clusters.Count > 1
                    ? (clusterTimes[clusterTimes.Count - 1] - clusterTimes[0]) * scale / (clusters.Count - 1)
                    : chunkSeconds;
                double estimate = lastStart + average;
                duration = durationTicks >= 0 ? Math.Min(duration, estimate) : estimate;
            }

            return new MatroskaChunkIndex(BuildHeader(ebml, info, tracks), duration, chunks, damaged);
        }

        /// <summary>
        /// Skips damaged data by searching from <paramref name="from"/> for the next cluster that reads cleanly and
        /// continues the timeline; leaves the reader on it and returns true when one is found.
        /// </summary>
        static bool Resync(HeaderReader reader, long from, long segmentEnd, int maxChunkBytes, List<double> clusterTimes)
        {
            double lastTime = clusterTimes.Count > 0 ? clusterTimes[clusterTimes.Count - 1] : 0;
            long candidate = from;
            while ((candidate = reader.Find(ClusterId, candidate, segmentEnd)) >= 0)
            {
                try
                {
                    reader.Seek(candidate);
                    uint id;
                    long size;
                    long timestamp;
                    if (reader.TryReadElement(segmentEnd, out id, out size) && id == ClusterId && size >= 0 &&
                        reader.Position + size <= segmentEnd && reader.Position + size - candidate <= maxChunkBytes &&
                        TryReadClusterTimestamp(reader, reader.Position, reader.Position + size, out timestamp) &&
                        timestamp >= lastTime)
                    {
                        reader.Seek(candidate);
                        return true;
                    }
                }
                catch (Exception)
                {
                }

                candidate++;
            }

            return false;
        }

        /// <summary>The Timestamp child of a cluster; muxers may write a CRC-32 or Void element ahead of it.</summary>
        static bool TryReadClusterTimestamp(HeaderReader reader, long payload, long end, out long timestamp)
        {
            timestamp = 0;
            try
            {
                reader.Seek(payload);
                uint id;
                long size;
                while (reader.TryReadElement(end, out id, out size) && size >= 0)
                {
                    if (id == ClusterTimestampId)
                    {
                        if (size <= 0 || size > 8)
                            return false;

                        timestamp = (long)reader.ReadUnsigned((int)size);
                        return true;
                    }

                    if (id == SimpleBlockId || id == BlockGroupId)
                        return false;

                    reader.Seek(reader.Position + size);
                }
            }
            catch (Exception)
            {
            }

            return false;
        }

        public byte[] Serialize()
        {
            var data = new byte[12 + 4 + Header.Length + 4 + _chunks.Count * 20];
            int position = 0;
            WriteInt(data, ref position, SerializedVersion);
            WriteLong(data, ref position, BitConverter.DoubleToInt64Bits(Duration));
            WriteInt(data, ref position, Header.Length);
            Buffer.BlockCopy(Header, 0, data, position, Header.Length);
            position += Header.Length;
            WriteInt(data, ref position, _chunks.Count);
            for (int i = 0; i < _chunks.Count; i++)
            {
                WriteLong(data, ref position, BitConverter.DoubleToInt64Bits(_chunks[i].Start));
                WriteLong(data, ref position, _chunks[i].Offset);
                WriteInt(data, ref position, _chunks[i].Length);
            }

            return data;
        }

        public static MatroskaChunkIndex Deserialize(byte[] data, int maxChunkBytes)
        {
            if (data == null)
                throw new ArgumentNullException(nameof(data));

            int position = 0;
            if (ReadInt(data, ref position) != SerializedVersion)
                throw new InvalidDataException("Unknown chunk index version.");

            double duration = BitConverter.Int64BitsToDouble(ReadLong(data, ref position));
            int headerLength = ReadInt(data, ref position);
            if (headerLength <= 0 || headerLength > MaxHeaderBytes || position + headerLength > data.Length)
                throw new InvalidDataException("The chunk index header is malformed.");

            var header = new byte[headerLength];
            Buffer.BlockCopy(data, position, header, 0, headerLength);
            position += headerLength;
            int count = ReadInt(data, ref position);
            if (count <= 0 || count > (data.Length - position) / 20)
                throw new InvalidDataException("The chunk index is malformed.");

            var chunks = new List<MatroskaChunk>(count);
            for (int i = 0; i < count; i++)
            {
                double start = BitConverter.Int64BitsToDouble(ReadLong(data, ref position));
                long offset = ReadLong(data, ref position);
                int length = ReadInt(data, ref position);
                if (double.IsNaN(start) || length <= 0 || length > maxChunkBytes || (i > 0 && start < chunks[i - 1].Start))
                    throw new InvalidDataException("The chunk index is malformed.");

                chunks.Add(new MatroskaChunk(start, offset, length));
            }

            if (double.IsNaN(duration) || double.IsInfinity(duration))
                duration = 0;

            return new MatroskaChunkIndex(header, duration, chunks);
        }

        static byte[] BuildHeader(byte[] ebml, byte[] info, byte[] tracks)
        {
            var header = new byte[ebml.Length + 4 + UnknownSize.Length + info.Length + tracks.Length];
            int position = 0;
            Buffer.BlockCopy(ebml, 0, header, position, ebml.Length);
            position += ebml.Length;
            header[position++] = 0x18;
            header[position++] = 0x53;
            header[position++] = 0x80;
            header[position++] = 0x67;
            Buffer.BlockCopy(UnknownSize, 0, header, position, UnknownSize.Length);
            position += UnknownSize.Length;
            Buffer.BlockCopy(info, 0, header, position, info.Length);
            position += info.Length;
            Buffer.BlockCopy(tracks, 0, header, position, tracks.Length);
            return header;
        }

        static void ReadInfo(byte[] element, int position, out long timestampScale, out double durationTicks)
        {
            timestampScale = 1000000;
            durationTicks = -1;
            while (position < element.Length)
            {
                uint id;
                long size;
                if (!HeaderReader.TryReadElement(element, ref position, out id, out size) || size < 0 || position + size > element.Length)
                    return;

                if (id == TimestampScaleId && size <= 8)
                    timestampScale = (long)ReadUnsigned(element, position, (int)size);
                else if (id == DurationId && size == 4)
                    durationTicks = BitConverter.ToSingle(Reverse(element, position, 4), 0);
                else if (id == DurationId && size == 8)
                    durationTicks = BitConverter.ToDouble(Reverse(element, position, 8), 0);

                position += (int)size;
            }
        }

        static byte[] Reverse(byte[] data, int offset, int count)
        {
            var bytes = new byte[count];
            for (int i = 0; i < count; i++)
                bytes[i] = data[offset + count - 1 - i];

            if (!BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return bytes;
        }

        static ulong ReadUnsigned(byte[] data, int offset, int count)
        {
            ulong value = 0;
            for (int i = 0; i < count; i++)
                value = (value << 8) | data[offset + i];

            return value;
        }

        static void WriteInt(byte[] data, ref int position, int value)
        {
            for (int i = 0; i < 4; i++)
                data[position++] = (byte)(value >> (8 * i));
        }

        static void WriteLong(byte[] data, ref int position, long value)
        {
            for (int i = 0; i < 8; i++)
                data[position++] = (byte)(value >> (8 * i));
        }

        static int ReadInt(byte[] data, ref int position)
        {
            if (position + 4 > data.Length)
                throw new InvalidDataException("The chunk index is truncated.");

            int value = 0;
            for (int i = 0; i < 4; i++)
                value |= data[position++] << (8 * i);

            return value;
        }

        static long ReadLong(byte[] data, ref int position)
        {
            if (position + 8 > data.Length)
                throw new InvalidDataException("The chunk index is truncated.");

            long value = 0;
            for (int i = 0; i < 8; i++)
                value |= (long)data[position++] << (8 * i);

            return value;
        }

        sealed class HeaderReader
        {
            readonly Stream _stream;
            readonly byte[] _buffer = new byte[8];

            public HeaderReader(Stream stream)
            {
                _stream = stream;
            }

            public long Position => _stream.Position;

            public void Seek(long position)
            {
                _stream.Position = position;
            }

            /// <summary>Position of the first 4-byte <paramref name="id"/> in [from, end), or -1.</summary>
            public long Find(uint id, long from, long end)
            {
                const int BufferBytes = 64 * 1024;
                var buffer = new byte[BufferBytes + 3];
                long position = from;
                while (position + 4 <= end)
                {
                    _stream.Position = position;
                    int wanted = (int)Math.Min(buffer.Length, end - position);
                    int read = 0;
                    while (read < wanted)
                    {
                        int got = _stream.Read(buffer, read, wanted - read);
                        if (got <= 0)
                            break;
                        read += got;
                    }

                    for (int i = 0; i + 4 <= read; i++)
                    {
                        if (buffer[i] == (byte)(id >> 24) && buffer[i + 1] == (byte)(id >> 16) &&
                            buffer[i + 2] == (byte)(id >> 8) && buffer[i + 3] == (byte)id)
                            return position + i;
                    }

                    if (read < wanted || read < 4)
                        return -1;
                    position += read - 3;
                }

                return -1;
            }

            public byte[] ReadRange(long offset, int count)
            {
                _stream.Position = offset;
                var data = new byte[count];
                int read = 0;
                while (read < count)
                {
                    int got = _stream.Read(data, read, count - read);
                    if (got <= 0)
                        throw new InvalidDataException("Unexpected end of the Matroska file.");
                    read += got;
                }

                return data;
            }

            public ulong ReadUnsigned(int count)
            {
                ulong value = 0;
                for (int i = 0; i < count; i++)
                    value = (value << 8) | (byte)ReadByte();

                return value;
            }

            /// <summary>Reads an element id and size; a size of -1 means unknown.</summary>
            public bool TryReadElement(long end, out uint id, out long size)
            {
                id = 0;
                size = 0;
                if (_stream.Position >= end)
                    return false;

                int first = ReadByte();
                int idLength = VintLength(first);
                if (idLength == 0 || idLength > 4)
                    throw new InvalidDataException("Invalid Matroska element id.");

                id = (uint)first;
                for (int i = 1; i < idLength; i++)
                    id = (id << 8) | (uint)ReadByte();

                int sizeFirst = ReadByte();
                int sizeLength = VintLength(sizeFirst);
                if (sizeLength == 0)
                    throw new InvalidDataException("Invalid Matroska element size.");

                long value = sizeFirst & (0xFF >> sizeLength);
                bool allOnes = value == (0xFF >> sizeLength);
                for (int i = 1; i < sizeLength; i++)
                {
                    int b = ReadByte();
                    allOnes &= b == 0xFF;
                    value = (value << 8) | (uint)b;
                }

                size = allOnes ? -1 : value;
                return true;
            }

            public static bool TryReadElement(byte[] data, ref int position, out uint id, out long size)
            {
                id = 0;
                size = 0;
                if (position >= data.Length)
                    return false;

                int idLength = VintLength(data[position]);
                if (idLength == 0 || idLength > 4 || position + idLength >= data.Length)
                    return false;

                for (int i = 0; i < idLength; i++)
                    id = (id << 8) | data[position++];

                int sizeLength = VintLength(data[position]);
                if (sizeLength == 0 || position + sizeLength > data.Length)
                    return false;

                long value = data[position++] & (0xFF >> sizeLength);
                bool allOnes = value == (0xFF >> sizeLength);
                for (int i = 1; i < sizeLength; i++)
                {
                    allOnes &= data[position] == 0xFF;
                    value = (value << 8) | data[position++];
                }

                size = allOnes ? -1 : value;
                return true;
            }

            int ReadByte()
            {
                if (_stream.Read(_buffer, 0, 1) != 1)
                    throw new InvalidDataException("Unexpected end of the Matroska file.");

                return _buffer[0];
            }

            static int VintLength(int first)
            {
                for (int i = 0; i < 8; i++)
                {
                    if ((first & (0x80 >> i)) != 0)
                        return i + 1;
                }

                return 0;
            }
        }
    }
}
