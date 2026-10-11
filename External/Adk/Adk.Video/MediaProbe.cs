// ReSharper disable RedundantUsingDirective
using System;
using System.Text;
using Adk.Video.Matroska;

namespace Adk.Video
{
    public enum MediaContainer
    {
        Unknown,
        Matroska,
        WebM,
        Mp4,
        QuickTime,
        Avi,
        Ogg,
        Flv,
        MpegProgramStream,
        MpegTransportStream,
        Asf,
        Png,
        Apng,
        Gif
    }

    /// <summary>What a media file holds and whether the ADK decoders can play it.</summary>
    public sealed class MediaProbeResult
    {
        public MediaContainer Container { get; internal set; }

        /// <summary>Readable codec names, or null when the container was not inspected that deep.</summary>
        public string VideoCodec { get; internal set; }
        public string AudioCodec { get; internal set; }

        public int Width { get; internal set; }
        public int Height { get; internal set; }

        /// <summary>Seconds, or 0 when unknown.</summary>
        public double Duration { get; internal set; }

        public bool HasSubtitles { get; internal set; }

        /// <summary>True when every track that would play is supported.</summary>
        public bool IsSupported { get; internal set; }

        /// <summary>Why the file cannot play, or null when it can.</summary>
        public string Problem { get; internal set; }
    }

    /// <summary>
    /// Identifies media files by their content. Only Matroska/WebM with MPEG-1 video, PCM or
    /// MS-ADPCM audio and UTF-8 subtitles, and PNG/APNG, are playable; every other known
    /// container is recognised so callers can say why it is not.
    /// </summary>
    public static class MediaProbe
    {
        public const string Mpeg1CodecId = "V_MPEG1";
        public const string MsAcmCodecId = "A_MS/ACM";
        public const string PcmCodecId = "A_PCM/INT/LIT";
        public const string TextSubtitleCodecId = "S_TEXT/UTF8";

        /// <summary>File extensions of the containers <see cref="Identify"/> knows, lower case with the dot.</summary>
        public static readonly string[] VideoExtensions =
        {
            ".mkv", ".webm", ".mp4", ".m4v", ".mov", ".avi", ".ogv", ".ogg", ".flv",
            ".mpg", ".mpeg", ".m1v", ".ts", ".m2ts", ".wmv", ".asf"
        };

        public static bool IsVideoExtension(string fileName)
        {
            if (string.IsNullOrEmpty(fileName))
                return false;

            for (int i = 0; i < VideoExtensions.Length; i++)
            {
                if (fileName.EndsWith(VideoExtensions[i], StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>Recognises the container from the first bytes of a file.</summary>
        public static MediaContainer Identify(byte[] data)
        {
            if (data == null || data.Length < 12)
                return MediaContainer.Unknown;

            if (Matches(data, 0, 0x1A, 0x45, 0xDF, 0xA3))
                return IndexOf(data, Encoding.ASCII.GetBytes("webm"), 64) >= 0 ? MediaContainer.WebM : MediaContainer.Matroska;

            if (Matches(data, 4, (byte)'f', (byte)'t', (byte)'y', (byte)'p'))
                return Matches(data, 8, (byte)'q', (byte)'t') ? MediaContainer.QuickTime : MediaContainer.Mp4;

            if (Matches(data, 4, (byte)'m', (byte)'o', (byte)'o', (byte)'v') ||
                Matches(data, 4, (byte)'m', (byte)'d', (byte)'a', (byte)'t') ||
                Matches(data, 4, (byte)'w', (byte)'i', (byte)'d', (byte)'e'))
            {
                return MediaContainer.QuickTime;
            }

            if (Matches(data, 0, (byte)'R', (byte)'I', (byte)'F', (byte)'F') &&
                Matches(data, 8, (byte)'A', (byte)'V', (byte)'I', (byte)' '))
            {
                return MediaContainer.Avi;
            }

            if (Matches(data, 0, (byte)'O', (byte)'g', (byte)'g', (byte)'S'))
                return MediaContainer.Ogg;

            if (Matches(data, 0, (byte)'F', (byte)'L', (byte)'V'))
                return MediaContainer.Flv;

            if (Matches(data, 0, 0x00, 0x00, 0x01, 0xBA) || Matches(data, 0, 0x00, 0x00, 0x01, 0xB3))
                return MediaContainer.MpegProgramStream;

            if (data[0] == 0x47 && (data.Length < 189 || data[188] == 0x47))
                return MediaContainer.MpegTransportStream;

            if (Matches(data, 0, 0x30, 0x26, 0xB2, 0x75))
                return MediaContainer.Asf;

            if (Matches(data, 0, (byte)'G', (byte)'I', (byte)'F', (byte)'8'))
                return MediaContainer.Gif;

            if (Matches(data, 0, 0x89, (byte)'P', (byte)'N', (byte)'G'))
                return IsAnimatedPng(data) ? MediaContainer.Apng : MediaContainer.Png;

            return MediaContainer.Unknown;
        }

        /// <summary>True when a PNG declares an animation (an <c>acTL</c> chunk before the image data).</summary>
        public static bool IsAnimatedPng(byte[] data)
        {
            if (data == null || data.Length < 16 || !Matches(data, 0, 0x89, (byte)'P', (byte)'N', (byte)'G'))
                return false;

            int position = 8;
            while (position + 8 <= data.Length)
            {
                long length = ((long)data[position] << 24) | ((long)data[position + 1] << 16) |
                              ((long)data[position + 2] << 8) | data[position + 3];
                if (Matches(data, position + 4, (byte)'a', (byte)'c', (byte)'T', (byte)'L'))
                    return true;

                if (Matches(data, position + 4, (byte)'I', (byte)'D', (byte)'A', (byte)'T') ||
                    Matches(data, position + 4, (byte)'I', (byte)'E', (byte)'N', (byte)'D'))
                {
                    return false;
                }

                long next = position + 12 + length;
                if (next > data.Length)
                    return false;

                position = (int)next;
            }

            return false;
        }

        /// <summary>Inspects a whole video file. Matroska and WebM are indexed to read their tracks.</summary>
        public static MediaProbeResult Probe(byte[] data)
        {
            var result = new MediaProbeResult { Container = Identify(data) };
            switch (result.Container)
            {
                case MediaContainer.Matroska:
                case MediaContainer.WebM:
                    ProbeMatroska(data, result);
                    break;

                case MediaContainer.Unknown:
                    result.Problem = "Not a recognised video file.";
                    break;

                case MediaContainer.MpegProgramStream:
                    result.VideoCodec = "MPEG-1/2";
                    result.Problem = "MPEG program streams (.mpg) are not supported. Remux MPEG-1 video into .mkv.";
                    break;

                case MediaContainer.Png:
                case MediaContainer.Apng:
                    result.Problem = "PNG and APNG images belong in the image library.";
                    break;

                default:
                    result.Problem = ContainerName(result.Container) +
                                     " files are not supported. Convert the video to MPEG-1 in .mkv.";
                    break;
            }

            return result;
        }

        /// <summary>A result for a file the caller rejected without probing it, e.g. for its size.</summary>
        public static MediaProbeResult Rejected(MediaContainer container, string problem)
        {
            return new MediaProbeResult { Container = container, Problem = problem };
        }

        public static string ContainerName(MediaContainer container)
        {
            switch (container)
            {
                case MediaContainer.Matroska: return "Matroska";
                case MediaContainer.WebM: return "WebM";
                case MediaContainer.Mp4: return "MP4";
                case MediaContainer.QuickTime: return "QuickTime";
                case MediaContainer.Avi: return "AVI";
                case MediaContainer.Ogg: return "Ogg";
                case MediaContainer.Flv: return "FLV";
                case MediaContainer.MpegProgramStream: return "MPEG program stream";
                case MediaContainer.MpegTransportStream: return "MPEG transport stream";
                case MediaContainer.Asf: return "WMV/ASF";
                case MediaContainer.Png: return "PNG";
                case MediaContainer.Apng: return "APNG";
                case MediaContainer.Gif: return "GIF";
                default: return "Unknown";
            }
        }

        /// <summary>Readable name for a Matroska codec id, such as "H.264" for <c>V_MPEG4/ISO/AVC</c>.</summary>
        public static string CodecName(string codecId)
        {
            if (string.IsNullOrEmpty(codecId))
                return "unknown";

            switch (codecId)
            {
                case Mpeg1CodecId: return "MPEG-1";
                case "V_MPEG2": return "MPEG-2";
                case "V_MPEG4/ISO/AVC": return "H.264";
                case "V_MPEGH/ISO/HEVC": return "H.265";
                case "V_MPEG4/ISO/SP":
                case "V_MPEG4/ISO/ASP":
                case "V_MPEG4/ISO/AP": return "MPEG-4 Part 2";
                case "V_VP8": return "VP8";
                case "V_VP9": return "VP9";
                case "V_AV1": return "AV1";
                case "V_THEORA": return "Theora";
                case "V_MS/VFW/FOURCC": return "VfW";
                case MsAcmCodecId: return "ACM";
                case PcmCodecId: return "PCM";
                case "A_PCM/INT/BIG": return "PCM (big-endian)";
                case "A_PCM/FLOAT/IEEE": return "PCM (float)";
                case "A_AAC": return "AAC";
                case "A_OPUS": return "Opus";
                case "A_VORBIS": return "Vorbis";
                case "A_MPEG/L3": return "MP3";
                case "A_MPEG/L2": return "MP2";
                case "A_AC3": return "AC-3";
                case "A_EAC3": return "E-AC-3";
                case "A_DTS": return "DTS";
                case "A_FLAC": return "FLAC";
                case TextSubtitleCodecId: return "SRT";
                default:
                    return codecId.StartsWith("A_AAC", StringComparison.Ordinal) ? "AAC" : codecId;
            }
        }

        static void ProbeMatroska(byte[] data, MediaProbeResult result)
        {
            MatroskaFile file;
            try
            {
                file = MatroskaFile.Load(data);
            }
            catch (Exception e)
            {
                result.Problem = "The " + ContainerName(result.Container) + " file could not be read: " + e.Message;
                return;
            }

            result.Duration = file.Duration;
            MatroskaTrack video = file.FindTrack(MatroskaTrackType.Video);
            MatroskaTrack audio = file.FindTrack(MatroskaTrackType.Audio);
            result.HasSubtitles = file.FindTrack(MatroskaTrackType.Subtitle, TextSubtitleCodecId) != null;

            if (video == null)
            {
                result.Problem = "The file has no video track.";
                return;
            }

            result.VideoCodec = CodecName(video.CodecId);
            result.Width = video.PixelWidth;
            result.Height = video.PixelHeight;
            if (video.CodecId != Mpeg1CodecId)
            {
                result.Problem = "Video codec " + result.VideoCodec + " is not supported; re-encode it as MPEG-1.";
                return;
            }

            if (video.HasContentEncoding)
            {
                result.Problem = "Compressed or encrypted Matroska tracks are not supported.";
                return;
            }

            if (audio != null)
            {
                string audioProblem = MatroskaAudioDecoder.GetProblem(audio);
                result.AudioCodec = MatroskaAudioDecoder.GetCodecName(audio);
                if (audioProblem != null)
                {
                    result.Problem = audioProblem;
                    return;
                }
            }

            result.IsSupported = true;
        }

        static bool Matches(byte[] data, int offset, params byte[] expected)
        {
            if (offset + expected.Length > data.Length)
                return false;

            for (int i = 0; i < expected.Length; i++)
            {
                if (data[offset + i] != expected[i])
                    return false;
            }

            return true;
        }

        static int IndexOf(byte[] data, byte[] pattern, int limit)
        {
            int end = Math.Min(data.Length, limit) - pattern.Length;
            for (int i = 0; i <= end; i++)
            {
                if (Matches(data, i, pattern))
                    return i;
            }

            return -1;
        }
    }
}
