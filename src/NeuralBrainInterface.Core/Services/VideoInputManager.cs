using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Handles real-time webcam input and video processing for visual interactions.
/// Manages video capture, device selection, and configuration.
/// </summary>
public class VideoInputManager : IVideoInputManager
{
    private bool _isEnabled;
    private VideoSettings _currentSettings;
    private string _currentDeviceName;
    private readonly List<string> _availableDevices;
    private VideoStreamImpl? _videoStream;
    private readonly object _lock = new();

    public bool IsEnabled => _isEnabled;

    public event EventHandler<ImageData>? FrameCaptured;
    public event EventHandler<string>? VideoError;

    public VideoInputManager()
    {
        _isEnabled = false;
        _currentSettings = new VideoSettings();
        _currentDeviceName = "Default Webcam";
        _availableDevices = new List<string>
        {
            "Default Webcam",
            "Built-in Camera",
            "External USB Camera",
            "Virtual Camera"
        };
    }

    public async Task<bool> EnableWebcamAsync()
    {
        try
        {
            lock (_lock)
            {
                if (_isEnabled)
                {
                    return true; // Already enabled
                }

                // Simulate webcam initialization
                _videoStream = new VideoStreamImpl(_currentSettings);
                _videoStream.FrameReceived += OnFrameReceived;
                _videoStream.Start();
                _isEnabled = true;
            }

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            VideoError?.Invoke(this, $"Failed to enable webcam: {ex.Message}");
            return false;
        }
    }

    public async Task DisableWebcamAsync()
    {
        try
        {
            lock (_lock)
            {
                if (!_isEnabled)
                {
                    return; // Already disabled
                }

                if (_videoStream != null)
                {
                    _videoStream.Stop();
                    _videoStream.FrameReceived -= OnFrameReceived;
                    _videoStream = null;
                }

                _isEnabled = false;
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            VideoError?.Invoke(this, $"Failed to disable webcam: {ex.Message}");
        }
    }

    public IVideoStream GetVideoStream()
    {
        lock (_lock)
        {
            if (_videoStream == null)
            {
                _videoStream = new VideoStreamImpl(_currentSettings);
            }
            return _videoStream;
        }
    }

    public async Task ConfigureVideoSettingsAsync(VideoSettings settings)
    {
        try
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // Validate settings
            if (settings.Resolution.Width <= 0 || settings.Resolution.Height <= 0)
            {
                throw new ArgumentException("Resolution must have positive dimensions", nameof(settings));
            }

            if (settings.FrameRate <= 0 || settings.FrameRate > 120)
            {
                throw new ArgumentException("Frame rate must be between 1 and 120", nameof(settings));
            }

            if (settings.CompressionQuality < 0 || settings.CompressionQuality > 1.0f)
            {
                throw new ArgumentException("Compression quality must be between 0 and 1.0", nameof(settings));
            }

            lock (_lock)
            {
                var wasEnabled = _isEnabled;

                // If currently enabled, restart with new settings
                if (wasEnabled)
                {
                    _videoStream?.Stop();
                }

                _currentSettings = new VideoSettings
                {
                    Resolution = settings.Resolution,
                    FrameRate = settings.FrameRate,
                    ColorFormat = settings.ColorFormat ?? "RGB24",
                    CompressionQuality = settings.CompressionQuality
                };

                if (wasEnabled && _videoStream != null)
                {
                    _videoStream = new VideoStreamImpl(_currentSettings);
                    _videoStream.FrameReceived += OnFrameReceived;
                    _videoStream.Start();
                }
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            VideoError?.Invoke(this, $"Failed to configure video settings: {ex.Message}");
            throw;
        }
    }

    public async Task<List<string>> GetAvailableCamerasAsync()
    {
        // In a real implementation, this would enumerate actual video devices
        return await Task.FromResult(new List<string>(_availableDevices));
    }

    public async Task<bool> SetCameraDeviceAsync(string deviceName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                VideoError?.Invoke(this, "Device name cannot be empty");
                return false;
            }

            if (!_availableDevices.Contains(deviceName))
            {
                VideoError?.Invoke(this, $"Device not found: {deviceName}");
                return false;
            }

            lock (_lock)
            {
                var wasEnabled = _isEnabled;

                // If currently enabled, restart with new device
                if (wasEnabled)
                {
                    _videoStream?.Stop();
                }

                _currentDeviceName = deviceName;

                if (wasEnabled)
                {
                    _videoStream = new VideoStreamImpl(_currentSettings);
                    _videoStream.FrameReceived += OnFrameReceived;
                    _videoStream.Start();
                }
            }

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            VideoError?.Invoke(this, $"Failed to set camera device: {ex.Message}");
            return false;
        }
    }

    private void OnFrameReceived(object? sender, ImageData frame)
    {
        FrameCaptured?.Invoke(this, frame);
    }

    /// <summary>
    /// Gets the current video settings.
    /// </summary>
    public VideoSettings GetCurrentSettings()
    {
        lock (_lock)
        {
            return new VideoSettings
            {
                Resolution = _currentSettings.Resolution,
                FrameRate = _currentSettings.FrameRate,
                ColorFormat = _currentSettings.ColorFormat,
                CompressionQuality = _currentSettings.CompressionQuality
            };
        }
    }

    /// <summary>
    /// Gets the current device name.
    /// </summary>
    public string GetCurrentDeviceName()
    {
        lock (_lock)
        {
            return _currentDeviceName;
        }
    }
}

/// <summary>
/// Implementation of IVideoStream for real-time video capture.
/// </summary>
internal class VideoStreamImpl : IVideoStream
{
    private bool _isActive;
    private readonly VideoSettings _settings;
    private Timer? _captureTimer;
    private readonly object _lock = new();
    private int _frameCounter;

    public bool IsActive => _isActive;

    public event EventHandler<ImageData>? FrameReceived;

    public VideoStreamImpl(VideoSettings settings)
    {
        _settings = settings;
        _isActive = false;
        _frameCounter = 0;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_isActive)
            {
                return;
            }

            _isActive = true;
            _frameCounter = 0;

            // Calculate interval based on frame rate
            var intervalMs = 1000 / _settings.FrameRate;
            _captureTimer = new Timer(CaptureFrameCallback, null, 0, intervalMs);
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (!_isActive)
            {
                return;
            }

            _isActive = false;
            _captureTimer?.Dispose();
            _captureTimer = null;
        }
    }

    private void CaptureFrameCallback(object? state)
    {
        if (!_isActive)
        {
            return;
        }

        // Simulate captured video frame
        var frame = new ImageData
        {
            Data = GenerateSimulatedFrameData(),
            Width = _settings.Resolution.Width,
            Height = _settings.Resolution.Height,
            Format = _settings.ColorFormat,
            Metadata = new Dictionary<string, object>
            {
                ["FrameNumber"] = _frameCounter++,
                ["Timestamp"] = DateTime.UtcNow
            }
        };

        FrameReceived?.Invoke(this, frame);
    }

    private byte[] GenerateSimulatedFrameData()
    {
        // Generate simulated frame data
        var bytesPerPixel = _settings.ColorFormat == "RGB24" ? 3 : 4;
        var frameSize = _settings.Resolution.Width * _settings.Resolution.Height * bytesPerPixel;
        
        var data = new byte[frameSize];
        var random = new Random(_frameCounter); // Deterministic for same frame number
        
        // Generate a simple gradient pattern with some noise
        for (int y = 0; y < _settings.Resolution.Height; y++)
        {
            for (int x = 0; x < _settings.Resolution.Width; x++)
            {
                var pixelIndex = (y * _settings.Resolution.Width + x) * bytesPerPixel;
                
                // Create a gradient pattern
                var r = (byte)((x * 255) / _settings.Resolution.Width);
                var g = (byte)((y * 255) / _settings.Resolution.Height);
                var b = (byte)((_frameCounter * 10) % 256);
                
                // Add some noise
                r = (byte)Math.Clamp(r + random.Next(-10, 10), 0, 255);
                g = (byte)Math.Clamp(g + random.Next(-10, 10), 0, 255);
                b = (byte)Math.Clamp(b + random.Next(-10, 10), 0, 255);
                
                data[pixelIndex] = r;
                data[pixelIndex + 1] = g;
                data[pixelIndex + 2] = b;
                
                if (bytesPerPixel == 4)
                {
                    data[pixelIndex + 3] = 255; // Alpha
                }
            }
        }
        
        return data;
    }
}
