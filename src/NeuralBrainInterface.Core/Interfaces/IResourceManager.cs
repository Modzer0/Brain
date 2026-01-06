using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IResourceManager
{
    Task<bool> ConfigureResourcesAsync(ResourceConfig config);
    ResourceInfo GetAvailableResources();
    MemoryHandle AllocateMemory(long bytes);
    ComputeHandle AllocateCompute(int cpuCores, int gpuCores);
    ResourceUsage MonitorUsage();
    
    event EventHandler<ResourceUsage>? ResourceUsageChanged;
    event EventHandler<string>? ResourceWarning;
}