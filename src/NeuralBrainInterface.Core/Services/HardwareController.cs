using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using System.Text.Json;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Provides unified control over all audio and video input/output devices.
/// Coordinates enable/disable operations, maintains device state persistence,
/// handles device permissions, and notifies the neural network of device state changes.
/// </summary>
public class HardwareController : IHardwareController
{
    private readonly IAudioInputManager _audioInputManager;
    private readonly IAudioOutputManager _audioOutputManager;
    private readonly IVideoInputManager _videoInputManager;
    private readonly INeuralCore _neuralCore;
    
    private readonly Dictionary<DeviceType, DeviceStatus> _deviceStatuses;
    private readonly string _preferencesFilePath;
    private readonly object _statusLock = new();

    public event EventHandler<DeviceStatus>? DeviceStatusChanged;
    public event EventHandler<string>? DeviceError;

    public HardwareController(
        IAudioInputManager audioInputManager,
        IAudioOutputManager audioOutputManager,
        IVideoInputManager videoInputManager,
        INeuralCore neuralCore,
        string? preferencesFilePath = null)
    {
        _audioInputManager = audioInputManager ?? throw new ArgumentNullException(nameof(audioInputManager));
        _audioOutputManager = audioOutputManager ?? throw new ArgumentNullException(nameof(audioOutputManager));
        _videoInputManager = videoInputManager ?? throw new ArgumentNullException(nameof(videoInputManager));
        _neuralCore = neuralCore ?? throw new ArgumentNullException(nameof(neuralCore));
        
        _preferencesFilePath = preferencesFilePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "NeuralBrainInterface",
            "device_preferences.json");

        _deviceStatuses = InitializeDeviceStatuses();
        
        // Subscribe to device events
        SubscribeToDeviceEvents();
    }

    private Dictionary<DeviceType, DeviceStatus> InitializeDeviceStatuses()
    {
        return new Dictionary<DeviceType, DeviceStatus>
        {
            [DeviceType.Microphone] = new DeviceStatus
            {
                DeviceType = DeviceType.Microphone,
                IsEnabled = false,
                IsAvailable = true,
                DeviceName = "Default Microphone",
                PermissionGranted = false
            },
            [DeviceType.Speaker] = new DeviceStatus
            {
                DeviceType = DeviceType.Speaker,
                IsEnabled = false,
                IsAvailable = true,
                DeviceName = "Default Speaker",
                PermissionGranted = true // Speakers typically don't require permission
            },
            [DeviceType.Webcam] = new DeviceStatus
            {
                DeviceType = DeviceType.Webcam,
                IsEnabled = false,
                IsAvailable = true,
                DeviceName = "Default Webcam",
                PermissionGranted = false
            }
        };
    }

    private void SubscribeToDeviceEvents()
    {
        _audioInputManager.AudioError += (sender, error) =>
        {
            HandleDeviceError(DeviceType.Microphone, error);
        };

        _audioOutputManager.AudioError += (sender, error) =>
        {
            HandleDeviceError(DeviceType.Speaker, error);
        };

        _videoInputManager.VideoError += (sender, error) =>
        {
            HandleDeviceError(DeviceType.Webcam, error);
        };
    }

    private void HandleDeviceError(DeviceType deviceType, string error)
    {
        lock (_statusLock)
        {
            if (_deviceStatuses.TryGetValue(deviceType, out var status))
            {
                status.ErrorMessage = error;
                status.IsEnabled = false;
            }
        }
        
        DeviceError?.Invoke(this, $"{deviceType}: {error}");
    }

    public async Task<bool> ToggleDeviceAsync(DeviceType deviceType, bool enabled)
    {
        try
        {
            bool success = false;

            switch (deviceType)
            {
                case DeviceType.Microphone:
                    success = await ToggleMicrophoneAsync(enabled);
                    break;
                case DeviceType.Speaker:
                    success = await ToggleSpeakerAsync(enabled);
                    break;
                case DeviceType.Webcam:
                    success = await ToggleWebcamAsync(enabled);
                    break;
                default:
                    DeviceError?.Invoke(this, $"Unknown device type: {deviceType}");
                    return false;
            }

            if (success)
            {
                UpdateDeviceStatus(deviceType, enabled);
                NotifyNeuralNetwork(new Dictionary<DeviceType, bool> { [deviceType] = enabled });
            }

            return success;
        }
        catch (Exception ex)
        {
            HandleDeviceError(deviceType, ex.Message);
            return false;
        }
    }

    private async Task<bool> ToggleMicrophoneAsync(bool enabled)
    {
        if (enabled)
        {
            var success = await _audioInputManager.EnableMicrophoneAsync();
            if (!success)
            {
                UpdateDeviceStatus(DeviceType.Microphone, false, "Failed to enable microphone");
            }
            return success;
        }
        else
        {
            await _audioInputManager.DisableMicrophoneAsync();
            return true;
        }
    }

    private async Task<bool> ToggleSpeakerAsync(bool enabled)
    {
        if (enabled)
        {
            var success = await _audioOutputManager.EnableSpeakerAsync();
            if (!success)
            {
                UpdateDeviceStatus(DeviceType.Speaker, false, "Failed to enable speaker");
            }
            return success;
        }
        else
        {
            await _audioOutputManager.DisableSpeakerAsync();
            return true;
        }
    }

    private async Task<bool> ToggleWebcamAsync(bool enabled)
    {
        if (enabled)
        {
            var success = await _videoInputManager.EnableWebcamAsync();
            if (!success)
            {
                UpdateDeviceStatus(DeviceType.Webcam, false, "Failed to enable webcam");
            }
            return success;
        }
        else
        {
            await _videoInputManager.DisableWebcamAsync();
            return true;
        }
    }

    private void UpdateDeviceStatus(DeviceType deviceType, bool enabled, string? errorMessage = null)
    {
        lock (_statusLock)
        {
            if (_deviceStatuses.TryGetValue(deviceType, out var status))
            {
                status.IsEnabled = enabled;
                status.ErrorMessage = errorMessage;
                
                DeviceStatusChanged?.Invoke(this, new DeviceStatus
                {
                    DeviceType = status.DeviceType,
                    IsEnabled = status.IsEnabled,
                    IsAvailable = status.IsAvailable,
                    DeviceName = status.DeviceName,
                    PermissionGranted = status.PermissionGranted,
                    ErrorMessage = status.ErrorMessage
                });
            }
        }
    }

    public DeviceStatus GetDeviceStatus(DeviceType deviceType)
    {
        lock (_statusLock)
        {
            if (_deviceStatuses.TryGetValue(deviceType, out var status))
            {
                // Return a copy to prevent external modification
                return new DeviceStatus
                {
                    DeviceType = status.DeviceType,
                    IsEnabled = status.IsEnabled,
                    IsAvailable = status.IsAvailable,
                    DeviceName = status.DeviceName,
                    PermissionGranted = status.PermissionGranted,
                    ErrorMessage = status.ErrorMessage
                };
            }
        }

        return new DeviceStatus
        {
            DeviceType = deviceType,
            IsEnabled = false,
            IsAvailable = false,
            DeviceName = "Unknown",
            PermissionGranted = false,
            ErrorMessage = "Device not found"
        };
    }

    public Dictionary<DeviceType, DeviceStatus> GetAllDeviceStatus()
    {
        lock (_statusLock)
        {
            var result = new Dictionary<DeviceType, DeviceStatus>();
            
            foreach (var (deviceType, status) in _deviceStatuses)
            {
                result[deviceType] = new DeviceStatus
                {
                    DeviceType = status.DeviceType,
                    IsEnabled = status.IsEnabled,
                    IsAvailable = status.IsAvailable,
                    DeviceName = status.DeviceName,
                    PermissionGranted = status.PermissionGranted,
                    ErrorMessage = status.ErrorMessage
                };
            }
            
            return result;
        }
    }

    public async Task SaveDevicePreferencesAsync()
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var preferences = new DevicePreferences
            {
                MicrophoneEnabled = GetDeviceStatus(DeviceType.Microphone).IsEnabled,
                SpeakerEnabled = GetDeviceStatus(DeviceType.Speaker).IsEnabled,
                WebcamEnabled = GetDeviceStatus(DeviceType.Webcam).IsEnabled,
                LastSaved = DateTime.UtcNow
            };

            var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            await File.WriteAllTextAsync(_preferencesFilePath, json);
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Failed to save device preferences: {ex.Message}");
        }
    }

    public async Task LoadDevicePreferencesAsync()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return;
            }

            var json = await File.ReadAllTextAsync(_preferencesFilePath);
            var preferences = JsonSerializer.Deserialize<DevicePreferences>(json);

            if (preferences != null)
            {
                // Apply saved preferences
                if (preferences.MicrophoneEnabled)
                {
                    await ToggleDeviceAsync(DeviceType.Microphone, true);
                }
                
                if (preferences.SpeakerEnabled)
                {
                    await ToggleDeviceAsync(DeviceType.Speaker, true);
                }
                
                if (preferences.WebcamEnabled)
                {
                    await ToggleDeviceAsync(DeviceType.Webcam, true);
                }
            }
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Failed to load device preferences: {ex.Message}");
        }
    }

    /// <summary>
    /// Validates device preferences file integrity
    /// </summary>
    public async Task<bool> ValidateDevicePreferencesAsync()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return true; // No preferences file is valid (will use defaults)
            }

            var json = await File.ReadAllTextAsync(_preferencesFilePath);
            var preferences = JsonSerializer.Deserialize<DevicePreferences>(json);
            
            return preferences != null;
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Device preferences validation failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Creates a backup of current device preferences
    /// </summary>
    public async Task<bool> BackupDevicePreferencesAsync()
    {
        try
        {
            if (!File.Exists(_preferencesFilePath))
            {
                return true; // Nothing to backup
            }

            var backupPath = _preferencesFilePath + $".backup.{DateTime.UtcNow:yyyyMMddHHmmss}";
            File.Copy(_preferencesFilePath, backupPath);
            
            return true;
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Failed to backup device preferences: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores device preferences from backup
    /// </summary>
    public async Task<bool> RestoreDevicePreferencesFromBackupAsync(string? backupPath = null)
    {
        try
        {
            if (backupPath == null)
            {
                // Find the most recent backup
                var directory = Path.GetDirectoryName(_preferencesFilePath);
                if (directory == null) return false;
                
                var backupFiles = Directory.GetFiles(directory, "device_preferences.json.backup.*")
                    .OrderByDescending(f => File.GetCreationTime(f))
                    .FirstOrDefault();
                
                if (backupFiles == null)
                {
                    DeviceError?.Invoke(this, "No backup files found");
                    return false;
                }
                
                backupPath = backupFiles;
            }

            if (!File.Exists(backupPath))
            {
                DeviceError?.Invoke(this, $"Backup file not found: {backupPath}");
                return false;
            }

            // Validate backup before restoring
            var json = await File.ReadAllTextAsync(backupPath);
            var preferences = JsonSerializer.Deserialize<DevicePreferences>(json);
            
            if (preferences == null)
            {
                DeviceError?.Invoke(this, "Invalid backup file format");
                return false;
            }

            // Create backup of current preferences before restoring
            await BackupDevicePreferencesAsync();
            
            // Restore from backup
            File.Copy(backupPath, _preferencesFilePath, true);
            await LoadDevicePreferencesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Failed to restore device preferences from backup: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RequestDevicePermissionsAsync()
    {
        var allPermissionsGranted = true;

        try
        {
            // Request microphone permission
            var micPermission = await RequestMicrophonePermissionAsync();
            UpdatePermissionStatus(DeviceType.Microphone, micPermission);
            allPermissionsGranted &= micPermission;

            // Request webcam permission
            var webcamPermission = await RequestWebcamPermissionAsync();
            UpdatePermissionStatus(DeviceType.Webcam, webcamPermission);
            allPermissionsGranted &= webcamPermission;

            // Speaker typically doesn't require explicit permission
            UpdatePermissionStatus(DeviceType.Speaker, true);

            return allPermissionsGranted;
        }
        catch (Exception ex)
        {
            DeviceError?.Invoke(this, $"Failed to request device permissions: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> RequestMicrophonePermissionAsync()
    {
        try
        {
            // Attempt to enable microphone to check permission
            var success = await _audioInputManager.EnableMicrophoneAsync();
            if (success)
            {
                await _audioInputManager.DisableMicrophoneAsync();
            }
            return success;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> RequestWebcamPermissionAsync()
    {
        try
        {
            // Attempt to enable webcam to check permission
            var success = await _videoInputManager.EnableWebcamAsync();
            if (success)
            {
                await _videoInputManager.DisableWebcamAsync();
            }
            return success;
        }
        catch
        {
            return false;
        }
    }

    private void UpdatePermissionStatus(DeviceType deviceType, bool granted)
    {
        lock (_statusLock)
        {
            if (_deviceStatuses.TryGetValue(deviceType, out var status))
            {
                status.PermissionGranted = granted;
                if (!granted)
                {
                    status.ErrorMessage = "Permission denied";
                    status.IsAvailable = false;
                }
            }
        }
    }

    public void NotifyNeuralNetwork(Dictionary<DeviceType, bool> deviceChanges)
    {
        // Get current state of all devices
        var allDeviceStates = new Dictionary<DeviceType, bool>();
        
        lock (_statusLock)
        {
            foreach (var (deviceType, status) in _deviceStatuses)
            {
                allDeviceStates[deviceType] = status.IsEnabled;
            }
        }

        // Apply the changes
        foreach (var (deviceType, enabled) in deviceChanges)
        {
            allDeviceStates[deviceType] = enabled;
        }

        // Notify the neural network
        _neuralCore.UpdateDeviceContext(allDeviceStates);
    }

    /// <summary>
    /// Synchronizes the hardware controller's device status with the actual device managers.
    /// </summary>
    public void SynchronizeDeviceStatus()
    {
        lock (_statusLock)
        {
            _deviceStatuses[DeviceType.Microphone].IsEnabled = _audioInputManager.IsEnabled;
            _deviceStatuses[DeviceType.Speaker].IsEnabled = _audioOutputManager.IsEnabled;
            _deviceStatuses[DeviceType.Webcam].IsEnabled = _videoInputManager.IsEnabled;
        }
    }

    /// <summary>
    /// Disables all devices and releases resources.
    /// </summary>
    public async Task DisableAllDevicesAsync()
    {
        await ToggleDeviceAsync(DeviceType.Microphone, false);
        await ToggleDeviceAsync(DeviceType.Speaker, false);
        await ToggleDeviceAsync(DeviceType.Webcam, false);
    }
}

/// <summary>
/// Represents saved device preferences for persistence across sessions.
/// </summary>
internal class DevicePreferences
{
    public bool MicrophoneEnabled { get; set; }
    public bool SpeakerEnabled { get; set; }
    public bool WebcamEnabled { get; set; }
    public DateTime LastSaved { get; set; }
}
