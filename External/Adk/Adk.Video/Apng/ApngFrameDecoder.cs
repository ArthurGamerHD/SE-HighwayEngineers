// ReSharper disable RedundantUsingDirective
using System;
using Adk.Image.Png;
using ArgumentOutOfRangeException = Adk.Compression.Exceptions.ArgumentOutOfRangeException;

namespace Adk.Video.Apng
{
    /// <summary>
    /// Composites the frames of an <see cref="ApngVideo"/> onto a straight-alpha,
    /// top-to-bottom RGBA8 canvas, applying each frame's dispose and blend ops.
    /// Not thread-safe; use one decoder per consumer.
    /// </summary>
    public sealed class ApngFrameDecoder
    {
        readonly ApngVideo _video;
        readonly byte[] _canvas;
        byte[] _saved;
        int _index = -1;

        // Whether the dispose op of the frame at _index has already been applied to the canvas.
        bool _disposed = true;

        public ApngVideo Video => _video;

        /// <summary>Straight-alpha RGBA8 pixels of the last decoded frame, <c>Width * Height * 4</c> bytes.</summary>
        public byte[] Canvas => _canvas;

        /// <summary>Index of the frame the canvas shows, or -1 before the first decode.</summary>
        public int FrameIndex => _index;

        internal ApngFrameDecoder(ApngVideo video)
        {
            _video = video;
            _canvas = new byte[checked(video.Width * video.Height * 4)];
        }

        /// <summary>Clears the canvas and rewinds to before the first frame.</summary>
        public void Reset()
        {
            Array.Clear(_canvas, 0, _canvas.Length);
            _index = -1;
            _disposed = true;
        }

        /// <summary>Decodes the frame after the current one, wrapping to the first frame at the end.</summary>
        public void DecodeNext()
        {
            int next = _index + 1;
            if (next >= _video.FrameCount)
            {
                Reset();
                next = 0;
            }

            Advance(next);
        }

        /// <summary>
        /// Brings the canvas to <paramref name="index"/>, decoding forward from the
        /// current frame or from the closest key frame, whichever is fewer frames.
        /// </summary>
        public void SeekTo(int index)
        {
            if ((uint)index >= (uint)_video.FrameCount)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (index == _index)
                return;

            int keyFrame = index;
            while (!_video.GetFrame(keyFrame).IsKeyFrame)
                keyFrame--;

            if (_index < keyFrame || _index > index)
            {
                // A key frame does not depend on the canvas, so a cleared one stands in for its predecessors.
                Array.Clear(_canvas, 0, _canvas.Length);
                _index = keyFrame - 1;
                _disposed = true;
            }

            while (_index < index)
                Advance(_index + 1);
        }

        void Advance(int next)
        {
            if (!_disposed)
                Dispose(_video.GetFrame(_index));

            ApngFrameInfo frame = _video.GetFrame(next);
            if (frame.DisposeOp == ApngDisposeOp.Previous)
                SaveRegion(frame);

            byte[] pixels = DecodePixels(frame);
            if (frame.BlendOp == ApngBlendOp.Source)
                CopyRegion(pixels, frame);
            else
                BlendRegion(pixels, frame);

            _index = next;
            _disposed = false;
        }

        byte[] DecodePixels(ApngFrameInfo frame)
        {
            byte[] data = _video.Data;
            int size = 0;
            for (int i = 0; i < frame.Chunks.Count; i++)
                size = checked(size + frame.Chunks[i].Length);

            byte[] compressed = new byte[size];
            int offset = 0;
            for (int i = 0; i < frame.Chunks.Count; i++)
            {
                ChunkRange chunk = frame.Chunks[i];
                int chunkLength = chunk.DataOffset - chunk.ChunkOffset - 8 + chunk.Length;
                ApngVideo.VerifyCrc(data, chunk.ChunkOffset, chunkLength);
                Buffer.BlockCopy(data, chunk.DataOffset, compressed, offset, chunk.Length);
                offset += chunk.Length;
            }

            ushort[] red16;
            return PngDecoder.DecodeImageData(
                compressed,
                frame.Width,
                frame.Height,
                _video.BitDepth,
                _video.ColorType,
                _video.InterlaceMethod,
                _video.Palette,
                _video.Transparency,
                out red16);
        }

        void Dispose(ApngFrameInfo frame)
        {
            if (frame.DisposeOp == ApngDisposeOp.Background)
            {
                int rowBytes = frame.Width * 4;
                for (int y = 0; y < frame.Height; y++)
                    Array.Clear(_canvas, CanvasOffset(frame.X, frame.Y + y), rowBytes);
            }
            else if (frame.DisposeOp == ApngDisposeOp.Previous)
            {
                CopyRegion(_saved, frame);
            }

            _disposed = true;
        }

        void SaveRegion(ApngFrameInfo frame)
        {
            int rowBytes = frame.Width * 4;
            int size = rowBytes * frame.Height;
            if (_saved == null || _saved.Length < size)
                _saved = new byte[size];

            for (int y = 0; y < frame.Height; y++)
                Buffer.BlockCopy(_canvas, CanvasOffset(frame.X, frame.Y + y), _saved, y * rowBytes, rowBytes);
        }

        void CopyRegion(byte[] source, ApngFrameInfo frame)
        {
            int rowBytes = frame.Width * 4;
            for (int y = 0; y < frame.Height; y++)
                Buffer.BlockCopy(source, y * rowBytes, _canvas, CanvasOffset(frame.X, frame.Y + y), rowBytes);
        }

        void BlendRegion(byte[] source, ApngFrameInfo frame)
        {
            int rowBytes = frame.Width * 4;
            for (int y = 0; y < frame.Height; y++)
            {
                int src = y * rowBytes;
                int dst = CanvasOffset(frame.X, frame.Y + y);
                for (int x = 0; x < frame.Width; x++, src += 4, dst += 4)
                {
                    int sourceAlpha = source[src + 3];
                    if (sourceAlpha == 0)
                        continue;

                    if (sourceAlpha == 255)
                    {
                        _canvas[dst] = source[src];
                        _canvas[dst + 1] = source[src + 1];
                        _canvas[dst + 2] = source[src + 2];
                        _canvas[dst + 3] = 255;
                        continue;
                    }

                    // Straight-alpha "over": the canvas keeps (1 - a) of its own coverage.
                    int destinationWeight = _canvas[dst + 3] * (255 - sourceAlpha) / 255;
                    int alpha = sourceAlpha + destinationWeight;
                    for (int c = 0; c < 3; c++)
                    {
                        _canvas[dst + c] = (byte)((source[src + c] * sourceAlpha +
                                                   _canvas[dst + c] * destinationWeight) / alpha);
                    }

                    _canvas[dst + 3] = (byte)alpha;
                }
            }
        }

        int CanvasOffset(int x, int y)
        {
            return (y * _video.Width + x) * 4;
        }
    }
}
