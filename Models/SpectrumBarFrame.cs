namespace MusicVisualizer.Models;

/// <summary>
/// Represents a frame of bar magnitudes for the spectrum visualizer.
/// </summary>
public sealed class SpectrumBarFrame
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SpectrumBarFrame"/> class.
    /// </summary>
    /// <param name="bars">The bar values for the current frame.</param>
    public SpectrumBarFrame(float[] bars)
    {
        Bars = bars ?? throw new ArgumentNullException(nameof(bars));
    }

    /// <summary>
    /// Gets the current bar values.
    /// </summary>
    public float[] Bars { get; }
}