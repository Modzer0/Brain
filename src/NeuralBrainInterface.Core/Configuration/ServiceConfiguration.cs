using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Configuration;

public static class ServiceConfiguration
{
    public static IServiceCollection AddNeuralBrainInterfaceCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Memory services (must be registered first as they have no dependencies on other services)
        services.AddSingleton<IShortTermMemory, Services.ShortTermMemory>();
        services.AddSingleton<ILongTermMemory, Services.LongTermMemory>();
        
        // State and time management (no circular dependencies)
        services.AddSingleton<IStateManager, Services.StateManager>();
        services.AddSingleton<ITimeContextManager, Services.TimeContextManager>();
        
        // Memory manager depends on short/long term memory
        services.AddSingleton<IMemoryManager, Services.MemoryManager>();
        
        // Core services - use fully qualified name to avoid placeholder conflict
        services.AddSingleton<INeuralCore, Services.NeuralCore>();
        services.AddSingleton<IVisualizationEngine, Services.VisualizationEngine>();
        services.AddSingleton<IResourceManager, Services.ResourceManager>();
        
        // Hardware management
        services.AddSingleton<IHardwareController, Services.HardwareController>();
        services.AddSingleton<IAudioInputManager, Services.AudioInputManager>();
        services.AddSingleton<IAudioOutputManager, Services.AudioOutputManager>();
        services.AddSingleton<IVideoInputManager, Services.VideoInputManager>();
        
        // File management
        services.AddSingleton<IFileFormatManager, Services.FileFormatManager>();
        services.AddSingleton<IBrainFileManager, Services.BrainFileManager>();
        
        // NodaTime clock for time-based operations
        services.AddSingleton<NodaTime.IClock>(NodaTime.SystemClock.Instance);
        
        // Application startup and integration
        services.AddSingleton<Services.ApplicationStartupService>();
        
        // Post-configuration to resolve circular dependencies
        services.AddSingleton<IServiceInitializer, ServiceInitializer>();
        
        // Configuration
        services.Configure<NeuralBrainInterfaceOptions>(configuration.GetSection("NeuralBrainInterface"));
        
        // Logging
        services.AddLogging(builder =>
        {
            builder.AddConsole();
            builder.AddDebug();
        });
        
        return services;
    }
}

/// <summary>
/// Handles post-configuration initialization to resolve circular dependencies
/// </summary>
public interface IServiceInitializer
{
    Task InitializeServicesAsync();
}

public class ServiceInitializer : IServiceInitializer
{
    private readonly IServiceProvider _serviceProvider;
    
    public ServiceInitializer(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }
    
    public async Task InitializeServicesAsync()
    {
        // Resolve circular dependency between TimeContextManager and NeuralCore
        var timeContextManager = _serviceProvider.GetRequiredService<ITimeContextManager>();
        var neuralCore = _serviceProvider.GetRequiredService<INeuralCore>();
        
        if (timeContextManager is Services.TimeContextManager tcm)
        {
            tcm.SetNeuralCore(neuralCore);
        }
        
        // Initialize any other cross-dependencies here
        await Task.CompletedTask;
    }
}

public class NeuralBrainInterfaceOptions
{
    public ResourceConfig ResourceConfig { get; set; } = new();
    public AudioSettings AudioSettings { get; set; } = new();
    public VideoSettings VideoSettings { get; set; } = new();
    public VoiceSettings VoiceSettings { get; set; } = new();
    public string DataDirectory { get; set; } = "Data";
    public string BrainFilesDirectory { get; set; } = "BrainFiles";
    public string MemoryDirectory { get; set; } = "Memory";
}