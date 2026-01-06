using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using NeuralBrainInterface.Core.Configuration;
using NeuralBrainInterface.Core.Interfaces;

namespace NeuralBrainInterface.Tests;

public class CoreInterfacesTests
{
    private readonly IServiceProvider _serviceProvider;

    public CoreInterfacesTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NeuralBrainInterface:DataDirectory"] = "TestData",
                ["NeuralBrainInterface:ResourceConfig:ActiveMemoryMb"] = "1024"
            })
            .Build();

        services.AddNeuralBrainInterfaceCore(configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Fact]
    public void CanResolveNeuralCore()
    {
        var neuralCore = _serviceProvider.GetService<INeuralCore>();
        Assert.NotNull(neuralCore);
    }

    [Fact]
    public void CanResolveVisualizationEngine()
    {
        var visualizationEngine = _serviceProvider.GetService<IVisualizationEngine>();
        Assert.NotNull(visualizationEngine);
    }

    [Fact]
    public void CanResolveResourceManager()
    {
        var resourceManager = _serviceProvider.GetService<IResourceManager>();
        Assert.NotNull(resourceManager);
    }

    [Fact]
    public void CanResolveHardwareController()
    {
        var hardwareController = _serviceProvider.GetService<IHardwareController>();
        Assert.NotNull(hardwareController);
    }

    [Fact]
    public void CanResolveAudioInputManager()
    {
        var audioInputManager = _serviceProvider.GetService<IAudioInputManager>();
        Assert.NotNull(audioInputManager);
    }

    [Fact]
    public void CanResolveAudioOutputManager()
    {
        var audioOutputManager = _serviceProvider.GetService<IAudioOutputManager>();
        Assert.NotNull(audioOutputManager);
    }

    [Fact]
    public void CanResolveVideoInputManager()
    {
        var videoInputManager = _serviceProvider.GetService<IVideoInputManager>();
        Assert.NotNull(videoInputManager);
    }

    [Fact]
    public void CanResolveStateManager()
    {
        var stateManager = _serviceProvider.GetService<IStateManager>();
        Assert.NotNull(stateManager);
    }

    [Fact]
    public void CanResolveTimeContextManager()
    {
        var timeContextManager = _serviceProvider.GetService<ITimeContextManager>();
        Assert.NotNull(timeContextManager);
    }

    [Fact]
    public void CanResolveFileFormatManager()
    {
        var fileFormatManager = _serviceProvider.GetService<IFileFormatManager>();
        Assert.NotNull(fileFormatManager);
    }

    [Fact]
    public void CanResolveMemoryManager()
    {
        var memoryManager = _serviceProvider.GetService<IMemoryManager>();
        Assert.NotNull(memoryManager);
    }

    [Fact]
    public void CanResolveShortTermMemory()
    {
        var shortTermMemory = _serviceProvider.GetService<IShortTermMemory>();
        Assert.NotNull(shortTermMemory);
    }

    [Fact]
    public void CanResolveLongTermMemory()
    {
        var longTermMemory = _serviceProvider.GetService<ILongTermMemory>();
        Assert.NotNull(longTermMemory);
    }

    [Fact]
    public void CanResolveBrainFileManager()
    {
        var brainFileManager = _serviceProvider.GetService<IBrainFileManager>();
        Assert.NotNull(brainFileManager);
    }

    [Fact]
    public void CanResolveUIManager()
    {
        var uiManager = _serviceProvider.GetService<IUIManager>();
        Assert.NotNull(uiManager);
    }
}