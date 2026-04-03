using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MusicVisualizer.Audio;

/// <summary>
/// Captures loopback audio from a specific Windows render device and raises level updates.
/// </summary>
public sealed class LoopbackCaptureService : IDisposable
{
    private const int RollingBufferCapacity = 192000;

    private WasapiLoopbackCapture? _capture;
    private bool _isDisposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="LoopbackCaptureService"/> class.
    /// </summary>
    public LoopbackCaptureService()
    {
        SampleBuffer = new AudioSampleBuffer(RollingBufferCapacity);
    }

    /// <summary>
    /// Occurs when a new capture peak level is available.
    /// </summary>
    public event EventHandler<AudioCaptureLevelEventArgs>? LevelCalculated;

    /// <summary>
    /// Occurs when capture status text changes.
    /// </summary>
    public event EventHandler<string>? StatusChanged;

    /// <summary>
    /// Gets a value indicating whether capture is currently running.
    /// </summary>
    public bool IsRunning { get; private set; }

    /// <summary>
    /// Gets the rolling buffer of recently captured samples.
    /// </summary>
    public AudioSampleBuffer SampleBuffer { get; }

    /// <summary>
    /// Starts loopback capture for the specified MMDevice.
    /// </summary>
    /// <param name="device">The render device to capture.</param>
    public void Start(MMDevice device)
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
        ArgumentNullException.ThrowIfNull(device);

        Stop();

        _capture = new WasapiLoopbackCapture(device);
        _capture.DataAvailable += OnDataAvailable;
        _capture.RecordingStopped += OnRecordingStopped;
        _capture.StartRecording();

        IsRunning = true;

        OnStatusChanged($"Capturing from: {device.FriendlyName}");
        System.Diagnostics.Debug.WriteLine($"[Capture] Started: {device.FriendlyName}");
        System.Diagnostics.Debug.WriteLine($"[Capture] WaveFormat: {_capture.WaveFormat}");
    }

    /// <summary>
    /// Stops capture if it is running.
    /// </summary>
    public void Stop()
    {
        if (_capture is null)
        {
            IsRunning = false;
            return;
        }

        try
        {
            _capture.DataAvailable -= OnDataAvailable;
            _capture.RecordingStopped -= OnRecordingStopped;
            _capture.StopRecording();
        }
        catch
        {
            // Intentionally swallow stop-time exceptions for this POC.
        }
        finally
        {
            _capture.Dispose();
            _capture = null;
            IsRunning = false;
            OnStatusChanged("Capture stopped.");
            System.Diagnostics.Debug.WriteLine("[Capture] Stopped.");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        Stop();
        _isDisposed = true;
    }

    private void OnDataAvailable(object? sender, WaveInEventArgs e)
    {
        if (_capture is null)
        {
            return;
        }

        WaveFormat format = _capture.WaveFormat;

        if (format.Encoding != WaveFormatEncoding.IeeeFloat || format.BitsPerSample != 32)
        {
            OnStatusChanged(
                $"Unsupported format for current POC: {format.Encoding}, {format.BitsPerSample}-bit");

            return;
        }

        int sampleCount = e.BytesRecorded / 4;
        float[] samples = new float[sampleCount];
        float peak = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float sample = BitConverter.ToSingle(e.Buffer, i * 4);
            samples[i] = sample;

            float magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        SampleBuffer.AddSamples(samples, sampleCount);

        LevelCalculated?.Invoke(this, new AudioCaptureLevelEventArgs(Math.Clamp(peak, 0f, 1f)));
    }

    private void OnRecordingStopped(object? sender, StoppedEventArgs e)
    {
        IsRunning = false;

        if (e.Exception is not null)
        {
            OnStatusChanged($"Capture stopped with error: {e.Exception.Message}");
            System.Diagnostics.Debug.WriteLine($"[Capture] Error: {e.Exception}");
            return;
        }

        OnStatusChanged("Capture stopped.");
    }

    private void OnStatusChanged(string message)
    {
        StatusChanged?.Invoke(this, message);
    }
}