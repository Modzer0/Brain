using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IVideoInputManager
{
    Task<bool> EnableWebcamAsync();
    Task DisableWebcamAsync();
    IVideoStream GetVideoStream();
    Task ConfigureVideoSettingsAsync(VideoSettings settings);
    Task<List<string>> GetAvailableCamerasAsync();
    Task<bool> SetCameraDeviceAsync(string deviceName);
    
    bool IsEnabled { get; }
    event EventHandler<ImageData>? FrameCaptured;
    event EventHandler<string>? VideoError;
}