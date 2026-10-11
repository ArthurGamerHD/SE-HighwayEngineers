// ReSharper disable RedundantUsingDirective
using System;
using Adk.Video.Mpeg1;

namespace Adk.Video.Matroska
{
    /// <summary>
    /// Plays the MPEG-1 track of a Matroska file by time: <see cref="SeekTo"/> decodes forward from the
    /// current picture, or from the closest earlier key frame when going back or far ahead.
    /// Not thread-safe; use one instance per consumer.
    /// </summary>
    public sealed class MatroskaMpeg1Video
    {
        const int RestartDistance = 90;
        const double Epsilon = 1e-6;

        readonly MatroskaFile _file;
        readonly MatroskaTrack _track;
        readonly Mpeg1VideoDecoder _decoder;
        readonly bool _reorders;
        Mpeg1Picture _shown;
        double _heldTime = double.MaxValue;
        int _next;

        public MatroskaMpeg1Video(MatroskaFile file, MatroskaTrack track)
        {
            if (file == null)
                throw new ArgumentNullException(nameof(file));
            if (track == null || track.CodecId != MediaProbe.Mpeg1CodecId)
                throw new NotSupportedException("The track is not MPEG-1 video.");

            _file = file;
            _track = track;
            _decoder = new Mpeg1VideoDecoder(track.CodecPrivate);
            for (int i = 0; i < track.FrameCount && !_reorders; i++)
            {
                MatroskaFrame frame = track.GetFrame(i);
                _reorders = Mpeg1VideoDecoder.GetPictureType(file.Data, frame.Offset, frame.Length) == 3;
            }

            _decoder.LowDelay = !_reorders;
        }

        public int Width => _decoder.HasSequenceHeader ? _decoder.Width : _track.PixelWidth;
        public int Height => _decoder.HasSequenceHeader ? _decoder.Height : _track.PixelHeight;

        /// <summary>The picture showing after the last <see cref="SeekTo"/>, or null before the first frame.</summary>
        public Mpeg1Picture Picture => _shown;

        /// <summary>Brings <see cref="Picture"/> to the frame showing at <paramref name="seconds"/>; true when it changed.</summary>
        public bool SeekTo(double seconds)
        {
            if (_track.FrameCount == 0)
                return false;

            int target = LastFrameAtOrBefore(seconds);
            if (target < 0)
                return false;

            if (_shown == null || seconds < _shown.Time || target - _next > RestartDistance)
                Restart(seconds);

            bool changed = false;
            while (_next < _track.FrameCount && ShouldDecode(_next, seconds))
            {
                MatroskaFrame frame = _track.GetFrame(_next++);
                Mpeg1Picture picture = _decoder.Decode(_file.Data, frame.Offset, frame.Length, frame.Time);
                if (_reorders && IsReference(frame))
                    _heldTime = frame.Time;
                if (picture != null && picture.Time <= seconds + Epsilon)
                {
                    Keep(picture);
                    changed = true;
                }
            }

            if (_next >= _track.FrameCount && _reorders)
            {
                Mpeg1Picture last = _heldTime <= seconds + Epsilon ? _decoder.Flush() : null;
                if (last != null)
                    _heldTime = double.MaxValue;
                if (last != null && last.Time <= seconds + Epsilon)
                {
                    Keep(last);
                    changed = true;
                }
            }

            return changed;
        }

        /// <summary>
        /// A frame due by <paramref name="seconds"/> is decoded. With B pictures the decoder holds the last
        /// reference picture back until the next one arrives, so that next reference is decoded early
        /// whenever the held picture is already due.
        /// </summary>
        bool ShouldDecode(int index, double seconds)
        {
            MatroskaFrame frame = _track.GetFrame(index);
            if (frame.Time <= seconds + Epsilon)
                return true;

            return _reorders && _heldTime <= seconds + Epsilon && IsReference(frame);
        }

        bool IsReference(MatroskaFrame frame)
        {
            int type = Mpeg1VideoDecoder.GetPictureType(_file.Data, frame.Offset, frame.Length);
            return type == 1 || type == 2;
        }

        void Restart(double seconds)
        {
            int key = 0;
            for (int i = 0; i < _track.FrameCount; i++)
            {
                MatroskaFrame frame = _track.GetFrame(i);
                if (frame.Time > seconds + Epsilon)
                    break;
                if (frame.IsKeyFrame)
                    key = i;
            }

            if (_shown != null && _next > key && seconds >= _shown.Time)
                return;

            _decoder.Reset();
            _heldTime = double.MaxValue;
            _next = key;
            _shown = null;
        }

        int LastFrameAtOrBefore(double seconds)
        {
            int found = -1;
            for (int i = 0; i < _track.FrameCount; i++)
            {
                if (_track.GetFrame(i).Time <= seconds + Epsilon)
                    found = i;
            }

            return found;
        }

        void Keep(Mpeg1Picture picture)
        {
            if (_shown == null || _shown.Width != picture.Width || _shown.Height != picture.Height ||
                _shown.LumaStride != picture.LumaStride || _shown.Luma.Length != picture.Luma.Length)
            {
                _shown = new Mpeg1Picture(picture.Width, picture.Height, picture.LumaStride / 16, picture.PlaneHeight / 16);
            }

            Buffer.BlockCopy(picture.Luma, 0, _shown.Luma, 0, picture.Luma.Length);
            Buffer.BlockCopy(picture.Cb, 0, _shown.Cb, 0, picture.Cb.Length);
            Buffer.BlockCopy(picture.Cr, 0, _shown.Cr, 0, picture.Cr.Length);
            _shown.PictureType = picture.PictureType;
            _shown.Time = picture.Time;
        }
    }
}
