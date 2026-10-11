// ReSharper disable RedundantUsingDirective
using System;
using System.Collections.Generic;
using Adk.Audio.Adpcm;
using Adk.Image.Png;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Apng
{
    /// <summary>
    /// Private ancillary chunks that carry audio and captions inside an APNG. Standard
    /// decoders skip them (lowercase first and second letters), so the blob still plays
    /// as a silent animation anywhere else. All integers are big-endian like PNG's own.
    /// <list type="bullet">
    /// <item><c>vaAh</c> once, before any <c>vaAu</c>: the body of a WAVE <c>fmt </c> chunk
    /// (little-endian WAVEFORMATEX plus its extension). Format tag 1 is 16-bit PCM and
    /// tag 2 is Microsoft ADPCM.</item>
    /// <item><c>vaAu</c>: u32 index of the first sample frame, then whole sample frames
    /// (PCM) or whole blocks (ADPCM). Interleave each one ahead of the frame it plays with.</item>
    /// <item><c>vaCp</c>: u32 start and u32 end in milliseconds, then the cue as UTF-8.</item>
    /// </list>
    /// </summary>
    public static class ApngMediaChunks
    {
        public const string AudioHeader = "vaAh";
        public const string AudioData = "vaAu";
        public const string Caption = "vaCp";
    }

    /// <summary>Audio carried in <c>vaAh</c>/<c>vaAu</c> chunks, decoded to 16-bit little-endian PCM on demand.</summary>
    public sealed class ApngAudioTrack
    {
        public const ushort FormatPcm = 1;
        public const ushort FormatMsAdpcm = 2;

        readonly byte[] _data;
        readonly List<AudioBlock> _blocks = new List<AudioBlock>();
        MsAdpcmFormat _adpcm;

        public ushort FormatTag { get; private set; }
        public int SampleRate { get; private set; }
        public int Channels { get; private set; }

        /// <summary>Total sample frames across every audio chunk.</summary>
        public long SampleCount { get; private set; }

        public double Duration => SampleRate == 0 ? 0 : SampleCount / (double)SampleRate;
        public int BlockCount => _blocks.Count;

        internal ApngAudioTrack(byte[] data)
        {
            _data = data;
        }

        internal void ReadFormat(int offset, int length)
        {
            if (length < 16)
                throw new InvalidDataException("APNG vaAh chunk is too short.");

            FormatTag = ReadUInt16Le(offset);
            Channels = ReadUInt16Le(offset + 2);
            SampleRate = checked((int)ReadUInt32Le(offset + 4));
            ushort blockAlign = ReadUInt16Le(offset + 12);
            ushort bitsPerSample = ReadUInt16Le(offset + 14);

            if (Channels <= 0 || SampleRate <= 0)
                throw new InvalidDataException("APNG audio has no channels or sample rate.");

            if (FormatTag == FormatPcm)
            {
                if (bitsPerSample != 16 || blockAlign != Channels * 2)
                    throw new NotSupportedException("APNG PCM audio must be 16-bit.");

                return;
            }

            if (FormatTag != FormatMsAdpcm)
                throw new NotSupportedException("Unsupported APNG audio format tag " + FormatTag + ".");

            int extensionLength = length >= 18 ? ReadUInt16Le(offset + 16) : 0;
            if (18 + extensionLength > length)
                throw new InvalidDataException("APNG vaAh format extension is truncated.");

            byte[] extension = new byte[extensionLength];
            Buffer.BlockCopy(_data, offset + 18, extension, 0, extensionLength);

            string failure;
            if (!MsAdpcmDecoder.TryReadFormat(
                    (ushort)Channels,
                    (uint)SampleRate,
                    blockAlign,
                    bitsPerSample,
                    extension,
                    out _adpcm,
                    out failure))
            {
                throw new InvalidDataException(failure);
            }
        }

        internal void AddBlock(int chunkOffset, int dataOffset, int length)
        {
            long start = PngDecoder.ReadUInt32BigEndian(_data, dataOffset);
            var block = new AudioBlock(chunkOffset, dataOffset + 4, length - 4, start, CountFrames(length - 4));
            if (_blocks.Count > 0 && start < _blocks[_blocks.Count - 1].StartSample)
                throw new InvalidDataException("APNG audio chunks are out of order.");

            _blocks.Add(block);
            SampleCount = Math.Max(SampleCount, start + block.SampleCount);
        }

        long CountFrames(int byteCount)
        {
            if (FormatTag == FormatPcm)
            {
                if (byteCount % (Channels * 2) != 0)
                    throw new InvalidDataException("APNG PCM chunk does not hold whole sample frames.");

                return byteCount / (Channels * 2);
            }

            if (byteCount % _adpcm.BlockAlign != 0)
                throw new InvalidDataException("APNG ADPCM chunk does not hold whole blocks.");

            return (long)(byteCount / _adpcm.BlockAlign) * _adpcm.SamplesPerBlock;
        }

        public long GetBlockStartSample(int index)
        {
            return GetBlock(index).StartSample;
        }

        public long GetBlockSampleCount(int index)
        {
            return GetBlock(index).SampleCount;
        }

        /// <summary>Index of the chunk holding <paramref name="sample"/>, or the first one after it; -1 past the end.</summary>
        public int FindBlock(long sample)
        {
            int low = 0;
            int high = _blocks.Count - 1;
            int found = -1;
            while (low <= high)
            {
                int middle = (low + high) >> 1;
                AudioBlock block = _blocks[middle];
                if (block.StartSample + block.SampleCount <= sample)
                {
                    low = middle + 1;
                }
                else
                {
                    found = middle;
                    high = middle - 1;
                }
            }

            return found;
        }

        /// <summary>Decodes one audio chunk to interleaved 16-bit little-endian PCM.</summary>
        public byte[] DecodeBlock(int index)
        {
            AudioBlock block = GetBlock(index);
            ApngVideo.VerifyCrc(_data, block.ChunkOffset, block.Length + 4);

            byte[] encoded = new byte[block.Length];
            Buffer.BlockCopy(_data, block.DataOffset, encoded, 0, block.Length);
            if (FormatTag == FormatPcm)
                return encoded;

            byte[] pcm;
            string failure;
            if (!MsAdpcmDecoder.TryDecode(encoded, _adpcm, int.MaxValue, out pcm, out failure))
                throw new InvalidDataException(failure);

            return pcm;
        }

        AudioBlock GetBlock(int index)
        {
            if ((uint)index >= (uint)_blocks.Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return _blocks[index];
        }

        ushort ReadUInt16Le(int offset)
        {
            return (ushort)(_data[offset] | (_data[offset + 1] << 8));
        }

        uint ReadUInt32Le(int offset)
        {
            return (uint)(_data[offset] | (_data[offset + 1] << 8) | (_data[offset + 2] << 16) | (_data[offset + 3] << 24));
        }

        struct AudioBlock
        {
            public readonly int ChunkOffset;
            public readonly int DataOffset;
            public readonly int Length;
            public readonly long StartSample;
            public readonly long SampleCount;

            public AudioBlock(int chunkOffset, int dataOffset, int length, long startSample, long sampleCount)
            {
                ChunkOffset = chunkOffset;
                DataOffset = dataOffset;
                Length = length;
                StartSample = startSample;
                SampleCount = sampleCount;
            }
        }
    }

    /// <summary>One caption cue from a <c>vaCp</c> chunk.</summary>
    public sealed class ApngCaption
    {
        public double Start { get; private set; }
        public double End { get; private set; }
        public string Text { get; private set; }

        internal ApngCaption(double start, double end, string text)
        {
            Start = start;
            End = end;
            Text = text;
        }
    }
}
