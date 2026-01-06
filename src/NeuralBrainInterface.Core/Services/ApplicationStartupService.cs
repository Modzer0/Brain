using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NeuralBrainInterface.Core.Configuration;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Manages application startup, component integration, and lifecycle coordination
/// </summary>
public class ApplicationStartupService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ApplicationStartupService> _logger;
    private readonly NeuralBrainInterfaceOptions _options;
    
    // Core components
    private readonly INeuralCore _neuralCore;
    private readonly IVisualizationEngine _visualizationEngine;
    private readonly IUIManager _uiManager;
    private readonly IResourceManager _resourceManager;
    private readonly IHardwareController _hardwareController;
    private readonly IStateManager _stateManager;
    private readonly ITimeContextManager _timeContextManager;
    private readonly IFileFormatManager _fileFormatManager;
    private readonly IMemoryManager _memoryManager;
    private readonly IBrainFileManager _brainFileManager;
    
    private bool _isInitialized = false;
    private bool _isShuttingDown = false;

    public ApplicationStartupService(
        IServiceProvider serviceProvider,
        ILogger<ApplicationStartupService> logger,
        IOptions<NeuralBrainInterfaceOptions> options,
        INeuralCore neuralCore,
        IVisualizationEngine visualizationEngine,
        IUIManager uiManager,
        IResourceManager resourceManager,
        IHardwareController hardwareController,
        IStateManager stateManager,
        ITimeContextManager timeContextManager,
        IFileFormatManager fileFormatManager,
        IMemoryManager memoryManager,
        IBrainFileManager brainFileManager)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options.Value;
        _neuralCore = neuralCore;
        _visualizationEngine = visualizationEngine;
        _uiManager = uiManager;
        _resourceManager = resourceManager;
        _hardwareController = hardwareController;
        _stateManager = stateManager;
        _timeContextManager = timeContextManager;
        _fileFormatManager = fileFormatManager;
        _memoryManager = memoryManager;
        _brainFileManager = brainFileManager;
    }

    /// <summary>
    /// Initialize the complete application with all components wired together
    /// </summary>
    public async Task<bool> InitializeApplicationAsync()
    {
        if (_isInitialized)
        {
            _logger.LogWarning("Application is already initialized");
            return true;
        }

        try
        {
            _logger.LogInformation("Starting Neural Brain Interface application initialization...");

            // Step 0: Initialize cross-service dependencies
            _logger.LogInformation("Initializing service dependencies...");
            var serviceInitializer = _serviceProvider.GetRequiredService<IServiceInitializer>();
            await serviceInitializer.InitializeServicesAsync();

            // Step 1: Initialize resource management first
            _logger.LogInformation("Configuring system resources...");
            var resourceConfigured = await ConfigureResourcesAsync();
            if (!resourceConfigured)
            {
                _logger.LogError("Failed to configure system resources");
                return false;
            }

            // Step 2: Initialize file format support
            _logger.LogInformation("Validating file format support...");
            await ValidateFileFormatSupportAsync();

            // Step 3: Initialize hardware devices and request permissions
            _logger.LogInformation("Initializing hardware devices...");
            var devicesInitialized = await InitializeHardwareDevicesAsync();
            if (!devicesInitialized)
            {
                _logger.LogWarning("Some hardware devices failed to initialize, continuing with available devices");
            }

            // Step 4: Initialize memory management system
            _logger.LogInformation("Initializing memory management...");
            await InitializeMemorySystemAsync();

            // Step 5: Initialize time context management
            _logger.LogInformation("Starting time context management...");
            InitializeTimeContextManagement();

            // Step 6: Initialize neural core with all dependencies
            _logger.LogInformation("Initializing neural processing core...");
            await InitializeNeuralCoreAsync();

            // Step 7: Initialize visualization engine
            _logger.LogInformation("Initializing visualization engine...");
            await InitializeVisualizationEngineAsync();

            // Step 8: Wire up component event handlers
            _logger.LogInformation("Wiring component event handlers...");
            WireComponentEventHandlers();

            // Step 9: Attempt automatic wake functionality
            _logger.LogInformation("Attempting automatic wake from previous session...");
            await AttemptAutomaticWakeAsync();

            // Step 10: Initialize UI manager last
            _logger.LogInformation("Initializing user interface...");
            await _uiManager.InitializeWindowAsync();

            _isInitialized = true;
            _logger.LogInformation("Neural Brain Interface application initialization completed successfully");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Neural Brain Interface application");
            return false;
        }
    }

    /// <summary>
    /// Configure system resources based on user preferences and hardware availability
    /// </summary>
    private async Task<bool> ConfigureResourcesAsync()
    {
        try
        {
            // Get available system resources
            var availableResources = _resourceManager.GetAvailableResources();
            _logger.LogInformation($"Available resources: {availableResources.TotalMemoryBytes / (1024 * 1024)}MB RAM, {availableResources.TotalCpuCores} CPU cores, {availableResources.TotalGpuCores} GPU cores");

            // Validate and configure resources
            var configured = await _resourceManager.ConfigureResourcesAsync(_options.ResourceConfig);
            if (!configured)
            {
                _logger.LogWarning("Failed to configure requested resources, attempting fallback configuration");
                
                // Create fallback configuration with reduced resource requirements
                var fallbackConfig = new ResourceConfig
                {
                    ActiveMemoryMb = Math.Min(_options.ResourceConfig.ActiveMemoryMb, (int)(availableResources.TotalMemoryBytes / (1024 * 1024) / 2)),
                    CpuCores = Math.Min(_options.ResourceConfig.CpuCores, availableResources.TotalCpuCores / 2),
                    GpuCores = Math.Min(_options.ResourceConfig.GpuCores, availableResources.TotalGpuCores / 2),
                    MaxProcessingTimeMs = _options.ResourceConfig.MaxProcessingTimeMs,
                    VisualizationFps = Math.Min(_options.ResourceConfig.VisualizationFps, 30)
                };

                configured = await _resourceManager.ConfigureResourcesAsync(fallbackConfig);
                if (configured)
                {
                    _logger.LogInformation("Successfully configured fallback resource allocation");
                }
            }

            return configured;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error configuring system resources");
            return false;
        }
    }

    /// <summary>
    /// Validate file format support and setup
    /// </summary>
    private async Task ValidateFileFormatSupportAsync()
    {
        try
        {
            _logger.LogInformation("Validating file format support and setup...");
            
            var supportedFormats = _fileFormatManager.GetSupportedFormats();
            _logger.LogInformation($"File format support initialized: {supportedFormats.Count} media types supported");
            
            // Validate each format group
            foreach (var formatGroup in supportedFormats)
            {
                _logger.LogDebug($"{formatGroup.Key}: {string.Join(", ", formatGroup.Value)}");
                
                // Ensure we have minimum required format support (Requirements 13.6, 13.7)
                switch (formatGroup.Key)
                {
                    case MediaType.Image:
                        if (!formatGroup.Value.Contains("jpg") && !formatGroup.Value.Contains("jpeg"))
                        {
                            _logger.LogWarning("JPEG image format support not detected");
                        }
                        if (!formatGroup.Value.Contains("png"))
                        {
                            _logger.LogWarning("PNG image format support not detected");
                        }
                        break;
                        
                    case MediaType.Video:
                        if (!formatGroup.Value.Contains("mp4"))
                        {
                            _logger.LogWarning("MP4 video format support not detected");
                        }
                        break;
                        
                    case MediaType.Audio:
                        if (!formatGroup.Value.Contains("mp3") && !formatGroup.Value.Contains("wav"))
                        {
                            _logger.LogWarning("Basic audio format support (MP3/WAV) not detected");
                        }
                        break;
                        
                    case MediaType.Document:
                        if (!formatGroup.Value.Contains("txt"))
                        {
                            _logger.LogWarning("Text document format support not detected");
                        }
                        break;
                }
            }
            
            // Test file format validation capabilities
            await TestFileFormatValidationAsync();
            
            _logger.LogInformation("File format validation and setup completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating file format support");
            throw new InvalidOperationException("File format validation failed", ex);
        }
    }

    /// <summary>
    /// Test file format validation capabilities
    /// </summary>
    private async Task TestFileFormatValidationAsync()
    {
        try
        {
            // Test with a non-existent file to ensure error handling works
            var testResult = await _fileFormatManager.ValidateFileIntegrityAsync("nonexistent.test");
            if (testResult.IsValid)
            {
                _logger.LogWarning("File format validation may not be working correctly - non-existent file reported as valid");
            }
            
            // Test unsupported format error message generation
            var errorMessage = await _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(".unsupported");
            if (string.IsNullOrEmpty(errorMessage))
            {
                _logger.LogWarning("Unsupported format error message generation may not be working correctly");
            }
            
            _logger.LogDebug("File format validation capabilities tested successfully");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error testing file format validation capabilities");
            // Don't throw here as this is just a test
        }
    }

    /// <summary>
    /// Initialize hardware devices and request necessary permissions
    /// </summary>
    private async Task<bool> InitializeHardwareDevicesAsync()
    {
        try
        {
            _logger.LogInformation("Initializing hardware devices and requesting permissions...");
            
            // Request device permissions first (Requirements 7.1, 7.4, 8.1, 9.1)
            var permissionsGranted = await _hardwareController.RequestDevicePermissionsAsync();
            if (!permissionsGranted)
            {
                _logger.LogWarning("Not all device permissions were granted - some functionality may be limited");
            }
            else
            {
                _logger.LogInformation("All device permissions granted successfully");
            }

            // Load device preferences from previous session
            await _hardwareController.LoadDevicePreferencesAsync();
            _logger.LogDebug("Device preferences loaded from previous session");

            // Validate device preferences
            var preferencesValid = await _hardwareController.ValidateDevicePreferencesAsync();
            if (!preferencesValid)
            {
                _logger.LogWarning("Device preferences validation failed, using defaults");
                // Reset to safe defaults
                await ResetDevicePreferencesToDefaultsAsync();
            }
            else
            {
                _logger.LogDebug("Device preferences validated successfully");
            }

            // Get initial device status and validate each device
            var deviceStatus = _hardwareController.GetAllDeviceStatus();
            _logger.LogInformation($"Hardware devices initialized: {deviceStatus.Count} devices detected");

            var availableDevices = 0;
            var enabledDevices = 0;
            
            foreach (var device in deviceStatus)
            {
                var status = device.Value;
                _logger.LogDebug($"{device.Key}: {(status.IsAvailable ? "Available" : "Unavailable")} - {(status.IsEnabled ? "Enabled" : "Disabled")}");
                
                if (status.IsAvailable)
                {
                    availableDevices++;
                    if (status.IsEnabled)
                    {
                        enabledDevices++;
                    }
                }
                
                if (!status.IsAvailable && !string.IsNullOrEmpty(status.ErrorMessage))
                {
                    _logger.LogWarning($"Device {device.Key} unavailable: {status.ErrorMessage}");
                }
                
                if (!status.PermissionGranted)
                {
                    _logger.LogWarning($"Permission not granted for device {device.Key}");
                }
            }
            
            _logger.LogInformation($"Device summary: {availableDevices} available, {enabledDevices} enabled");
            
            // Ensure at least basic functionality is available
            if (availableDevices == 0)
            {
                _logger.LogWarning("No hardware devices are available - application will run in limited mode");
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing hardware devices");
            return false;
        }
    }

    /// <summary>
    /// Reset device preferences to safe defaults
    /// </summary>
    private async Task ResetDevicePreferencesToDefaultsAsync()
    {
        try
        {
            // Disable all devices by default for safety
            await _hardwareController.ToggleDeviceAsync(DeviceType.Microphone, false);
            await _hardwareController.ToggleDeviceAsync(DeviceType.Speaker, false);
            await _hardwareController.ToggleDeviceAsync(DeviceType.Webcam, false);
            
            // Save the default preferences
            await _hardwareController.SaveDevicePreferencesAsync();
            
            _logger.LogInformation("Device preferences reset to safe defaults");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resetting device preferences to defaults");
        }
    }

    /// <summary>
    /// Initialize memory management system
    /// </summary>
    private async Task InitializeMemorySystemAsync()
    {
        try
        {
            // Restore memory state from previous session if available
            var memoryRestored = await _memoryManager.RestoreMemoryStateAsync();
            if (memoryRestored)
            {
                _logger.LogInformation("Memory state restored from previous session");
            }
            else
            {
                _logger.LogInformation("Starting with fresh memory state");
            }

            // Get initial memory usage
            var memoryUsage = await _memoryManager.GetMemoryUsageAsync();
            _logger.LogInformation($"Memory system initialized - Short-term: {memoryUsage.ShortTermUsed}/{memoryUsage.ShortTermCapacity}MB, Long-term: {memoryUsage.LongTermUsed}MB");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing memory system");
        }
    }

    /// <summary>
    /// Initialize time context management
    /// </summary>
    private void InitializeTimeContextManagement()
    {
        try
        {
            // Configure time update interval (default: every 30 seconds)
            _timeContextManager.ConfigureUpdateInterval(NodaTime.Duration.FromSeconds(30));
            
            // Start time updates
            _timeContextManager.StartTimeUpdates();
            
            var currentTime = _timeContextManager.GetTimeInfo();
            _logger.LogInformation($"Time context management started - Current time: {currentTime.CurrentDateTime}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing time context management");
        }
    }

    /// <summary>
    /// Initialize neural processing core with all dependencies
    /// </summary>
    private async Task InitializeNeuralCoreAsync()
    {
        try
        {
            // Update neural core with current device context
            var deviceStatus = _hardwareController.GetAllDeviceStatus();
            var deviceContext = deviceStatus.ToDictionary(
                kvp => kvp.Key, 
                kvp => kvp.Value.IsEnabled && kvp.Value.IsAvailable
            );
            _neuralCore.UpdateDeviceContext(deviceContext);

            // Update neural core with current time context
            var timeInfo = _timeContextManager.GetTimeInfo();
            _neuralCore.UpdateTimeContext(timeInfo);

            _logger.LogInformation("Neural processing core initialized with device and time context");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing neural processing core");
        }
    }

    /// <summary>
    /// Initialize visualization engine
    /// </summary>
    private async Task InitializeVisualizationEngineAsync()
    {
        try
        {
            // Get current neural state for initial visualization
            var currentState = await _neuralCore.GetCurrentStateAsync();
            var initialFrame = await _visualizationEngine.RenderNeuralStateAsync(currentState);
            await _visualizationEngine.UpdateDisplayAsync(initialFrame);

            _logger.LogInformation("Visualization engine initialized");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error initializing visualization engine");
        }
    }

    /// <summary>
    /// Wire up event handlers between components for real-time communication
    /// </summary>
    private void WireComponentEventHandlers()
    {
        try
        {
            // Neural Core -> Visualization Engine
            _neuralCore.StateChanged += async (sender, neuralState) =>
            {
                try
                {
                    var visualFrame = await _visualizationEngine.RenderNeuralStateAsync(neuralState);
                    await _visualizationEngine.UpdateDisplayAsync(visualFrame);
                    await _uiManager.UpdateMindDisplayAsync(visualFrame);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating visualization from neural state change");
                }
            };

            // Neural Core -> UI Manager (for responses)
            _neuralCore.ProcessingCompleted += async (sender, result) =>
            {
                try
                {
                    if (!string.IsNullOrEmpty(result.GeneratedOutput))
                    {
                        await _uiManager.DisplayResponseAsync(result.GeneratedOutput);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error displaying neural core response");
                }
            };

            // UI Manager -> Neural Core (for user inputs)
            _uiManager.TextInputReceived += async (sender, input) =>
            {
                try
                {
                    await _neuralCore.ProcessTextAsync(input);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing text input from UI");
                }
            };

            // UI Manager -> File Format Manager -> Neural Core (for file uploads)
            _uiManager.FileUploaded += async (sender, fileData) =>
            {
                try
                {
                    await ProcessFileUploadAsync(fileData.Data, fileData.FileName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing file upload from UI");
                }
            };

            // UI Manager -> Hardware Controller (for device toggles)
            _uiManager.DeviceToggleRequested += async (sender, deviceType) =>
            {
                try
                {
                    var currentStatus = _hardwareController.GetDeviceStatus(deviceType);
                    await _hardwareController.ToggleDeviceAsync(deviceType, !currentStatus.IsEnabled);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error toggling device {deviceType}");
                }
            };

            // UI Manager -> State Manager (for sleep/wake)
            _uiManager.SleepRequested += async (sender, e) =>
            {
                try
                {
                    await _stateManager.InitiateSleepAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initiating sleep from UI");
                }
            };

            _uiManager.WakeRequested += async (sender, e) =>
            {
                try
                {
                    await _stateManager.InitiateWakeAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error initiating wake from UI");
                }
            };

            // Hardware Controller -> Neural Core (for device state changes)
            _hardwareController.DeviceStatusChanged += (sender, deviceStatus) =>
            {
                try
                {
                    var allDeviceStatus = _hardwareController.GetAllDeviceStatus();
                    var deviceContext = allDeviceStatus.ToDictionary(
                        kvp => kvp.Key, 
                        kvp => kvp.Value.IsEnabled && kvp.Value.IsAvailable
                    );
                    _neuralCore.UpdateDeviceContext(deviceContext);
                    
                    // Also update UI
                    _ = Task.Run(async () => await _uiManager.UpdateDeviceStatusAsync(deviceStatus));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating device context from hardware controller");
                }
            };

            // Time Context Manager -> Neural Core (for time updates)
            _timeContextManager.TimeContextUpdated += (sender, timeInfo) =>
            {
                try
                {
                    _neuralCore.UpdateTimeContext(timeInfo);
                    
                    // Also update UI
                    _ = Task.Run(async () => await _uiManager.DisplayTimeContextAsync(timeInfo));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating time context from time context manager");
                }
            };

            // Memory Manager -> Logging (for memory events)
            _memoryManager.MemoryUsageChanged += (sender, usage) =>
            {
                _logger.LogDebug($"Memory usage changed - Short-term: {usage.ShortTermUsed}/{usage.ShortTermCapacity}MB, Long-term: {usage.LongTermUsed}MB");
            };

            _memoryManager.MemoryError += (sender, error) =>
            {
                _logger.LogError($"Memory system error: {error}");
            };

            // Resource Manager -> Logging (for resource monitoring)
            _resourceManager.ResourceUsageChanged += (sender, usage) =>
            {
                _logger.LogDebug($"Resource usage - CPU: {usage.CpuUsagePercent}%, Memory: {usage.MemoryUsedBytes / (1024 * 1024)}MB, GPU: {usage.GpuUsagePercent}%");
            };

            _resourceManager.ResourceWarning += (sender, warning) =>
            {
                _logger.LogWarning($"Resource warning: {warning}");
            };

            _logger.LogInformation("Component event handlers wired successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error wiring component event handlers");
        }
    }

    /// <summary>
    /// Attempt automatic wake functionality on startup
    /// </summary>
    private async Task AttemptAutomaticWakeAsync()
    {
        try
        {
            var wakeSuccessful = await _stateManager.AutoWakeOnStartAsync();
            if (wakeSuccessful)
            {
                _logger.LogInformation("Automatic wake completed successfully");
            }
            else
            {
                _logger.LogInformation("No previous state found for automatic wake, starting fresh");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during automatic wake attempt");
        }
    }

    /// <summary>
    /// Process file uploads through the file format manager and neural core
    /// </summary>
    private async Task ProcessFileUploadAsync(byte[] fileData, string fileName)
    {
        try
        {
            // Save file temporarily for processing
            var tempFilePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(tempFilePath, fileData);

            try
            {
                // Detect file format
                var fileFormat = await _fileFormatManager.DetectFileFormatAsync(tempFilePath);
                
                if (!fileFormat.IsSupported)
                {
                    var errorMessage = await _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(Path.GetExtension(fileName));
                    await _uiManager.DisplayResponseAsync($"Error: {errorMessage}");
                    return;
                }

                // Validate file integrity
                var validationResult = await _fileFormatManager.ValidateFileIntegrityAsync(tempFilePath);
                if (!validationResult.IsValid)
                {
                    await _uiManager.DisplayResponseAsync($"Error: File validation failed - {string.Join(", ", validationResult.ErrorMessages)}");
                    return;
                }

                // Process based on media type
                switch (fileFormat.MediaType)
                {
                    case MediaType.Image:
                        var imageData = await _fileFormatManager.ConvertImageFileAsync(tempFilePath);
                        await _neuralCore.ProcessImageAsync(imageData);
                        break;
                    
                    case MediaType.Video:
                        var videoData = await _fileFormatManager.ConvertVideoFileAsync(tempFilePath);
                        await _neuralCore.ProcessVideoAsync(videoData);
                        break;
                    
                    case MediaType.Audio:
                        var audioData = await _fileFormatManager.ConvertAudioFileAsync(tempFilePath);
                        await _neuralCore.ProcessAudioAsync(audioData);
                        break;
                    
                    case MediaType.Document:
                        var documentData = await _fileFormatManager.ConvertDocumentFileAsync(tempFilePath);
                        await _neuralCore.ProcessDocumentAsync(documentData);
                        break;
                    
                    case MediaType.Spreadsheet:
                        var spreadsheetData = await _fileFormatManager.ConvertSpreadsheetFileAsync(tempFilePath);
                        await _neuralCore.ProcessSpreadsheetAsync(spreadsheetData);
                        break;
                    
                    default:
                        await _uiManager.DisplayResponseAsync($"Error: Unsupported media type {fileFormat.MediaType}");
                        break;
                }
            }
            finally
            {
                // Clean up temporary file
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error processing file upload: {fileName}");
            await _uiManager.DisplayResponseAsync($"Error processing file: {ex.Message}");
        }
    }

    /// <summary>
    /// Gracefully shutdown the application with proper cleanup
    /// </summary>
    public async Task ShutdownApplicationAsync()
    {
        if (_isShuttingDown || !_isInitialized)
        {
            return;
        }

        _isShuttingDown = true;

        try
        {
            _logger.LogInformation("Starting Neural Brain Interface application shutdown...");

            // Step 1: Initiate automatic sleep to save state
            _logger.LogInformation("Saving application state...");
            await _stateManager.AutoSleepOnCloseAsync();

            // Step 2: Save memory state
            _logger.LogInformation("Saving memory state...");
            await _memoryManager.SaveMemoryStateAsync();

            // Step 3: Save device preferences
            _logger.LogInformation("Saving device preferences...");
            await _hardwareController.SaveDevicePreferencesAsync();

            // Step 4: Stop time context updates
            _logger.LogInformation("Stopping time context updates...");
            _timeContextManager.StopTimeUpdates();

            // Step 5: Clean up resources
            _logger.LogInformation("Cleaning up system resources...");
            // Resource cleanup is handled by DI container disposal

            _logger.LogInformation("Neural Brain Interface application shutdown completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during application shutdown");
        }
        finally
        {
            _isShuttingDown = false;
        }
    }

    /// <summary>
    /// Get the current initialization status
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Get the current shutdown status
    /// </summary>
    public bool IsShuttingDown => _isShuttingDown;
}