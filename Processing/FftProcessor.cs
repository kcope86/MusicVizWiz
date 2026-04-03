using NAudio.Dsp;

namespace MusicVisualizer.Processing;

/// <summary>
/// Performs FFT on audio samples and produces magnitude data.
/// </summary>
public sealed class FftProcessor
{
    private readonly int _fftSize;
    private readonly int _m;

    private readonly Complex[] _fftBuffer;

    /// <summary>
    /// Initializes a new instance of the <see cref="FftProcessor"/> class.
    /// </summary>
    /// <param name="fftSize">Must be a power of two (e.g., 1024, 2048, 4096).</param>
    public FftProcessor(int fftSize)
    {
        if (!IsPowerOfTwo(fftSize))
        {
            throw new ArgumentException("FFT size must be a power of two.");
        }

        _fftSize = fftSize;
        _m = (int)Math.Log2(fftSize);
        _fftBuffer = new Complex[fftSize];
    }

    /// <summary>
    /// Processes the provided samples and returns magnitude spectrum.
    /// </summary>
    public float[] ComputeMagnitude(float[] samples)
    {
        if (samples.Length < _fftSize)
        {
            return Array.Empty<float>();
        }

        // Take the last N samples
        int offset = samples.Length - _fftSize;

        for (int i = 0; i < _fftSize; i++)
        {
            float windowed = ApplyHannWindow(samples[offset + i], i, _fftSize);

            _fftBuffer[i].X = windowed;
            _fftBuffer[i].Y = 0;
        }

        FastFourierTransform.FFT(true, _m, _fftBuffer);

        int spectrumSize = _fftSize / 2;
        float[] magnitudes = new float[spectrumSize];

        for (int i = 0; i < spectrumSize; i++)
        {
            float real = _fftBuffer[i].X;
            float imag = _fftBuffer[i].Y;

            float magnitude = MathF.Sqrt(real * real + imag * imag);

            magnitudes[i] = magnitude;
        }

        return magnitudes;
    }

    private static float ApplyHannWindow(float sample, int index, int size)
    {
        float multiplier = 0.5f * (1 - MathF.Cos(2 * MathF.PI * index / (size - 1)));
        return sample * multiplier;
    }

    private static bool IsPowerOfTwo(int x)
    {
        return (x & (x - 1)) == 0;
    }
}