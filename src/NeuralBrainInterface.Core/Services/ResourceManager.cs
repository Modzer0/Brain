using System.Diagnostics;
using System.Runtime.InteropServices;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Manages system resource allocation and optimization for the neural brain interface.
/// Handles memory, CPU, and GPU resource configuration, validation, and monitoring.
/// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
/// </summary>
public class ResourceManager : IResourceManager, IDisposable
{
    private readonly object _lockObject = new();
    private readonly List<MemoryHandle> _allocatedMemory = new();
    private readonly List<ComputeHandle> _allocatedCompute = new();
    private ResourceConfig? _currentConfig;
    
    public event EventHandler<ResourceUsage>? ResourceUsageChanged;
    public event EventHandler<string>? ResourceWarning;

    public ResourceManager()
    {
    }

    /// <summary>
    /// Configures system resources based on the provided configuration.
    /// Validates resource availability and provides alternatives if insufficient.
    /// Requirements: 4.1, 4.2, 4.3, 4.4, 4.5
    /// </summary>
    public async Task<bool> ConfigureResourcesAsync(ResourceConfig config)
    {
        lock (_lockObject)
        {
            try
            {
                // First validate input parameters for invalid values
                if (config.ActiveMemoryMb <= 0)
                {
                    var alternativeConfig = GenerateAlternativeConfiguration(GetAvailableResources());
                    ResourceWarning?.Invoke(this, 
                        $"Invalid memory configuration: {config.ActiveMemoryMb}MB. Memory must be positive. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                if (config.CpuCores <= 0)
                {
                    var alternativeConfig = GenerateAlternativeConfiguration(GetAvailableResources());
                    ResourceWarning?.Invoke(this, 
                        $"Invalid CPU configuration: {config.CpuCores} cores. CPU cores must be positive. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                if (config.GpuCores < 0)
                {
                    var alternativeConfig = GenerateAlternativeConfiguration(GetAvailableResources());
                    ResourceWarning?.Invoke(this, 
                        $"Invalid GPU configuration: {config.GpuCores} cores. GPU cores cannot be negative. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                var availableResources = GetAvailableResources();
                
                // Validate memory requirements
                var requiredMemoryBytes = config.ActiveMemoryMb * 1024L * 1024L;
                if (requiredMemoryBytes > availableResources.AvailableMemoryBytes)
                {
                    // Generate alternative configuration
                    var alternativeConfig = GenerateAlternativeConfiguration(availableResources);
                    ResourceWarning?.Invoke(this, 
                        $"Insufficient memory. Requested: {config.ActiveMemoryMb}MB, Available: {availableResources.AvailableMemoryBytes / (1024 * 1024)}MB. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                // Validate CPU core requirements
                if (config.CpuCores > availableResources.AvailableCpuCores)
                {
                    var alternativeConfig = GenerateAlternativeConfiguration(availableResources);
                    ResourceWarning?.Invoke(this, 
                        $"Insufficient CPU cores. Requested: {config.CpuCores}, Available: {availableResources.AvailableCpuCores}. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                // Validate GPU core requirements (if any)
                if (config.GpuCores > 0 && config.GpuCores > availableResources.AvailableGpuCores)
                {
                    var alternativeConfig = GenerateAlternativeConfiguration(availableResources);
                    ResourceWarning?.Invoke(this, 
                        $"Insufficient GPU cores. Requested: {config.GpuCores}, Available: {availableResources.AvailableGpuCores}. " +
                        $"Alternative configuration: {alternativeConfig.ActiveMemoryMb}MB memory, {alternativeConfig.CpuCores} CPU cores, {alternativeConfig.GpuCores} GPU cores.");
                    
                    _currentConfig = alternativeConfig;
                    return false;
                }
                
                // Configuration is valid, apply it
                _currentConfig = config;
                return true;
            }
            catch (Exception ex)
            {
                ResourceWarning?.Invoke(this, $"Error configuring resources: {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Gets information about available system resources.
    /// Requirements: 4.4, 4.5
    /// </summary>
    public ResourceInfo GetAvailableResources()
    {
        try
        {
            var totalMemory = GetTotalPhysicalMemory();
            var availableMemory = GetAvailablePhysicalMemory();
            
            return new ResourceInfo
            {
                TotalMemoryBytes = totalMemory,
                AvailableMemoryBytes = availableMemory,
                TotalCpuCores = Environment.ProcessorCount,
                AvailableCpuCores = Math.Max(1, Environment.ProcessorCount - 1), // Reserve 1 core for system
                TotalGpuCores = GetTotalGpuCores(),
                AvailableGpuCores = GetAvailableGpuCores()
            };
        }
        catch (Exception ex)
        {
            ResourceWarning?.Invoke(this, $"Error getting available resources: {ex.Message}");
            
            // Return conservative defaults
            return new ResourceInfo
            {
                TotalMemoryBytes = 8L * 1024 * 1024 * 1024, // 8GB default
                AvailableMemoryBytes = 4L * 1024 * 1024 * 1024, // 4GB available
                TotalCpuCores = Environment.ProcessorCount,
                AvailableCpuCores = Math.Max(1, Environment.ProcessorCount / 2),
                TotalGpuCores = 0,
                AvailableGpuCores = 0
            };
        }
    }

    /// <summary>
    /// Allocates memory for neural network processing.
    /// Requirements: 4.4, 4.5
    /// </summary>
    public MemoryHandle AllocateMemory(long bytes)
    {
        lock (_lockObject)
        {
            try
            {
                var availableResources = GetAvailableResources();
                
                if (bytes > availableResources.AvailableMemoryBytes)
                {
                    ResourceWarning?.Invoke(this, $"Cannot allocate {bytes} bytes. Available: {availableResources.AvailableMemoryBytes} bytes.");
                    throw new OutOfMemoryException($"Insufficient memory. Requested: {bytes}, Available: {availableResources.AvailableMemoryBytes}");
                }
                
                var handle = new MemoryHandle
                {
                    AllocatedBytes = bytes,
                    Handle = Marshal.AllocHGlobal((IntPtr)bytes)
                };
                
                _allocatedMemory.Add(handle);
                return handle;
            }
            catch (Exception ex)
            {
                ResourceWarning?.Invoke(this, $"Error allocating memory: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Allocates compute resources (CPU and GPU cores).
    /// Requirements: 4.4, 4.5
    /// </summary>
    public ComputeHandle AllocateCompute(int cpuCores, int gpuCores)
    {
        lock (_lockObject)
        {
            try
            {
                var availableResources = GetAvailableResources();
                
                if (cpuCores > availableResources.AvailableCpuCores)
                {
                    ResourceWarning?.Invoke(this, $"Cannot allocate {cpuCores} CPU cores. Available: {availableResources.AvailableCpuCores}");
                    throw new InvalidOperationException($"Insufficient CPU cores. Requested: {cpuCores}, Available: {availableResources.AvailableCpuCores}");
                }
                
                if (gpuCores > 0 && gpuCores > availableResources.AvailableGpuCores)
                {
                    ResourceWarning?.Invoke(this, $"Cannot allocate {gpuCores} GPU cores. Available: {availableResources.AvailableGpuCores}");
                    throw new InvalidOperationException($"Insufficient GPU cores. Requested: {gpuCores}, Available: {availableResources.AvailableGpuCores}");
                }
                
                var handle = new ComputeHandle
                {
                    CpuCores = cpuCores,
                    GpuCores = gpuCores
                };
                
                _allocatedCompute.Add(handle);
                return handle;
            }
            catch (Exception ex)
            {
                ResourceWarning?.Invoke(this, $"Error allocating compute resources: {ex.Message}");
                throw;
            }
        }
    }

    /// <summary>
    /// Monitors current resource usage and reports statistics.
    /// Requirements: 4.4, 4.5
    /// </summary>
    public ResourceUsage MonitorUsage()
    {
        try
        {
            var cpuUsage = GetCpuUsage();
            var totalMemoryBytes = GetTotalPhysicalMemory();
            var availableMemoryBytes = GetAvailablePhysicalMemory();
            var usedMemoryBytes = totalMemoryBytes - availableMemoryBytes;
            
            var usage = new ResourceUsage
            {
                MemoryUsedBytes = usedMemoryBytes,
                CpuUsagePercent = cpuUsage,
                GpuUsagePercent = GetGpuUsage(),
                ProcessingTime = TimeSpan.Zero // Will be updated by processing operations
            };
            
            ResourceUsageChanged?.Invoke(this, usage);
            return usage;
        }
        catch (Exception ex)
        {
            ResourceWarning?.Invoke(this, $"Error monitoring resource usage: {ex.Message}");
            return new ResourceUsage();
        }
    }

    /// <summary>
    /// Generates alternative resource configuration when requested resources are insufficient.
    /// Requirements: 4.5
    /// </summary>
    private ResourceConfig GenerateAlternativeConfiguration(ResourceInfo availableResources)
    {
        var safeMemoryMb = (int)(availableResources.AvailableMemoryBytes * 0.7 / (1024 * 1024)); // Use 70% of available memory
        var safeCpuCores = Math.Max(1, availableResources.AvailableCpuCores / 2); // Use half of available CPU cores
        var safeGpuCores = Math.Max(0, availableResources.AvailableGpuCores / 2); // Use half of available GPU cores
        
        return new ResourceConfig
        {
            ActiveMemoryMb = Math.Max(512, safeMemoryMb), // Minimum 512MB
            CpuCores = safeCpuCores,
            GpuCores = safeGpuCores,
            MaxProcessingTimeMs = 10000, // Increase processing time for lower resources
            VisualizationFps = 15 // Reduce FPS for lower resources
        };
    }

    /// <summary>
    /// Gets total physical memory in bytes.
    /// </summary>
    private long GetTotalPhysicalMemory()
    {
        try
        {
            // Use GC.GetTotalMemory as a fallback approach
            // In a real implementation, this would use platform-specific APIs
            return Math.Max(8L * 1024 * 1024 * 1024, GC.GetTotalMemory(false) * 8); // Estimate based on GC memory
        }
        catch
        {
            return 8L * 1024 * 1024 * 1024; // 8GB default
        }
    }

    /// <summary>
    /// Gets available physical memory in bytes.
    /// </summary>
    private long GetAvailablePhysicalMemory()
    {
        try
        {
            var totalMemory = GetTotalPhysicalMemory();
            var usedMemory = GC.GetTotalMemory(false);
            return Math.Max(totalMemory / 2, totalMemory - (usedMemory * 4)); // Conservative estimate
        }
        catch
        {
            return 4L * 1024 * 1024 * 1024; // 4GB default
        }
    }

    /// <summary>
    /// Gets current CPU usage percentage (simplified implementation).
    /// </summary>
    private float GetCpuUsage()
    {
        try
        {
            // Simplified implementation - in a real system, this would use performance counters
            // For testing purposes, return a reasonable value
            return Math.Min(100.0f, Environment.ProcessorCount * 10.0f + (DateTime.Now.Millisecond % 50));
        }
        catch
        {
            return 25.0f; // Default moderate usage
        }
    }

    /// <summary>
    /// Gets total GPU cores (simplified implementation).
    /// </summary>
    private int GetTotalGpuCores()
    {
        // Simplified implementation - in a real system, this would query GPU drivers
        return 0; // Assume no GPU cores for now
    }

    /// <summary>
    /// Gets available GPU cores (simplified implementation).
    /// </summary>
    private int GetAvailableGpuCores()
    {
        // Simplified implementation - in a real system, this would query GPU drivers
        return 0; // Assume no GPU cores for now
    }

    /// <summary>
    /// Gets current GPU usage percentage (simplified implementation).
    /// </summary>
    private float GetGpuUsage()
    {
        // Simplified implementation - in a real system, this would query GPU performance counters
        return 0.0f;
    }

    public void Dispose()
    {
        lock (_lockObject)
        {
            // Clean up allocated memory
            foreach (var handle in _allocatedMemory)
            {
                handle.Dispose();
            }
            _allocatedMemory.Clear();
            
            // Clean up allocated compute resources
            foreach (var handle in _allocatedCompute)
            {
                handle.Dispose();
            }
            _allocatedCompute.Clear();
        }
    }
}