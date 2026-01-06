using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IHardwareController
{
    Task<bool> ToggleDeviceAsync(DeviceType deviceType, bool enabled);
    DeviceStatus GetDeviceStatus(DeviceType deviceType);
    Dictionary<DeviceType, DeviceStatus> GetAllDeviceStatus();
    
    Task SaveDevicePreferencesAsync();
    Task LoadDevicePreferencesAsync();
    Task<bool> ValidateDevicePreferencesAsync();
    Task<bool> BackupDevicePreferencesAsync();
    Task<bool> RestoreDevicePreferencesFromBackupAsync(string? backupPath = null);
    
    Task<bool> RequestDevicePermissionsAsync();
    void NotifyNeuralNetwork(Dictionary<DeviceType, bool> deviceChanges);
    
    event EventHandler<DeviceStatus>? DeviceStatusChanged;
    event EventHandler<string>? DeviceError;
}