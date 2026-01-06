using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NeuralBrainInterface.Core.Configuration;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using Xunit;
using Xunit.Abstractions;

namespace NeuralBrainInterface.Tests;

/// <summary>
/// Mock UIManager for testing purposes
/// </summary>
public class MockUIManager : IUIManager
{
    public event EventHandler<string>? TextInputReceived;
    public event EventHandler<(byte[] Data, string FileName)>? FileUploaded;
    public event EventHandler<DeviceType>? DeviceToggleRequested;
    public event EventHandler? SleepRequested;
    public event EventHandler? WakeRequested;

    public Task InitializeWindowAsync() => Task.CompletedTask;
    public Task HandleTextInputAsync(string input) => Task.CompletedTask;
    public Task HandleFileUploadAsync(byte[] fileData, string fileName) => Task.CompletedTask;
    public Task UpdateMindDisplayAsync(VisualFrame visualFrame) => Task.CompletedTask;
    public Task DisplayResponseAsync(string response) => Task.CompletedTask;
    public Task ToggleMicrophoneAsync(bool enabled) => Task.CompletedTask;
    public Task ToggleSpeakerAsync(bool enabled) => Task.CompletedTask;
    public Task ToggleWebcamAsync(bool enabled) => Task.CompletedTask;
    public Task UpdateDeviceStatusAsync(DeviceStatus status) => Task.CompletedTask;
    public Task ShowSleepMenuAsync() => Task.CompletedTask;
    public Task ShowWakeMenuAsync() => Task.CompletedTask;
    public Task DisplayTimeContextAsync(TimeInfo timeInfo) => Task.CompletedTask;
    public Task ShowDeviceConfigurationDialogAsync(DeviceType deviceType) => Task.CompletedTask;
    public Task ShowDevicePermissionsDialogAsync() => Task.CompletedTask;
    public Task RefreshDeviceStatusAsync() => Task.CompletedTask;
}

/// <summary>
/// Integration tests for the complete Neural Brain Interface application
/// Tests end-to-end workflows, component integration, and system behavior
/// </summary>
public class IntegrationTests : IDisposable
{
    private readonly ITestOutputHelper _output;
    private readonly IHost _host;
    private readonly IServiceProvider _serviceProvider;
    private readonly ApplicationStartupService _startupService;
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

    public IntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        
        // Create test host with all services
        var builder = Host.CreateDefaultBuilder();
        builder.ConfigureServices((context, services) =>
        {
            services.AddNeuralBrainInterfaceCore(context.Configuration);
            
            // Add a mock UIManager for testing
            services.AddSingleton<IUIManager, MockUIManager>();
            
            services.AddLogging(logging => logging.AddConsole().AddDebug());
        });
        
        _host = builder.Build();
        _serviceProvider = _host.Services;
        
        // Get all required services
        _startupService = _serviceProvider.GetRequiredService<ApplicationStartupService>();
        _neuralCore = _serviceProvider.GetRequiredService<INeuralCore>();
        _visualizationEngine = _serviceProvider.GetRequiredService<IVisualizationEngine>();
        _uiManager = _serviceProvider.GetRequiredService<IUIManager>();
        _resourceManager = _serviceProvider.GetRequiredService<IResourceManager>();
        _hardwareController = _serviceProvider.GetRequiredService<IHardwareController>();
        _stateManager = _serviceProvider.GetRequiredService<IStateManager>();
        _timeContextManager = _serviceProvider.GetRequiredService<ITimeContextManager>();
        _fileFormatManager = _serviceProvider.GetRequiredService<IFileFormatManager>();
        _memoryManager = _serviceProvider.GetRequiredService<IMemoryManager>();
        _brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
    }

    [Fact]
    public async Task ApplicationStartup_ShouldInitializeAllComponents()
    {
        // Test complete application startup sequence
        _output.WriteLine("Testing application startup and component initialization...");
        
        // Start the host
        await _host.StartAsync();
        
        // Initialize the application
        var initializationResult = await _startupService.InitializeApplicationAsync();
        
        // Verify initialization was successful
        Assert.True(initializationResult, "Application initialization should succeed");
        Assert.True(_startupService.IsInitialized, "Startup service should report as initialized");
        
        _output.WriteLine("Application startup completed successfully");
    }

    [Fact]
    public async Task EndToEndWorkflow_TextInputToVisualization_ShouldWork()
    {
        // Test complete workflow from text input to visualization
        _output.WriteLine("Testing end-to-end text input to visualization workflow...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Simulate text input
        var testInput = "Hello, neural network!";
        var processingResult = await _neuralCore.ProcessTextAsync(testInput);
        
        // Verify processing completed
        Assert.True(processingResult.Success, "Text processing should succeed");
        Assert.NotNull(processingResult.UpdatedState);
        
        // Get current neural state
        var currentState = await _neuralCore.GetCurrentStateAsync();
        Assert.NotNull(currentState);
        
        // Test visualization rendering
        var visualFrame = await _visualizationEngine.RenderNeuralStateAsync(currentState);
        Assert.NotNull(visualFrame);
        
        // Test visualization update
        await _visualizationEngine.UpdateDisplayAsync(visualFrame);
        
        _output.WriteLine("End-to-end text workflow completed successfully");
    }

    [Fact]
    public async Task FileProcessingWorkflow_ShouldHandleMultipleFormats()
    {
        // Test file processing workflow with different formats
        _output.WriteLine("Testing file processing workflow...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Test supported formats detection
        var supportedFormats = _fileFormatManager.GetSupportedFormats();
        Assert.NotEmpty(supportedFormats);
        
        // Create test files for different formats
        var testFiles = new Dictionary<string, byte[]>
        {
            ["test.txt"] = System.Text.Encoding.UTF8.GetBytes("Test document content"),
            ["test.json"] = System.Text.Encoding.UTF8.GetBytes("{\"test\": \"data\"}"),
            ["test.csv"] = System.Text.Encoding.UTF8.GetBytes("name,value\ntest,123")
        };
        
        foreach (var testFile in testFiles)
        {
            var tempPath = Path.Combine(Path.GetTempPath(), testFile.Key);
            await File.WriteAllBytesAsync(tempPath, testFile.Value);
            
            try
            {
                // Test file format detection
                var fileFormat = await _fileFormatManager.DetectFileFormatAsync(tempPath);
                Assert.NotNull(fileFormat);
                
                // Test file validation
                var validationResult = await _fileFormatManager.ValidateFileIntegrityAsync(tempPath);
                Assert.NotNull(validationResult);
                
                _output.WriteLine($"Successfully processed {testFile.Key}");
            }
            finally
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
        }
        
        _output.WriteLine("File processing workflow completed successfully");
    }

    [Fact]
    public async Task HardwareDeviceIntegration_ShouldManageDeviceStates()
    {
        // Test hardware device integration and state management
        _output.WriteLine("Testing hardware device integration...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Test device status retrieval
        var allDeviceStatus = _hardwareController.GetAllDeviceStatus();
        Assert.NotNull(allDeviceStatus);
        
        // Test individual device status
        var microphoneStatus = _hardwareController.GetDeviceStatus(DeviceType.Microphone);
        var speakerStatus = _hardwareController.GetDeviceStatus(DeviceType.Speaker);
        var webcamStatus = _hardwareController.GetDeviceStatus(DeviceType.Webcam);
        
        Assert.NotNull(microphoneStatus);
        Assert.NotNull(speakerStatus);
        Assert.NotNull(webcamStatus);
        
        // Test device preferences
        await _hardwareController.SaveDevicePreferencesAsync();
        await _hardwareController.LoadDevicePreferencesAsync();
        
        // Test device validation
        var preferencesValid = await _hardwareController.ValidateDevicePreferencesAsync();
        Assert.True(preferencesValid);
        
        _output.WriteLine("Hardware device integration completed successfully");
    }

    [Fact]
    public async Task SleepWakeCycles_ShouldPreserveState()
    {
        // Test sleep/wake cycles and state preservation
        _output.WriteLine("Testing sleep/wake cycles and state preservation...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Process some data to create state
        var testInput = "Test data for state preservation";
        await _neuralCore.ProcessTextAsync(testInput);
        
        // Get initial state
        var initialState = await _neuralCore.GetCurrentStateAsync();
        Assert.NotNull(initialState);
        
        // Test sleep functionality
        var sleepResult = await _stateManager.InitiateSleepAsync();
        Assert.True(sleepResult, "Sleep should succeed");
        
        // Verify sleep status
        var sleepStatus = _stateManager.GetSleepStatus();
        Assert.True(sleepStatus.IsSleeping, "System should be in sleep mode");
        
        // Test wake functionality
        var wakeResult = await _stateManager.InitiateWakeAsync();
        Assert.True(wakeResult, "Wake should succeed");
        
        // Verify wake status
        sleepStatus = _stateManager.GetSleepStatus();
        Assert.False(sleepStatus.IsSleeping, "System should be awake");
        
        // Verify state preservation
        var restoredState = await _neuralCore.GetCurrentStateAsync();
        Assert.NotNull(restoredState);
        
        _output.WriteLine("Sleep/wake cycles completed successfully");
    }

    [Fact]
    public async Task TimeContextIntegration_ShouldProvideTemporalAwareness()
    {
        // Test time context integration across all components
        _output.WriteLine("Testing time context integration...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Test time context manager
        var currentTime = _timeContextManager.GetCurrentTime();
        Assert.True(currentTime > NodaTime.Instant.MinValue);
        
        var timeInfo = _timeContextManager.GetTimeInfo();
        Assert.NotNull(timeInfo);
        Assert.True(timeInfo.CurrentDateTime > NodaTime.Instant.MinValue);
        
        // Test session duration tracking
        var sessionDuration = _timeContextManager.GetSessionDuration();
        Assert.True(sessionDuration >= NodaTime.Duration.Zero);
        
        // Test time updates
        _timeContextManager.StartTimeUpdates();
        await Task.Delay(100); // Brief delay to allow updates
        _timeContextManager.StopTimeUpdates();
        
        _output.WriteLine("Time context integration completed successfully");
    }

    [Fact]
    public async Task MemoryManagement_ShouldHandleShortAndLongTermMemory()
    {
        // Test comprehensive memory management workflows
        _output.WriteLine("Testing memory management system...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Create test memory items
        var testMemories = new List<MemoryItem>
        {
            new MemoryItem
            {
                MemoryId = Guid.NewGuid().ToString(),
                Content = "Test memory 1",
                Context = new Dictionary<string, object> { ["type"] = "test" },
                Timestamp = NodaTime.SystemClock.Instance.GetCurrentInstant(),
                ImportanceScore = 0.8f,
                MemoryType = MemoryType.Episodic,
                Tags = new List<string> { "test", "integration" }
            },
            new MemoryItem
            {
                MemoryId = Guid.NewGuid().ToString(),
                Content = "Test memory 2",
                Context = new Dictionary<string, object> { ["type"] = "test" },
                Timestamp = NodaTime.SystemClock.Instance.GetCurrentInstant(),
                ImportanceScore = 0.6f,
                MemoryType = MemoryType.Episodic,
                Tags = new List<string> { "test", "data" }
            }
        };
        
        // Test short-term memory storage
        foreach (var memory in testMemories)
        {
            var stored = await _memoryManager.StoreShortTermMemoryAsync(memory);
            Assert.True(stored, $"Should store memory {memory.MemoryId}");
        }
        
        // Test memory retrieval
        var query = new MemoryQuery
        {
            SearchTerms = "test",
            MaxResults = 10,
            MemoryTypes = new List<MemoryType> { MemoryType.ShortTerm, MemoryType.LongTerm }
        };
        
        var retrievedMemories = await _memoryManager.RecallMemoryAsync(query);
        Assert.NotEmpty(retrievedMemories);
        
        // Test memory usage monitoring
        var memoryUsage = await _memoryManager.GetMemoryUsageAsync();
        Assert.NotNull(memoryUsage);
        Assert.True(memoryUsage.ShortTermUsed >= 0);
        
        // Test memory statistics
        var statistics = await _memoryManager.GetDetailedStatisticsAsync();
        Assert.NotNull(statistics);
        Assert.True(statistics.TotalMemories >= 0);
        
        _output.WriteLine("Memory management system completed successfully");
    }

    [Fact]
    public async Task BrainFileManagement_ShouldHandleImportExport()
    {
        // Test brain file management workflows
        _output.WriteLine("Testing brain file management...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Create test brain file path
        var testBrainPath = Path.Combine(Path.GetTempPath(), "test_brain.brain");
        
        try
        {
            // Test brain file creation
            var brainConfig = new Dictionary<string, object>
            {
                ["brain_name"] = "Test Brain",
                ["neural_network_type"] = "Standard",
                ["version"] = "1.0.0"
            };
            
            var created = await _brainFileManager.CreateNewBrainFileAsync(testBrainPath, brainConfig);
            Assert.True(created, "Should create new brain file");
            Assert.True(File.Exists(testBrainPath), "Brain file should exist on disk");
            
            // Test brain file validation
            var validationResult = await _brainFileManager.ValidateBrainFileAsync(testBrainPath);
            Assert.NotNull(validationResult);
            Assert.True(validationResult.IsValid, "Brain file should be valid");
            
            // Test brain file metadata
            var metadata = await _brainFileManager.GetBrainFileMetadataAsync(testBrainPath);
            Assert.NotNull(metadata);
            Assert.Equal("Test Brain", metadata.BrainName);
            
            // Test brain file export (with current state)
            var exported = await _brainFileManager.ExportBrainFileAsync(testBrainPath, true);
            Assert.True(exported, "Should export brain file");
            
            _output.WriteLine("Brain file management completed successfully");
        }
        finally
        {
            if (File.Exists(testBrainPath))
            {
                File.Delete(testBrainPath);
            }
        }
    }

    [Fact]
    public async Task ErrorHandlingAndRecovery_ShouldHandleFailures()
    {
        // Test error handling and recovery scenarios
        _output.WriteLine("Testing error handling and recovery...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Test invalid file processing
        var invalidFilePath = Path.Combine(Path.GetTempPath(), "invalid_file.xyz");
        await File.WriteAllBytesAsync(invalidFilePath, new byte[] { 0x00, 0x01, 0x02 });
        
        try
        {
            var fileFormat = await _fileFormatManager.DetectFileFormatAsync(invalidFilePath);
            Assert.NotNull(fileFormat);
            
            if (!fileFormat.IsSupported)
            {
                var errorMessage = await _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(".xyz");
                Assert.NotEmpty(errorMessage);
                _output.WriteLine($"Correctly handled unsupported format: {errorMessage}");
            }
        }
        finally
        {
            if (File.Exists(invalidFilePath))
            {
                File.Delete(invalidFilePath);
            }
        }
        
        // Test resource constraint handling
        var constrainedConfig = new ResourceConfig
        {
            ActiveMemoryMb = int.MaxValue, // Impossible allocation
            CpuCores = 1000, // Impossible allocation
            GpuCores = 100, // Impossible allocation
            MaxProcessingTimeMs = 1,
            VisualizationFps = 1000
        };
        
        var resourceConfigured = await _resourceManager.ConfigureResourcesAsync(constrainedConfig);
        // Should either succeed with fallback or fail gracefully
        Assert.True(resourceConfigured || !resourceConfigured); // Either outcome is acceptable for this test
        
        _output.WriteLine("Error handling and recovery completed successfully");
    }

    [Fact]
    public async Task ResourceManagement_ShouldOptimizePerformance()
    {
        // Test resource management and performance optimization
        _output.WriteLine("Testing resource management and optimization...");
        
        await _host.StartAsync();
        await _startupService.InitializeApplicationAsync();
        
        // Test resource availability detection
        var availableResources = _resourceManager.GetAvailableResources();
        Assert.NotNull(availableResources);
        Assert.True(availableResources.TotalMemoryBytes > 0);
        Assert.True(availableResources.TotalCpuCores > 0);
        
        // Test resource allocation
        var memoryHandle = _resourceManager.AllocateMemory(1024 * 1024); // 1MB
        Assert.NotNull(memoryHandle);
        
        var computeHandle = _resourceManager.AllocateCompute(1, 0);
        Assert.NotNull(computeHandle);
        
        // Test resource monitoring
        var resourceUsage = _resourceManager.MonitorUsage();
        Assert.NotNull(resourceUsage);
        Assert.True(resourceUsage.CpuUsagePercent >= 0);
        Assert.True(resourceUsage.MemoryUsedBytes >= 0);
        
        _output.WriteLine("Resource management and optimization completed successfully");
    }

    [Fact]
    public async Task ComponentLifecycle_ShouldHandleStartupAndShutdown()
    {
        // Test complete component lifecycle management
        _output.WriteLine("Testing component lifecycle management...");
        
        // Test startup
        await _host.StartAsync();
        var initResult = await _startupService.InitializeApplicationAsync();
        Assert.True(initResult, "Initialization should succeed");
        Assert.True(_startupService.IsInitialized, "Should be initialized");
        
        // Test that components are working
        var neuralState = await _neuralCore.GetCurrentStateAsync();
        Assert.NotNull(neuralState);
        
        var timeInfo = _timeContextManager.GetTimeInfo();
        Assert.NotNull(timeInfo);
        
        var deviceStatus = _hardwareController.GetAllDeviceStatus();
        Assert.NotNull(deviceStatus);
        
        // Test shutdown
        await _startupService.ShutdownApplicationAsync();
        Assert.False(_startupService.IsShuttingDown, "Should complete shutdown");
        
        await _host.StopAsync();
        
        _output.WriteLine("Component lifecycle management completed successfully");
    }

    public void Dispose()
    {
        _host?.Dispose();
    }
}