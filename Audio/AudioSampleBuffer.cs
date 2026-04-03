namespace MusicVisualizer.Audio;

/// <summary>
/// Stores a rolling window of recent audio samples in a thread-safe circular buffer.
/// </summary>
public sealed class AudioSampleBuffer
{
    private readonly float[] _buffer;
    private readonly object _syncRoot = new();

    private int _writeIndex;
    private int _count;

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioSampleBuffer"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of samples to retain.</param>
    public AudioSampleBuffer(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Capacity must be greater than zero.");
        }

        _buffer = new float[capacity];
    }

    /// <summary>
    /// Gets the maximum number of samples the buffer can retain.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the current number of stored samples.
    /// </summary>
    public int Count
    {
        get
        {
            lock (_syncRoot)
            {
                return _count;
            }
        }
    }

    /// <summary>
    /// Appends samples to the rolling buffer, overwriting the oldest samples when full.
    /// </summary>
    /// <param name="samples">The samples to append.</param>
    /// <param name="sampleCount">The number of samples to append.</param>
    public void AddSamples(float[] samples, int sampleCount)
    {
        ArgumentNullException.ThrowIfNull(samples);

        if (sampleCount < 0 || sampleCount > samples.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleCount));
        }

        lock (_syncRoot)
        {
            for (int i = 0; i < sampleCount; i++)
            {
                _buffer[_writeIndex] = samples[i];
                _writeIndex = (_writeIndex + 1) % _buffer.Length;

                if (_count < _buffer.Length)
                {
                    _count++;
                }
            }
        }
    }

    /// <summary>
    /// Copies the most recent samples into a new array.
    /// </summary>
    /// <param name="requestedSampleCount">The number of recent samples to retrieve.</param>
    /// <returns>A new array containing the requested most recent samples, oldest to newest.</returns>
    public float[] GetLatestSamples(int requestedSampleCount)
    {
        if (requestedSampleCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSampleCount), "Requested sample count must be greater than zero.");
        }

        lock (_syncRoot)
        {
            int actualCount = Math.Min(requestedSampleCount, _count);
            float[] result = new float[actualCount];

            int startIndex = (_writeIndex - actualCount + _buffer.Length) % _buffer.Length;

            for (int i = 0; i < actualCount; i++)
            {
                int sourceIndex = (startIndex + i) % _buffer.Length;
                result[i] = _buffer[sourceIndex];
            }

            return result;
        }
    }
}