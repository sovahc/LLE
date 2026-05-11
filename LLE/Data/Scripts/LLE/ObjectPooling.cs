using System;

namespace LLE {
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

    public void StartFrame(int initialCapacity = 0x100)
    {
        _activeBuffer = _buffers[_ringIndex];

        if (_activeBuffer == null || _peakUsage > _activeBuffer.Length)
        {
            var oldSize = _activeBuffer == null ? 0 : _activeBuffer.Length;
            var newSize = initialCapacity;
            while(newSize < _peakUsage) newSize <<= 1;

            //Utilities.Log($"ObjectPooling reallocation: index {_ringIndex} oldSize {oldSize} newSize {newSize}");

            Array.Resize(ref _activeBuffer, newSize);

            _buffers[_ringIndex] = _activeBuffer;
        }

        _ringIndex = (_ringIndex + 1) % BufferCount;

        _currentIndex = 0;
    }

    public T Get()
    {
        T item;

        if (_currentIndex < _activeBuffer.Length)
        {
            item = _activeBuffer[_currentIndex];

            if (item == null)
            {
                item = new T();
                _activeBuffer[_currentIndex] = item;
            }
        }
        else
        {   item = new T();            
        }
        
        ++_currentIndex;

        if (_currentIndex > _peakUsage)
            _peakUsage = _currentIndex;

        return item;
    }
}
}
