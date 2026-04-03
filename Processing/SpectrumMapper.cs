namespace MusicVisualizer.Processing;

/// <summary>
/// Maps FFT magnitude bins into a fixed number of display bars using logarithmic grouping.
/// </summary>
public sealed class SpectrumMapper
{
    private readonly int _barCount;
    private readonly int _sampleRate;
    private readonly int _fftSize;
    private readonly int _spectrumBinCount;
    private readonly (int Start, int End)[] _barBinRanges;

    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrumMapper"/> class.
    /// </summary>
    /// <param name="barCount">The number of bars to produce.</param>
    /// <param name="sampleRate">The audio sample rate.</param>
    /// <param name="fftSize">The FFT size used to generate the magnitude data.</param>
    public SpectrumMapper(int barCount, int sampleRate, int fftSize)
    {
        if (barCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(barCount));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate));
        }

        if (fftSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(fftSize));
        }

        _barCount = barCount;
        _sampleRate = sampleRate;
        _fftSize = fftSize;
        _spectrumBinCount = fftSize / 2;
        _barBinRanges = BuildLogarithmicRanges();
    }

    /// <summary>
    /// Maps raw FFT magnitudes into scaled bar values in the range 0.0 to 1.0.
    /// </summary>
    /// <param name="magnitudes">The raw FFT magnitude bins.</param>
    /// <returns>The mapped bar values.</returns>
    public float[] MapToBars(float[] magnitudes)
    {
        ArgumentNullException.ThrowIfNull(magnitudes);

        if (magnitudes.Length == 0)
        {
            return new float[_barCount];
        }

        float[] bars = new float[_barCount];

        for (int barIndex = 0; barIndex < _barCount; barIndex++)
        {
            (int start, int end) = _barBinRanges[barIndex];

            start = Math.Clamp(start, 0, magnitudes.Length - 1);
            end = Math.Clamp(end, start + 1, magnitudes.Length);

            float sum = 0f;
            int count = 0;

            for (int bin = start; bin < end; bin++)
            {
                sum += magnitudes[bin];
                count++;
            }

            float averageMagnitude = count > 0 ? sum / count : 0f;

            float scaled = ScaleMagnitudeForDisplay(averageMagnitude, barIndex);
            bars[barIndex] = Math.Clamp(scaled, 0f, 1f);
        }

        return bars;
    }

    private (int Start, int End)[] BuildLogarithmicRanges()
    {
        (int Start, int End)[] ranges = new (int Start, int End)[_barCount];

        const float minFrequency = 20f;
        float maxFrequency = _sampleRate / 2f;

        double logMin = Math.Log10(minFrequency);
        double logMax = Math.Log10(maxFrequency);

        for (int i = 0; i < _barCount; i++)
        {
            double startFraction = (double)i / _barCount;
            double endFraction = (double)(i + 1) / _barCount;

            double startLog = logMin + ((logMax - logMin) * startFraction);
            double endLog = logMin + ((logMax - logMin) * endFraction);

            double startFrequency = Math.Pow(10, startLog);
            double endFrequency = Math.Pow(10, endLog);

            int startBin = FrequencyToBin(startFrequency);
            int endBin = FrequencyToBin(endFrequency);

            if (endBin <= startBin)
            {
                endBin = startBin + 1;
            }

            ranges[i] = (startBin, endBin);
        }

        return ranges;
    }

    private int FrequencyToBin(double frequency)
    {
        double nyquist = _sampleRate / 2.0;
        double normalized = frequency / nyquist;
        int bin = (int)(normalized * _spectrumBinCount);

        return Math.Clamp(bin, 0, _spectrumBinCount - 1);
    }

    private static float ScaleMagnitudeForDisplay(float magnitude, int barIndex)
    {
        float boosted = magnitude * 18f;

        float logScaled = MathF.Log10(1f + (boosted * 9f));

        float lowFrequencyCompensation = 1.15f - (barIndex / 128f);
        float finalValue = logScaled * lowFrequencyCompensation;

        return finalValue;
    }
}