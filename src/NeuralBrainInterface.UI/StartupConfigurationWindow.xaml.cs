using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.UI;

public partial class StartupConfigurationWindow : Window
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<StartupConfigurationWindow> _logger;
    private readonly IResourceManager _resourceManager;
    private readonly IHardwareController _hardwareController;
    private readonly IFileFormatManager _fileFormatManager;
    
    private ResourceConfig _currentResourceConfig;
    private bool _configurationChanged = false;

    public StartupConfigurationWindow(IServiceProvider serviceProvider)
    {
        InitializeComponent();
        
        _serviceProvider = serviceProvider;
        _logger = serviceProvider.GetRequiredService<ILogger<StartupConfigurationWindow>>();
        _resourceManager = serviceProvider.GetRequiredService<IResourceManager>();
        _hardwareController = serviceProvider.GetRequiredService<IHardwareController>();
        _fileFormatManager = serviceProvider.GetRequiredService<IFileFormatManager>();
        
        _currentResourceConfig = new ResourceConfig
        {
            ActiveMemoryMb = 2048,
            CpuCores = 4,
            GpuCores = 1,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 60
        };
        
        Loaded += StartupConfigurationWindow_Loaded;
    }

    private async void StartupConfigurationWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await LoadSystemInformationAsync();
            await LoadSupportedFormatsAsync();
            UpdateResourceSliders();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading startup configuration window");
            MessageBox.Show($"Error loading configuration: {ex.Message}", "Configuration Error", 
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async Task LoadSystemInformationAsync()
    {
        try
        {
            var availableResources = _resourceManager.GetAvailableResources();
            
            AvailableResourcesText.Text = $"Memory: {availableResources.TotalMemoryBytes / (1024 * 1024):N0} MB, " +
                                         $"CPU Cores: {availableResources.TotalCpuCores}, " +
                                         $"GPU Cores: {availableResources.TotalGpuCores}";
            
            // Update slider maximums based on available resources
            MemorySlider.Maximum = Math.Min(availableResources.TotalMemoryBytes / (1024 * 1024) * 0.8, 16384); // Max 80% of available memory
            CpuSlider.Maximum = Math.Max(1, availableResources.TotalCpuCores - 1); // Leave at least 1 core for system
            GpuSlider.Maximum = availableResources.TotalGpuCores;
            
            _logger.LogInformation($"System resources detected: {availableResources.TotalMemoryBytes / (1024 * 1024)}MB RAM, {availableResources.TotalCpuCores} CPU cores, {availableResources.TotalGpuCores} GPU cores");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading system information");
            AvailableResourcesText.Text = "Error detecting system resources";
        }
    }

    private async Task LoadSupportedFormatsAsync()
    {
        try
        {
            var supportedFormats = _fileFormatManager.GetSupportedFormats();
            var formatText = "";
            
            foreach (var formatGroup in supportedFormats)
            {
                formatText += $"{formatGroup.Key}: {string.Join(", ", formatGroup.Value)}\n";
            }
            
            if (string.IsNullOrEmpty(formatText))
            {
                formatText = "No supported formats detected";
            }
            
            SupportedFormatsText.Text = formatText.Trim();
            
            _logger.LogInformation($"Loaded {supportedFormats.Count} format groups");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading supported formats");
            SupportedFormatsText.Text = "Error loading format information";
        }
    }

    private void UpdateResourceSliders()
    {
        MemorySlider.Value = _currentResourceConfig.ActiveMemoryMb;
        CpuSlider.Value = _currentResourceConfig.CpuCores;
        GpuSlider.Value = _currentResourceConfig.GpuCores;
        FpsSlider.Value = _currentResourceConfig.VisualizationFps;
        ProcessingTimeSlider.Value = _currentResourceConfig.MaxProcessingTimeMs;
        
        UpdateValueDisplays();
    }

    private void UpdateValueDisplays()
    {
        MemoryValueText.Text = $"{(int)MemorySlider.Value} MB";
        CpuValueText.Text = $"{(int)CpuSlider.Value} cores";
        GpuValueText.Text = $"{(int)GpuSlider.Value} core{((int)GpuSlider.Value != 1 ? "s" : "")}";
        FpsValueText.Text = $"{(int)FpsSlider.Value} FPS";
        ProcessingTimeValueText.Text = $"{(int)ProcessingTimeSlider.Value} ms";
    }

    private void MemorySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (MemoryValueText != null)
        {
            MemoryValueText.Text = $"{(int)e.NewValue} MB";
            _currentResourceConfig.ActiveMemoryMb = (int)e.NewValue;
            _configurationChanged = true;
        }
    }

    private void CpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (CpuValueText != null)
        {
            CpuValueText.Text = $"{(int)e.NewValue} cores";
            _currentResourceConfig.CpuCores = (int)e.NewValue;
            _configurationChanged = true;
        }
    }

    private void GpuSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (GpuValueText != null)
        {
            var value = (int)e.NewValue;
            GpuValueText.Text = $"{value} core{(value != 1 ? "s" : "")}";
            _currentResourceConfig.GpuCores = value;
            _configurationChanged = true;
        }
    }

    private void FpsSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FpsValueText != null)
        {
            FpsValueText.Text = $"{(int)e.NewValue} FPS";
            _currentResourceConfig.VisualizationFps = (int)e.NewValue;
            _configurationChanged = true;
        }
    }

    private void ProcessingTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ProcessingTimeValueText != null)
        {
            ProcessingTimeValueText.Text = $"{(int)e.NewValue} ms";
            _currentResourceConfig.MaxProcessingTimeMs = (int)e.NewValue;
            _configurationChanged = true;
        }
    }

    private async void RequestPermissionsButton_Click(object sender, RoutedEventArgs e)
    {
        RequestPermissionsButton.IsEnabled = false;
        RequestPermissionsButton.Content = "Requesting permissions...";
        
        try
        {
            var permissionsGranted = await _hardwareController.RequestDevicePermissionsAsync();
            
            if (permissionsGranted)
            {
                MessageBox.Show("Device permissions granted successfully!", "Permissions", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Some device permissions were not granted. You can still use the application with limited functionality.", 
                    "Permissions", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error requesting device permissions");
            MessageBox.Show($"Error requesting permissions: {ex.Message}", "Permission Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RequestPermissionsButton.Content = "Request Permissions Now";
            RequestPermissionsButton.IsEnabled = true;
        }
    }

    private void BrowseDataDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForDirectory(DataDirectoryTextBox, "Select Data Directory");
    }

    private void BrowseBrainFilesDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForDirectory(BrainFilesDirectoryTextBox, "Select Brain Files Directory");
    }

    private void BrowseMemoryDirectoryButton_Click(object sender, RoutedEventArgs e)
    {
        BrowseForDirectory(MemoryDirectoryTextBox, "Select Memory Directory");
    }

    private void BrowseForDirectory(TextBox textBox, string description)
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = description,
            SelectedPath = textBox.Text,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            textBox.Text = dialog.SelectedPath;
            _configurationChanged = true;
        }
    }

    private void ResetToDefaultsButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show("Reset all settings to default values?", "Reset Configuration", 
            MessageBoxButton.YesNo, MessageBoxImage.Question);
        
        if (result == MessageBoxResult.Yes)
        {
            ResetToDefaults();
        }
    }

    private void ResetToDefaults()
    {
        // Reset resource configuration
        _currentResourceConfig = new ResourceConfig
        {
            ActiveMemoryMb = 2048,
            CpuCores = 4,
            GpuCores = 1,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 60
        };
        
        UpdateResourceSliders();
        
        // Reset device settings
        EnableMicrophoneCheckBox.IsChecked = false;
        EnableSpeakerCheckBox.IsChecked = true;
        EnableWebcamCheckBox.IsChecked = false;
        AudioQualityComboBox.SelectedIndex = 1;
        VideoResolutionComboBox.SelectedIndex = 1;
        VideoFrameRateComboBox.SelectedIndex = 1;
        NoiseReductionCheckBox.IsChecked = true;
        
        // Reset file settings
        ValidateFileIntegrityCheckBox.IsChecked = true;
        EnableFormatConversionCheckBox.IsChecked = true;
        ShowFormatWarningsCheckBox.IsChecked = true;
        DataDirectoryTextBox.Text = "Data";
        BrainFilesDirectoryTextBox.Text = "BrainFiles";
        MemoryDirectoryTextBox.Text = "Memory";
        
        // Reset privacy settings
        SaveDevicePreferencesCheckBox.IsChecked = true;
        ShowDeviceIndicatorsCheckBox.IsChecked = true;
        
        _configurationChanged = true;
    }

    private async void SaveConfigurationButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await SaveConfigurationAsync();
            MessageBox.Show("Configuration saved successfully!", "Configuration", 
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
            MessageBox.Show($"Error saving configuration: {ex.Message}", "Configuration Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async Task SaveConfigurationAsync()
    {
        try
        {
            _logger.LogInformation("Saving startup configuration...");
            
            // Validate configuration before saving
            var validationResult = await ValidateConfigurationAsync();
            if (!validationResult.IsValid)
            {
                var errorMessage = "Configuration validation failed:\n" + string.Join("\n", validationResult.Errors);
                throw new InvalidOperationException(errorMessage);
            }
            
            // Create directories if they don't exist
            var directories = new[] { DataDirectoryTextBox.Text, BrainFilesDirectoryTextBox.Text, MemoryDirectoryTextBox.Text };
            foreach (var directory in directories)
            {
                if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                    _logger.LogDebug($"Created directory: {directory}");
                }
            }
            
            // Configure resources with validation
            var resourceConfigured = await _resourceManager.ConfigureResourcesAsync(_currentResourceConfig);
            if (!resourceConfigured)
            {
                // Try with reduced settings
                var fallbackConfig = CreateFallbackResourceConfig();
                resourceConfigured = await _resourceManager.ConfigureResourcesAsync(fallbackConfig);
                
                if (!resourceConfigured)
                {
                    throw new InvalidOperationException("Failed to configure system resources even with reduced settings");
                }
                else
                {
                    _logger.LogWarning("Using fallback resource configuration due to system limitations");
                    _currentResourceConfig = fallbackConfig;
                    UpdateResourceSliders(); // Update UI to reflect fallback values
                }
            }
            
            _logger.LogInformation("Configuration saved successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving configuration");
            throw;
        }
    }

    /// <summary>
    /// Validate the current configuration
    /// </summary>
    private async Task<ConfigurationValidationResult> ValidateConfigurationAsync()
    {
        var result = new ConfigurationValidationResult { IsValid = true };
        
        try
        {
            // Validate resource configuration
            var availableResources = _resourceManager.GetAvailableResources();
            
            if (_currentResourceConfig.ActiveMemoryMb > availableResources.TotalMemoryBytes / (1024 * 1024) * 0.9)
            {
                result.Errors.Add($"Requested memory ({_currentResourceConfig.ActiveMemoryMb}MB) exceeds 90% of available system memory");
                result.IsValid = false;
            }
            
            if (_currentResourceConfig.CpuCores > availableResources.TotalCpuCores)
            {
                result.Errors.Add($"Requested CPU cores ({_currentResourceConfig.CpuCores}) exceeds available CPU cores ({availableResources.TotalCpuCores})");
                result.IsValid = false;
            }
            
            if (_currentResourceConfig.GpuCores > availableResources.TotalGpuCores)
            {
                result.Errors.Add($"Requested GPU cores ({_currentResourceConfig.GpuCores}) exceeds available GPU cores ({availableResources.TotalGpuCores})");
                result.IsValid = false;
            }
            
            // Validate directory paths
            var directories = new[] 
            { 
                ("Data Directory", DataDirectoryTextBox.Text),
                ("Brain Files Directory", BrainFilesDirectoryTextBox.Text),
                ("Memory Directory", MemoryDirectoryTextBox.Text)
            };
            
            foreach (var (name, path) in directories)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    result.Errors.Add($"{name} cannot be empty");
                    result.IsValid = false;
                }
                else if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                {
                    result.Errors.Add($"{name} contains invalid characters");
                    result.IsValid = false;
                }
            }
            
            // Validate performance settings
            if (_currentResourceConfig.VisualizationFps < 15 || _currentResourceConfig.VisualizationFps > 120)
            {
                result.Warnings.Add("Visualization frame rate outside recommended range (15-120 FPS)");
            }
            
            if (_currentResourceConfig.MaxProcessingTimeMs < 1000 || _currentResourceConfig.MaxProcessingTimeMs > 30000)
            {
                result.Warnings.Add("Processing timeout outside recommended range (1-30 seconds)");
            }
            
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            result.Errors.Add($"Configuration validation error: {ex.Message}");
            result.IsValid = false;
        }
        
        return result;
    }

    /// <summary>
    /// Create a fallback resource configuration with reduced requirements
    /// </summary>
    private ResourceConfig CreateFallbackResourceConfig()
    {
        var availableResources = _resourceManager.GetAvailableResources();
        
        return new ResourceConfig
        {
            ActiveMemoryMb = Math.Min(_currentResourceConfig.ActiveMemoryMb, (int)(availableResources.TotalMemoryBytes / (1024 * 1024) / 4)), // Use 25% of available memory
            CpuCores = Math.Min(_currentResourceConfig.CpuCores, Math.Max(1, availableResources.TotalCpuCores / 2)), // Use half of available CPU cores
            GpuCores = Math.Min(_currentResourceConfig.GpuCores, availableResources.TotalGpuCores), // Use available GPU cores
            MaxProcessingTimeMs = Math.Max(_currentResourceConfig.MaxProcessingTimeMs, 10000), // Increase timeout for lower-end systems
            VisualizationFps = Math.Min(_currentResourceConfig.VisualizationFps, 30) // Reduce FPS for better performance
        };
    }

    /// <summary>
    /// Configuration validation result
    /// </summary>
    private class ConfigurationValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        if (_configurationChanged)
        {
            var result = MessageBox.Show("You have unsaved changes. Are you sure you want to cancel?", 
                "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Question);
            
            if (result == MessageBoxResult.No)
            {
                return;
            }
        }
        
        DialogResult = false;
        Close();
    }

    private async void StartApplicationButton_Click(object sender, RoutedEventArgs e)
    {
        StartApplicationButton.IsEnabled = false;
        StartApplicationButton.Content = "Starting...";
        
        try
        {
            // Save configuration first
            await SaveConfigurationAsync();
            
            // Apply device settings
            await ApplyDeviceSettingsAsync();
            
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting application");
            MessageBox.Show($"Error starting application: {ex.Message}", "Startup Error", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            StartApplicationButton.Content = "Start Application";
            StartApplicationButton.IsEnabled = true;
        }
    }

    private async Task ApplyDeviceSettingsAsync()
    {
        try
        {
            _logger.LogInformation("Applying device settings...");
            
            var settingsApplied = 0;
            var settingsFailed = 0;
            
            // Apply device enable/disable settings with individual error handling
            if (EnableMicrophoneCheckBox.IsChecked == true)
            {
                try
                {
                    var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Microphone, true);
                    if (success)
                    {
                        settingsApplied++;
                        _logger.LogDebug("Microphone enabled successfully");
                    }
                    else
                    {
                        settingsFailed++;
                        _logger.LogWarning("Failed to enable microphone");
                    }
                }
                catch (Exception ex)
                {
                    settingsFailed++;
                    _logger.LogError(ex, "Error enabling microphone");
                }
            }
            
            if (EnableSpeakerCheckBox.IsChecked == true)
            {
                try
                {
                    var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Speaker, true);
                    if (success)
                    {
                        settingsApplied++;
                        _logger.LogDebug("Speaker enabled successfully");
                    }
                    else
                    {
                        settingsFailed++;
                        _logger.LogWarning("Failed to enable speaker");
                    }
                }
                catch (Exception ex)
                {
                    settingsFailed++;
                    _logger.LogError(ex, "Error enabling speaker");
                }
            }
            
            if (EnableWebcamCheckBox.IsChecked == true)
            {
                try
                {
                    var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Webcam, true);
                    if (success)
                    {
                        settingsApplied++;
                        _logger.LogDebug("Webcam enabled successfully");
                    }
                    else
                    {
                        settingsFailed++;
                        _logger.LogWarning("Failed to enable webcam");
                    }
                }
                catch (Exception ex)
                {
                    settingsFailed++;
                    _logger.LogError(ex, "Error enabling webcam");
                }
            }
            
            // Save device preferences if requested
            if (SaveDevicePreferencesCheckBox.IsChecked == true)
            {
                try
                {
                    await _hardwareController.SaveDevicePreferencesAsync();
                    _logger.LogDebug("Device preferences saved successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error saving device preferences");
                    throw new InvalidOperationException("Failed to save device preferences", ex);
                }
            }
            
            _logger.LogInformation($"Device settings applied: {settingsApplied} successful, {settingsFailed} failed");
            
            if (settingsFailed > 0 && settingsApplied == 0)
            {
                throw new InvalidOperationException("All device settings failed to apply");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error applying device settings");
            throw;
        }
    }

    public ResourceConfig GetResourceConfiguration()
    {
        return _currentResourceConfig;
    }

    public bool GetDeviceSetting(DeviceType deviceType)
    {
        return deviceType switch
        {
            DeviceType.Microphone => EnableMicrophoneCheckBox.IsChecked == true,
            DeviceType.Speaker => EnableSpeakerCheckBox.IsChecked == true,
            DeviceType.Webcam => EnableWebcamCheckBox.IsChecked == true,
            _ => false
        };
    }
}