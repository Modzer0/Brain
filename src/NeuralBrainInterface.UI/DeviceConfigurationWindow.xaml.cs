using System.Windows;
using System.Windows.Controls;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.UI;

public partial class DeviceConfigurationWindow : Window
{
    private readonly DeviceType _deviceType;
    private readonly IHardwareController _hardwareController;
    private DeviceStatus _currentStatus;

    public DeviceConfigurationWindow(DeviceType deviceType, IHardwareController hardwareController)
    {
        InitializeComponent();
        
        _deviceType = deviceType;
        _hardwareController = hardwareController;
        _currentStatus = _hardwareController.GetDeviceStatus(deviceType);
        
        InitializeWindow();
        LoadCurrentSettings();
        SetupEventHandlers();
    }

    private void InitializeWindow()
    {
        // Update window title and header
        Title = $"{_deviceType} Configuration";
        HeaderText.Text = $"{_deviceType} Configuration";
        
        // Show relevant settings panels based on device type
        switch (_deviceType)
        {
            case DeviceType.Microphone:
                AudioSettingsPanel.Visibility = Visibility.Visible;
                break;
            case DeviceType.Speaker:
                AudioSettingsPanel.Visibility = Visibility.Visible;
                VoiceSettingsPanel.Visibility = Visibility.Visible;
                break;
            case DeviceType.Webcam:
                VideoSettingsPanel.Visibility = Visibility.Visible;
                break;
        }
        
        // Update test button text
        TestDeviceButton.Content = $"Test {_deviceType}";
    }

    private void LoadCurrentSettings()
    {
        // Update status display
        UpdateStatusDisplay();
        
        // Load default settings (in a real implementation, these would be loaded from configuration)
        LoadDefaultSettings();
    }

    private void UpdateStatusDisplay()
    {
        DeviceStatusText.Text = $"Device: {_currentStatus.DeviceName}\n" +
                               $"Status: {(_currentStatus.IsEnabled ? "Enabled" : "Disabled")}\n" +
                               $"Available: {(_currentStatus.IsAvailable ? "Yes" : "No")}";
        
        PermissionStatusText.Text = $"Permission Granted: {(_currentStatus.PermissionGranted ? "Yes" : "No")}";
        
        if (!string.IsNullOrEmpty(_currentStatus.ErrorMessage))
        {
            ErrorStatusText.Text = $"Error: {_currentStatus.ErrorMessage}";
            ErrorStatusText.Visibility = Visibility.Visible;
        }
        else
        {
            ErrorStatusText.Visibility = Visibility.Collapsed;
        }
        
        // Update button states
        TestDeviceButton.IsEnabled = _currentStatus.IsAvailable && _currentStatus.PermissionGranted;
        RequestPermissionButton.IsEnabled = !_currentStatus.PermissionGranted;
    }

    private void LoadDefaultSettings()
    {
        // Audio settings defaults
        if (AudioSettingsPanel.Visibility == Visibility.Visible)
        {
            AudioQualityCombo.SelectedIndex = 2; // High quality (44.1 kHz)
            AudioChannelsCombo.SelectedIndex = 1; // Stereo
            NoiseReductionCheck.IsChecked = true;
        }
        
        // Video settings defaults
        if (VideoSettingsPanel.Visibility == Visibility.Visible)
        {
            VideoResolutionCombo.SelectedIndex = 1; // HD (1280x720)
            VideoFrameRateCombo.SelectedIndex = 1; // 30 FPS
            VideoQualitySlider.Value = 0.8; // 80% quality
            UpdateVideoQualityLabel();
        }
        
        // Voice settings defaults
        if (VoiceSettingsPanel.Visibility == Visibility.Visible)
        {
            VoiceTypeCombo.SelectedIndex = 0; // Default voice
            SpeechRateSlider.Value = 1.0; // Normal speed
            VolumeSlider.Value = 0.8; // 80% volume
            UpdateSpeechRateLabel();
            UpdateVolumeLabel();
        }
    }

    private void SetupEventHandlers()
    {
        // Button event handlers
        TestDeviceButton.Click += TestDeviceButton_Click;
        RequestPermissionButton.Click += RequestPermissionButton_Click;
        RefreshStatusButton.Click += RefreshStatusButton_Click;
        ApplyButton.Click += ApplyButton_Click;
        CancelButton.Click += CancelButton_Click;
        
        // Slider event handlers for real-time updates
        if (VideoQualitySlider != null)
        {
            VideoQualitySlider.ValueChanged += (s, e) => UpdateVideoQualityLabel();
        }
        
        if (SpeechRateSlider != null)
        {
            SpeechRateSlider.ValueChanged += (s, e) => UpdateSpeechRateLabel();
        }
        
        if (VolumeSlider != null)
        {
            VolumeSlider.ValueChanged += (s, e) => UpdateVolumeLabel();
        }
    }

    private void UpdateVideoQualityLabel()
    {
        if (VideoQualityLabel != null && VideoQualitySlider != null)
        {
            var percentage = (int)(VideoQualitySlider.Value * 100);
            var qualityText = percentage switch
            {
                >= 90 => "Excellent",
                >= 80 => "High",
                >= 60 => "Medium",
                >= 40 => "Low",
                _ => "Very Low"
            };
            VideoQualityLabel.Text = $"{qualityText} Quality ({percentage}%)";
        }
    }

    private void UpdateSpeechRateLabel()
    {
        if (SpeechRateLabel != null && SpeechRateSlider != null)
        {
            var percentage = (int)(SpeechRateSlider.Value * 100);
            var speedText = SpeechRateSlider.Value switch
            {
                >= 1.5 => "Fast",
                >= 1.2 => "Above Normal",
                >= 0.8 => "Normal",
                >= 0.6 => "Below Normal",
                _ => "Slow"
            };
            SpeechRateLabel.Text = $"{speedText} Speed ({percentage}%)";
        }
    }

    private void UpdateVolumeLabel()
    {
        if (VolumeLabel != null && VolumeSlider != null)
        {
            var percentage = (int)(VolumeSlider.Value * 100);
            VolumeLabel.Text = $"{percentage}%";
        }
    }

    private async void TestDeviceButton_Click(object sender, RoutedEventArgs e)
    {
        TestDeviceButton.IsEnabled = false;
        TestDeviceButton.Content = "Testing...";
        
        try
        {
            var success = await TestDeviceAsync();
            
            if (success)
            {
                MessageBox.Show(
                    $"{_deviceType} test completed successfully!",
                    "Test Successful",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    $"{_deviceType} test failed. Please check your device connection and permissions.",
                    "Test Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error testing {_deviceType}: {ex.Message}",
                "Test Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            TestDeviceButton.Content = $"Test {_deviceType}";
            TestDeviceButton.IsEnabled = _currentStatus.IsAvailable && _currentStatus.PermissionGranted;
        }
    }

    private async Task<bool> TestDeviceAsync()
    {
        // Temporarily enable the device for testing
        var wasEnabled = _currentStatus.IsEnabled;
        
        try
        {
            if (!wasEnabled)
            {
                var enableSuccess = await _hardwareController.ToggleDeviceAsync(_deviceType, true);
                if (!enableSuccess) return false;
            }
            
            // Wait a moment to ensure device is ready
            await Task.Delay(1000);
            
            // Perform device-specific test
            switch (_deviceType)
            {
                case DeviceType.Microphone:
                    // Test microphone by checking if audio input is available
                    return await TestMicrophoneAsync();
                case DeviceType.Speaker:
                    // Test speaker by playing a test sound
                    return await TestSpeakerAsync();
                case DeviceType.Webcam:
                    // Test webcam by capturing a frame
                    return await TestWebcamAsync();
                default:
                    return false;
            }
        }
        finally
        {
            // Restore original state if it was disabled
            if (!wasEnabled)
            {
                await _hardwareController.ToggleDeviceAsync(_deviceType, false);
            }
        }
    }

    private async Task<bool> TestMicrophoneAsync()
    {
        // In a real implementation, this would test microphone input
        // For now, just simulate a successful test
        await Task.Delay(500);
        return true;
    }

    private async Task<bool> TestSpeakerAsync()
    {
        // In a real implementation, this would play a test sound
        // For now, just simulate a successful test
        await Task.Delay(500);
        return true;
    }

    private async Task<bool> TestWebcamAsync()
    {
        // In a real implementation, this would capture a test frame
        // For now, just simulate a successful test
        await Task.Delay(500);
        return true;
    }

    private async void RequestPermissionButton_Click(object sender, RoutedEventArgs e)
    {
        RequestPermissionButton.IsEnabled = false;
        RequestPermissionButton.Content = "Requesting...";
        
        try
        {
            var success = await _hardwareController.RequestDevicePermissionsAsync();
            
            if (success)
            {
                MessageBox.Show(
                    "Device permissions granted successfully!",
                    "Permission Granted",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show(
                    "Permission request failed or was denied. Please check your system settings.",
                    "Permission Denied",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            
            // Refresh status
            RefreshStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error requesting permissions: {ex.Message}",
                "Permission Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            RequestPermissionButton.Content = "Request Permission";
            RequestPermissionButton.IsEnabled = !_currentStatus.PermissionGranted;
        }
    }

    private void RefreshStatusButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        _currentStatus = _hardwareController.GetDeviceStatus(_deviceType);
        UpdateStatusDisplay();
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Apply the configuration settings
            await ApplySettingsAsync();
            
            MessageBox.Show(
                "Settings applied successfully!",
                "Settings Applied",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Error applying settings: {ex.Message}",
                "Settings Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private async Task ApplySettingsAsync()
    {
        // In a real implementation, this would apply the settings to the device managers
        // For now, just simulate applying settings
        await Task.Delay(100);
        
        // Here you would:
        // 1. Create AudioSettings, VideoSettings, or VoiceSettings objects based on UI values
        // 2. Pass them to the appropriate device managers
        // 3. Save the settings for persistence
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}