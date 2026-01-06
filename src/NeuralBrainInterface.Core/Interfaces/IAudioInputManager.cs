using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IAudioInputManager
{
    Task<bool> EnableMicrophoneAsync();
    Task DisableMicrophoneAsync();
    IAudioStream GetAudioStream();
    Task ConfigureAudioSettingsAsync(AudioSettings settings);
    Task<List<string>> GetAvailableDevicesAsync();
    Task<bool> SetInputDeviceAsync(string deviceName);
    
    bool IsEnabled { get; }
    event EventHandler<AudioData>? AudioCaptured;
    event EventHandler<string>? AudioError;
}