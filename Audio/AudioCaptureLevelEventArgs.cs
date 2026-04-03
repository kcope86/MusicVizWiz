namespace MusicVisualizer.Audio;

/// <summary>
/// Represents a captured audio level update.
/// </summary>
public sealed class AudioCaptureLevelEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AudioCaptureLevelEventArgs"/> class.
    /// </summary>
    /// <param name="peakLevel">The normalized peak level from 0.0 to 1.0.</param>
    public AudioCaptureLevelEventArgs(float peakLevel)
    {
        PeakLevel = peakLevel;
    }

    /// <summary>
    /// Gets the normalized peak level from 0.0 to 1.0.
    /// </summary>
    public float PeakLevel { get; }
}