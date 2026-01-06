using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IAudioOutputManager
{
    Task<bool> EnableSpeakerAsync();
    Task DisableSpeakerAsync();
    Task PlayAudioResponseAsync(AudioData audio);
    Task<AudioData> ConvertTextToSpeechAsync(string text);
    Task ConfigureVoiceSettingsAsync(VoiceSettings settings);
    Task<List<string>> GetAvailableOutputDevicesAsync();
    Task<bool> SetOutputDeviceAsync(string deviceName);
    
    bool IsEnabled { get; }
    event EventHandler<string>? AudioPlaybackCompleted;
    event EventHandler<string>? AudioError;
}