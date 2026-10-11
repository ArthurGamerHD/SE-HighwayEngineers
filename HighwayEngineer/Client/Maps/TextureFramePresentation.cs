namespace HighwayEngineer.Client.Maps
{
    // Freeze metadata at upload time, then publish at the drawing boundary. Live
    // input may already have moved the camera again; adding a whole extra draw
    // on top of the renderer's upload scheduling makes overlays lag the texture.
    internal sealed class TextureFramePresentation<T> where T : class, new()
    {
        T _uploaded;
        T _ready;
        public T Current { get; private set; } = new T();

        public void Uploaded(T frame) { _uploaded = frame; }

        public void BeginDraw()
        {
            T frame = _uploaded ?? _ready;
            if (frame == null) return;
            Current = frame;
            _uploaded = null;
            _ready = null;
        }

        public void EndDraw()
        {
            if (_uploaded == null) return;
            _ready = _uploaded;
            _uploaded = null;
        }

        public void Reset()
        {
            Current = new T();
            _uploaded = null;
            _ready = null;
        }
    }
}
