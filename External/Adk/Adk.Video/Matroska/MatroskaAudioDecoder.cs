// ReSharper disable RedundantUsingDirective
using System;
using Adk.Audio.Adpcm;
using InvalidDataException = Adk.Compression.Exceptions.InvalidDataException;

namespace Adk.Video.Matroska
{
    /// <summary>Decoded audio track: interleaved 16-bit little-endian PCM.</summary>
    public sealed class MatroskaPcmAudio
    {
        public byte[] Samples { get; internal set; }
        public int SampleRate { get; internal set; }
        public int Channels { get; internal set; }

        /// <summary>Presentation time of the first sample, in seconds.</summary>
        public double StartTime { get; internal set; }

        public double Duration => SampleRate == 0 || Channels == 0 ? 0 : Samples.Length / (2.0 * Channels * SampleRate);
    }

    /// <summary>
    /// Decodes a Matroska audio track holding 16-bit PCM (<c>A_PCM/INT/LIT</c>, or <c>A_MS/ACM</c> with
    /// format tag 1) or Microsoft ADPCM (<c>A_MS/ACM</c> with format tag 2).
    /// </summary>
    public static class MatroskaAudioDecoder
    {
        const ushort FormatPcm = 1;
        const ushort FormatMsAdpcm = 2;

        /// <summary>Why the track cannot be decoded, or null when it can.</summary>
        public static string GetProblem(MatroskaTrack track)
        {
            if (track == null)
                return "Missing audio track.";

            if (track.HasContentEncoding)
                return "Compressed or encrypted Matroska tracks are not supported.";

            if (track.CodecId == MediaProbe.PcmCodecId)
            {
                return track.BitDepth == 16 && track.Channels > 0 && track.SamplingFrequency > 0
                    ? null
                    : "Only 16-bit PCM audio is supported.";
            }

            if (track.CodecId == MediaProbe.MsAcmCodecId)
            {
                ushort tag = FormatTag(track);
                return tag == FormatPcm || tag == FormatMsAdpcm
                    ? null
                    : "Audio format tag " + tag + " is not supported; use MS-ADPCM or PCM.";
            }

            return "Audio codec " + MediaProbe.CodecName(track.CodecId) + " is not supported; use MS-ADPCM or PCM.";
        }

        public static string GetCodecName(MatroskaTrack track)
        {
            if (track == null)
                return null;

            if (track.CodecId == MediaProbe.MsAcmCodecId)
            {
                ushort tag = FormatTag(track);
                return tag == FormatMsAdpcm ? "MS-ADPCM" : tag == FormatPcm ? "PCM" : "ACM " + tag;
            }

            return MediaProbe.CodecName(track.CodecId);
        }

        public static MatroskaPcmAudio Decode(MatroskaFile file, MatroskaTrack track)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));

            string problem = GetProblem(track);
            if (problem != null)
                throw new NotSupportedException(problem);

            int total = 0;
            for (int i = 0; i < track.FrameCount; i++)
                total = checked(total + track.GetFrame(i).Length);

            byte[] encoded = new byte[total];
            int offset = 0;
            for (int i = 0; i < track.FrameCount; i++)
            {
                MatroskaFrame frame = track.GetFrame(i);
                Buffer.BlockCopy(file.Data, frame.Offset, encoded, offset, frame.Length);
                offset += frame.Length;
            }

            var audio = new MatroskaPcmAudio
            {
                StartTime = track.FrameCount > 0 ? track.GetFrame(0).Time : 0
            };

            if (track.CodecId == MediaProbe.PcmCodecId)
            {
                audio.SampleRate = (int)Math.Round(track.SamplingFrequency);
                audio.Channels = track.Channels;
                audio.Samples = TrimToFrames(encoded, audio.Channels * 2);
                return audio;
            }

            byte[] format = track.CodecPrivate;
            ushort channels = ReadUInt16(format, 2);
            uint sampleRate = ReadUInt32(format, 4);
            ushort blockAlign = ReadUInt16(format, 12);
            ushort bitsPerSample = ReadUInt16(format, 14);
            audio.SampleRate = checked((int)sampleRate);
            audio.Channels = channels;
            if (channels == 0 || sampleRate == 0)
                throw new InvalidDataException("The audio track has no channels or sample rate.");

            if (ReadUInt16(format, 0) == FormatPcm)
            {
                if (bitsPerSample != 16)
                    throw new NotSupportedException("Only 16-bit PCM audio is supported.");

                audio.Samples = TrimToFrames(encoded, channels * 2);
                return audio;
            }

            int extensionLength = format.Length >= 18 ? ReadUInt16(format, 16) : 0;
            if (18 + extensionLength > format.Length)
                throw new InvalidDataException("The audio format extension is truncated.");

            byte[] extension = new byte[extensionLength];
            Buffer.BlockCopy(format, 18, extension, 0, extensionLength);

            MsAdpcmFormat adpcm;
            string failure;
            if (!MsAdpcmDecoder.TryReadFormat(channels, sampleRate, blockAlign, bitsPerSample, extension, out adpcm, out failure))
                throw new InvalidDataException(failure);

            byte[] pcm;
            if (!MsAdpcmDecoder.TryDecode(TrimToFrames(encoded, blockAlign), adpcm, int.MaxValue, out pcm, out failure))
                throw new InvalidDataException(failure);

            audio.Samples = pcm;
            return audio;
        }

        static ushort FormatTag(MatroskaTrack track)
        {
            return track.CodecPrivate != null && track.CodecPrivate.Length >= 16 ? ReadUInt16(track.CodecPrivate, 0) : (ushort)0;
        }

        static byte[] TrimToFrames(byte[] data, int frameBytes)
        {
            int length = data.Length - data.Length % Math.Max(1, frameBytes);
            if (length == data.Length)
                return data;

            byte[] trimmed = new byte[length];
            Buffer.BlockCopy(data, 0, trimmed, 0, length);
            return trimmed;
        }

        static ushort ReadUInt16(byte[] data, int offset)
        {
            if (data == null || offset + 2 > data.Length)
                throw new InvalidDataException("The audio format header is truncated.");

            return (ushort)(data[offset] | (data[offset + 1] << 8));
        }

        static uint ReadUInt32(byte[] data, int offset)
        {
            if (data == null || offset + 4 > data.Length)
                throw new InvalidDataException("The audio format header is truncated.");

            return (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24));
        }
    }
}
