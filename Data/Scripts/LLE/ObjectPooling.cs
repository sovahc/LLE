using System;

public class ObjectPooling<T> where T : class, new()
{
    private const int BufferCount = 4;

    private readonly T[][] _buffers;
    private T[] _activeBuffer;

    private int _ringIndex = 0;
    private int _currentIndex = 0;
    private int _peakUsage = 0;

    public ObjectPooling()
    {
        _buffers = new T[BufferCount][];
    }

    public void StartFrame(int initialCapacity = 1024)
    {
        _activeBuffer = _buffers[_ringIndex];
        _ringIndex = (_ringIndex + 1) % BufferCount;

        if (_activeBuffer == null || _peakUsage >= _activeBuffer.Length)
        {
            int newSize = Math.Max(initialCapacity, _peakUsage * 2);
            Array.Resize(ref _activeBuffer, newSize);

            int prevIndex = (_ringIndex + BufferCount - 1) % BufferCount;
            _buffers[prevIndex] = _activeBuffer;
        }

        _currentIndex = 0;
    }

    public T Get()
    {
        if (_currentIndex < _activeBuffer.Length)
        {
            var item = _activeBuffer[_currentIndex];

            if (item == null)
            {
                item = new T();
                _activeBuffer[_currentIndex] = item;
            }

            if (_currentIndex + 1 > _peakUsage)
                _peakUsage = _currentIndex + 1;

            _currentIndex++;

            return item;
        }

        return new T();
    }
}
