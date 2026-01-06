using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface INeuralCore
{
    Task<ProcessingResult> ProcessTextAsync(string input);
    Task<ProcessingResult> ProcessImageAsync(ImageData input);
    Task<ProcessingResult> ProcessVideoAsync(VideoData input);
    Task<ProcessingResult> ProcessAudioAsync(AudioData input);
    Task<ProcessingResult> ProcessSpreadsheetAsync(SpreadsheetData input);
    Task<ProcessingResult> ProcessDocumentAsync(DocumentData input);
    Task<ProcessingResult> ProcessRealtimeAudioAsync(IAudioStream stream);
    Task<ProcessingResult> ProcessRealtimeVideoAsync(IVideoStream stream);
    
    NeuralState GetCurrentState();
    Task<NeuralState> GetCurrentStateAsync();
    Task<string> GenerateResponseAsync(string input);
    Task<AudioData> GenerateAudioResponseAsync(string input);
    
    Task<bool> SaveStateAsync();
    Task<bool> LoadStateAsync(string checkpoint);
    Task<bool> LoadStateAsync(NeuralState state);
    
    void UpdateDeviceContext(Dictionary<DeviceType, bool> deviceStatus);
    void UpdateTimeContext(TimeInfo currentTime);
    
    Task<bool> EnterSleepModeAsync();
    Task<bool> WakeFromSleepAsync();
    
    Dictionary<DeviceType, bool> GetDeviceAwareness();
    
    Task<bool> StoreMemoryAsync(MemoryItem memory);
    Task<List<MemoryItem>> RecallMemoryAsync(MemoryQuery query);
    Task<List<MemoryItem>> SearchMemoriesAsync(string searchTerms);
    
    event EventHandler<NeuralState>? StateChanged;
    event EventHandler<ProcessingResult>? ProcessingCompleted;
}