using NAudio.CoreAudioApi;

namespace MusicVisualizer.Audio;

/// <summary>
/// Provides methods for discovering and retrieving Windows audio render devices.
/// </summary>
public sealed class AudioDeviceService
{
    /// <summary>
    /// Gets all active Windows render devices that can be used for loopback capture.
    /// </summary>
    public IReadOnlyList<AudioDeviceInfo> GetRenderDevices()
    {
        using MMDeviceEnumerator enumerator = new();

        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        List<AudioDeviceInfo> results = new();

        foreach (MMDevice device in devices)
        {
            results.Add(new AudioDeviceInfo
            {
                Id = device.ID,
                Name = device.FriendlyName,
                State = device.State.ToString()
            });
        }

        return results;
    }

    /// <summary>
    /// Gets an active render device by its exact MMDevice ID.
    /// </summary>
    /// <param name="deviceId">The exact MMDevice ID.</param>
    /// <returns>The matching device info, or null if not found.</returns>
    public AudioDeviceInfo? GetRenderDeviceById(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        return GetRenderDevices().FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Gets the active MMDevice by its exact ID.
    /// </summary>
    /// <param name="deviceId">The exact MMDevice ID.</param>
    /// <returns>The matching MMDevice.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the device cannot be found.</exception>
    public MMDevice GetMmDeviceById(string deviceId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        MMDeviceEnumerator enumerator = new();
        MMDeviceCollection devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);

        foreach (MMDevice device in devices)
        {
            if (string.Equals(device.ID, deviceId, StringComparison.OrdinalIgnoreCase))
            {
                return device;
            }
        }

        enumerator.Dispose();

        throw new InvalidOperationException($"Could not find active render device with ID '{deviceId}'.");
    }
}