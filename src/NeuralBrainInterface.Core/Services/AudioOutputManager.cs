using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Manages speaker output and text-to-speech conversion for AI responses.
/// Handles audio playback, device selection, and voice configuration.
/// </summary>
public class AudioOutputManager : IAudioOutputManager
{
    private bool _isEnabled;
    private VoiceSettings _currentVoiceSettings;
    private string _currentDeviceName;
    private readonly List<string> _availableDevices;
    private readonly object _lock = new();
    private bool _isPlaying;

    public bool IsEnabled => _isEnabled;

    public event EventHandler<string>? AudioPlaybackCompleted;
    public event EventHandler<string>? AudioError;

    public AudioOutputManager()
    {
        _isEnabled = false;
        _isPlaying = false;
        _currentVoiceSettings = new VoiceSettings();
        _currentDeviceName = "Default Speaker";
        _availableDevices = new List<string>
        {
            "Default Speaker",
            "Built-in Speaker",
            "External Speaker",
            "Headphones"
        };
    }

    public async Task<bool> EnableSpeakerAsync()
    {
        try
        {
            lock (_lock)
            {
                if (_isEnabled)
                {
                    return true; // Already enabled
                }

                // Simulate speaker initialization
                // In a real implementation, this would initialize audio output APIs
                _isEnabled = true;
            }

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            AudioError?.Invoke(this, $"Failed to enable speaker: {ex.Message}");
            return false;
        }
    }

    public async Task DisableSpeakerAsync()
    {
        try
        {
            lock (_lock)
            {
                if (!_isEnabled)
                {
                    return; // Already disabled
                }

                // Stop any ongoing playback
                _isPlaying = false;
                _isEnabled = false;
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AudioError?.Invoke(this, $"Failed to disable speaker: {ex.Message}");
        }
    }

    public async Task PlayAudioResponseAsync(AudioData audio)
    {
        try
        {
            if (!_isEnabled)
            {
                AudioError?.Invoke(this, "Speaker is not enabled");
                return;
            }

            if (audio == null || audio.Data.Length == 0)
            {
                AudioError?.Invoke(this, "Invalid audio data");
                return;
            }

            lock (_lock)
            {
                if (_isPlaying)
                {
                    // Queue or skip - for simplicity, we'll skip
                    return;
                }
                _isPlaying = true;
            }

            // Simulate audio playback duration
            var playbackDuration = audio.Duration;
            if (playbackDuration == TimeSpan.Zero)
            {
                // Estimate duration based on data size
                var bytesPerSecond = audio.SampleRate * audio.Channels * (audio.BitDepth / 8);
                playbackDuration = TimeSpan.FromSeconds((double)audio.Data.Length / bytesPerSecond);
            }

            // Simulate playback (in real implementation, this would use audio APIs)
            await Task.Delay(Math.Min((int)playbackDuration.TotalMilliseconds, 5000)); // Cap at 5 seconds for simulation

            lock (_lock)
            {
                _isPlaying = false;
            }

            AudioPlaybackCompleted?.Invoke(this, "Playback completed");
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _isPlaying = false;
            }
            AudioError?.Invoke(this, $"Failed to play audio: {ex.Message}");
        }
    }

    public async Task<AudioData> ConvertTextToSpeechAsync(string text)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return new AudioData
                {
                    Data = Array.Empty<byte>(),
                    SampleRate = 44100,
                    Channels = 1,
                    BitDepth = 16,
                    Duration = TimeSpan.Zero,
                    Format = "PCM"
                };
            }

            // Simulate text-to-speech conversion
            // In a real implementation, this would use TTS APIs (e.g., System.Speech, Azure Cognitive Services)
            
            var wordsPerMinute = 150 * _currentVoiceSettings.SpeechRate;
            var wordCount = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
            var durationSeconds = (wordCount / wordsPerMinute) * 60;
            
            // Generate simulated audio data
            var sampleRate = 44100;
            var channels = 1;
            var bitDepth = 16;
            var bytesPerSecond = sampleRate * channels * (bitDepth / 8);
            var dataSize = (int)(bytesPerSecond * durationSeconds);
            
            var audioData = new byte[Math.Max(dataSize, 1000)]; // Minimum 1000 bytes
            var random = new Random(text.GetHashCode()); // Deterministic for same text
            
            // Generate simulated speech waveform
            for (int i = 0; i < audioData.Length; i += 2)
            {
                // Simulate speech-like waveform
                var sample = (short)(Math.Sin(i * 0.01) * 10000 * _currentVoiceSettings.Volume + random.Next(-1000, 1000));
                audioData[i] = (byte)(sample & 0xFF);
                if (i + 1 < audioData.Length)
                {
                    audioData[i + 1] = (byte)((sample >> 8) & 0xFF);
                }
            }

            return await Task.FromResult(new AudioData
            {
                Data = audioData,
                SampleRate = sampleRate,
                Channels = channels,
                BitDepth = bitDepth,
                Duration = TimeSpan.FromSeconds(durationSeconds),
                Format = "PCM",
                Metadata = new Dictionary<string, object>
                {
                    ["SourceText"] = text,
                    ["VoiceType"] = _currentVoiceSettings.VoiceType,
                    ["SpeechRate"] = _currentVoiceSettings.SpeechRate,
                    ["Language"] = _currentVoiceSettings.Language
                }
            });
        }
        catch (Exception ex)
        {
            AudioError?.Invoke(this, $"Failed to convert text to speech: {ex.Message}");
            return new AudioData
            {
                Data = Array.Empty<byte>(),
                SampleRate = 44100,
                Channels = 1,
                BitDepth = 16,
                Duration = TimeSpan.Zero,
                Format = "PCM"
            };
        }
    }

    public async Task ConfigureVoiceSettingsAsync(VoiceSettings settings)
    {
        try
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            // Validate settings
            if (settings.SpeechRate <= 0 || settings.SpeechRate > 3.0f)
            {
                throw new ArgumentException("Speech rate must be between 0 and 3.0", nameof(settings));
            }

            if (settings.Volume < 0 || settings.Volume > 1.0f)
            {
                throw new ArgumentException("Volume must be between 0 and 1.0", nameof(settings));
            }

            if (settings.Pitch <= 0 || settings.Pitch > 2.0f)
            {
                throw new ArgumentException("Pitch must be between 0 and 2.0", nameof(settings));
            }

            lock (_lock)
            {
                _currentVoiceSettings = new VoiceSettings
                {
                    VoiceType = settings.VoiceType ?? "Default",
                    SpeechRate = settings.SpeechRate,
                    Volume = settings.Volume,
                    Pitch = settings.Pitch,
                    Language = settings.Language ?? "en-US"
                };
            }

            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            AudioError?.Invoke(this, $"Failed to configure voice settings: {ex.Message}");
            throw;
        }
    }

    public async Task<List<string>> GetAvailableOutputDevicesAsync()
    {
        // In a real implementation, this would enumerate actual audio output devices
        return await Task.FromResult(new List<string>(_availableDevices));
    }

    public async Task<bool> SetOutputDeviceAsync(string deviceName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                AudioError?.Invoke(this, "Device name cannot be empty");
                return false;
            }

            if (!_availableDevices.Contains(deviceName))
            {
                AudioError?.Invoke(this, $"Device not found: {deviceName}");
                return false;
            }

            lock (_lock)
            {
                _currentDeviceName = deviceName;
            }

            return await Task.FromResult(true);
        }
        catch (Exception ex)
        {
            AudioError?.Invoke(this, $"Failed to set output device: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets the current voice settings.
    /// </summary>
    public VoiceSettings GetCurrentVoiceSettings()
    {
        lock (_lock)
        {
            return new VoiceSettings
            {
                VoiceType = _currentVoiceSettings.VoiceType,
                SpeechRate = _currentVoiceSettings.SpeechRate,
                Volume = _currentVoiceSettings.Volume,
                Pitch = _currentVoiceSettings.Pitch,
                Language = _currentVoiceSettings.Language
            };
        }
    }

    /// <summary>
    /// Gets the current output device name.
    /// </summary>
    public string GetCurrentDeviceName()
    {
        lock (_lock)
        {
            return _currentDeviceName;
        }
    }

    /// <summary>
    /// Checks if audio is currently playing.
    /// </summary>
    public bool IsPlaying()
    {
        lock (_lock)
        {
            return _isPlaying;
        }
    }
}
