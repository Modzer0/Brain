using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IUIManager
{
    Task InitializeWindowAsync();
    Task HandleTextInputAsync(string input);
    Task HandleFileUploadAsync(byte[] fileData, string fileName);
    Task UpdateMindDisplayAsync(VisualFrame visualFrame);
    Task DisplayResponseAsync(string response);
    
    Task ToggleMicrophoneAsync(bool enabled);
    Task ToggleSpeakerAsync(bool enabled);
    Task ToggleWebcamAsync(bool enabled);
    
    Task UpdateDeviceStatusAsync(DeviceStatus status);
    Task ShowSleepMenuAsync();
    Task ShowWakeMenuAsync();
    Task DisplayTimeContextAsync(TimeInfo timeInfo);
    Task ShowDeviceConfigurationDialogAsync(DeviceType deviceType);
    Task ShowDevicePermissionsDialogAsync();
    Task RefreshDeviceStatusAsync();
    
    event EventHandler<string>? TextInputReceived;
    event EventHandler<(byte[] Data, string FileName)>? FileUploaded;
    event EventHandler<DeviceType>? DeviceToggleRequested;
    event EventHandler? SleepRequested;
    event EventHandler? WakeRequested;
}