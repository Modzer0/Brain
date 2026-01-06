using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Tests.Mocks;

/// <summary>
/// Mock implementation of INeuralCore for testing state persistence
/// </summary>
internal class MockNeuralCore : INeuralCore
{
    private NeuralState _currentState;

    public MockNeuralCore(NeuralState initialState)
    {
        _currentState = initialState;
    }

    public event EventHandler<NeuralState>? StateChanged;
    public event EventHandler<ProcessingResult>? ProcessingCompleted;

    public NeuralState GetCurrentState() => _currentState;
    
    public Task<NeuralState> GetCurrentStateAsync() => Task.FromResult(_currentState);

    public Task<ProcessingResult> ProcessTextAsync(string input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessImageAsync(ImageData input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessVideoAsync(VideoData input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessAudioAsync(AudioData input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessSpreadsheetAsync(SpreadsheetData input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessDocumentAsync(DocumentData input) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessRealtimeAudioAsync(IAudioStream stream) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<ProcessingResult> ProcessRealtimeVideoAsync(IVideoStream stream) => 
        Task.FromResult(new ProcessingResult { Success = true, UpdatedState = _currentState });

    public Task<string> GenerateResponseAsync(string input) => Task.FromResult("Mock response");

    public Task<AudioData> GenerateAudioResponseAsync(string input) => 
        Task.FromResult(new AudioData());

    public Task<bool> SaveStateAsync() => Task.FromResult(true);

    public Task<bool> LoadStateAsync(string checkpoint) => Task.FromResult(true);
    
    public Task<bool> LoadStateAsync(NeuralState state)
    {
        _currentState = state;
        return Task.FromResult(true);
    }

    public void UpdateDeviceContext(Dictionary<DeviceType, bool> deviceStatus) { }

    public void UpdateTimeContext(TimeInfo currentTime) { }

    public Task<bool> EnterSleepModeAsync() => Task.FromResult(true);

    public Task<bool> WakeFromSleepAsync() => Task.FromResult(true);

    public Dictionary<DeviceType, bool> GetDeviceAwareness() => new();

    public Task<bool> StoreMemoryAsync(MemoryItem memory) => Task.FromResult(true);

    public Task<List<MemoryItem>> RecallMemoryAsync(MemoryQuery query) => 
        Task.FromResult(new List<MemoryItem>());

    public Task<List<MemoryItem>> SearchMemoriesAsync(string searchTerms) => 
        Task.FromResult(new List<MemoryItem>());
}