namespace WisperTranslator.Core.Audio;

/// <summary>
/// Buffer circolare di float thread-safe. Se il produttore supera la capacità
/// scarta i campioni più vecchi: meglio perdere audio che accumulare ritardo.
/// </summary>
public sealed class FloatRingBuffer
{
    private readonly float[] _buffer;
    private readonly object _gate = new();
    private int _head;
    private int _count;

    public FloatRingBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "La capacità deve essere positiva.");
        }

        _buffer = new float[capacity];
    }

    public int Capacity => _buffer.Length;

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _count;
            }
        }
    }

    public void Write(ReadOnlySpan<float> source)
    {
        if (source.IsEmpty)
        {
            return;
        }

        lock (_gate)
        {
            if (source.Length >= _buffer.Length)
            {
                source[^_buffer.Length..].CopyTo(_buffer);
                _head = 0;
                _count = _buffer.Length;
                return;
            }

            var overflow = _count + source.Length - _buffer.Length;
            if (overflow > 0)
            {
                _head = (_head + overflow) % _buffer.Length;
                _count -= overflow;
            }

            var write = (_head + _count) % _buffer.Length;
            var first = Math.Min(source.Length, _buffer.Length - write);
            source[..first].CopyTo(_buffer.AsSpan(write));
            if (first < source.Length)
            {
                source[first..].CopyTo(_buffer);
            }

            _count += source.Length;
        }
    }

    /// <summary>Copia fino a <c>destination.Length</c> campioni; restituisce quanti ne ha copiati.</summary>
    public int Read(Span<float> destination)
    {
        lock (_gate)
        {
            var available = Math.Min(destination.Length, _count);
            if (available == 0)
            {
                return 0;
            }

            var first = Math.Min(available, _buffer.Length - _head);
            _buffer.AsSpan(_head, first).CopyTo(destination);
            if (first < available)
            {
                _buffer.AsSpan(0, available - first).CopyTo(destination[first..]);
            }

            _head = (_head + available) % _buffer.Length;
            _count -= available;
            return available;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _head = 0;
            _count = 0;
        }
    }
}
