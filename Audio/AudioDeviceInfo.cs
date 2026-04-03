namespace MusicVisualizer.Audio;

/// <summary>
/// Represents a Windows audio render device that can be used for loopback capture.
/// </summary>
public sealed class AudioDeviceInfo
{
    /// <summary>
    /// Gets or sets the MMDevice ID.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Gets or sets the friendly name shown in Windows.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets or sets the device state as a string.
    /// </summary>
    public required string State { get; init; }

    /// <summary>
    /// Returns the friendly display text for the device.
    /// </summary>
    public override string ToString()
    {
        return $"{Name} [{State}]";
    }
}