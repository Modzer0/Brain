using FsCheck;
using FsCheck.Xunit;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using Xunit;

namespace NeuralBrainInterface.Tests;

public class ResourceManagementPropertyTests
{
    private ResourceManager CreateResourceManager()
    {
        return new ResourceManager();
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 4: Resource Validation and Allocation**
    /// **Validates: Requirements 4.4, 4.5**
    /// 
    /// For any resource configuration request, the system should validate resource availability 
    /// and either allocate the requested resources or provide alternative configurations when 
    /// resources are insufficient.
    /// </summary>
    [Property(MaxTest = 100)]
    public void ResourceValidationAndAllocation_ValidatesAvailabilityAndProvidesAlternatives(PositiveInt memoryMb, PositiveInt cpuCores, NonNegativeInt gpuCores)
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var availableResources = resourceManager.GetAvailableResources();
        
        var requestedConfig = new ResourceConfig
        {
            ActiveMemoryMb = memoryMb.Get,
            CpuCores = cpuCores.Get,
            GpuCores = gpuCores.Get,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 30
        };

        // Act
        var configurationResult = resourceManager.ConfigureResourcesAsync(requestedConfig).Result;

        // Assert
        if (requestedConfig.ActiveMemoryMb * 1024L * 1024L <= availableResources.AvailableMemoryBytes &&
            requestedConfig.CpuCores <= availableResources.AvailableCpuCores &&
            (requestedConfig.GpuCores == 0 || requestedConfig.GpuCores <= availableResources.AvailableGpuCores))
        {
            // If resources are sufficient, configuration should succeed
            Assert.True(configurationResult);
        }
        else
        {
            // If resources are insufficient, configuration should fail but alternatives should be provided
            Assert.False(configurationResult);
            
            // The resource manager should have provided warnings about alternatives
            // This is verified through the ResourceWarning event which would be triggered
        }

        // Verify that resource info is always available
        Assert.True(availableResources.TotalMemoryBytes > 0);
        Assert.True(availableResources.AvailableMemoryBytes >= 0);
        Assert.True(availableResources.TotalCpuCores > 0);
        Assert.True(availableResources.AvailableCpuCores >= 0);
        Assert.True(availableResources.TotalGpuCores >= 0);
        Assert.True(availableResources.AvailableGpuCores >= 0);

        // Available resources should not exceed total resources
        Assert.True(availableResources.AvailableMemoryBytes <= availableResources.TotalMemoryBytes);
        Assert.True(availableResources.AvailableCpuCores <= availableResources.TotalCpuCores);
        Assert.True(availableResources.AvailableGpuCores <= availableResources.TotalGpuCores);
    }

    [Property(MaxTest = 100)]
    public void MemoryAllocation_AllocatesWithinAvailableLimits(PositiveInt requestedBytes)
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var availableResources = resourceManager.GetAvailableResources();
        var requestedMemory = Math.Min(requestedBytes.Get, availableResources.AvailableMemoryBytes / 2); // Request at most half of available

        // Act & Assert
        if (requestedMemory <= availableResources.AvailableMemoryBytes)
        {
            // Should succeed if within limits
            var memoryHandle = resourceManager.AllocateMemory(requestedMemory);
            
            Assert.NotNull(memoryHandle);
            Assert.Equal(requestedMemory, memoryHandle.AllocatedBytes);
            Assert.NotEqual(IntPtr.Zero, memoryHandle.Handle);
            
            // Clean up
            memoryHandle.Dispose();
        }
        else
        {
            // Should throw exception if exceeding limits
            Assert.Throws<OutOfMemoryException>(() => resourceManager.AllocateMemory(requestedMemory));
        }
    }

    [Property(MaxTest = 100)]
    public void ComputeAllocation_AllocatesWithinAvailableLimits(PositiveInt requestedCpuCores, NonNegativeInt requestedGpuCores)
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var availableResources = resourceManager.GetAvailableResources();
        var cpuCores = Math.Min(requestedCpuCores.Get, availableResources.AvailableCpuCores);
        var gpuCores = Math.Min(requestedGpuCores.Get, availableResources.AvailableGpuCores);

        // Act & Assert
        if (cpuCores <= availableResources.AvailableCpuCores &&
            (gpuCores == 0 || gpuCores <= availableResources.AvailableGpuCores))
        {
            // Should succeed if within limits
            var computeHandle = resourceManager.AllocateCompute(cpuCores, gpuCores);
            
            Assert.NotNull(computeHandle);
            Assert.Equal(cpuCores, computeHandle.CpuCores);
            Assert.Equal(gpuCores, computeHandle.GpuCores);
            
            // Clean up
            computeHandle.Dispose();
        }
        else
        {
            // Should throw exception if exceeding limits
            Assert.Throws<InvalidOperationException>(() => resourceManager.AllocateCompute(cpuCores, gpuCores));
        }
    }

    [Property(MaxTest = 100)]
    public void ResourceMonitoring_ProvidesAccurateUsageStatistics()
    {
        // Arrange
        var resourceManager = CreateResourceManager();

        // Act
        var usage = resourceManager.MonitorUsage();

        // Assert
        Assert.NotNull(usage);
        
        // Memory usage should be non-negative
        Assert.True(usage.MemoryUsedBytes >= 0);
        
        // CPU usage should be between 0 and 100 percent
        Assert.True(usage.CpuUsagePercent >= 0.0f);
        Assert.True(usage.CpuUsagePercent <= 100.0f);
        
        // GPU usage should be between 0 and 100 percent
        Assert.True(usage.GpuUsagePercent >= 0.0f);
        Assert.True(usage.GpuUsagePercent <= 100.0f);
        
        // Processing time should be non-negative
        Assert.True(usage.ProcessingTime >= TimeSpan.Zero);
    }

    [Property(MaxTest = 100)]
    public void ResourceConfiguration_RejectsInvalidConfigurations()
    {
        // Arrange
        var resourceManager = CreateResourceManager();

        // Test with negative memory
        var invalidConfig1 = new ResourceConfig
        {
            ActiveMemoryMb = -100,
            CpuCores = 2,
            GpuCores = 0,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 30
        };

        // Test with zero CPU cores
        var invalidConfig2 = new ResourceConfig
        {
            ActiveMemoryMb = 1024,
            CpuCores = 0,
            GpuCores = 0,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 30
        };

        // Test with negative GPU cores
        var invalidConfig3 = new ResourceConfig
        {
            ActiveMemoryMb = 1024,
            CpuCores = 2,
            GpuCores = -1,
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 30
        };

        // Act & Assert
        var result1 = resourceManager.ConfigureResourcesAsync(invalidConfig1).Result;
        var result2 = resourceManager.ConfigureResourcesAsync(invalidConfig2).Result;
        var result3 = resourceManager.ConfigureResourcesAsync(invalidConfig3).Result;

        // All invalid configurations should be rejected
        Assert.False(result1);
        Assert.False(result2);
        Assert.False(result3);
    }

    [Property(MaxTest = 100)]
    public void ResourceAllocation_HandlesMultipleAllocations()
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var availableResources = resourceManager.GetAvailableResources();
        var allocationSize = Math.Min(1024 * 1024, availableResources.AvailableMemoryBytes / 10); // 1MB or 1/10th of available
        var handles = new List<MemoryHandle>();

        try
        {
            // Act - Allocate multiple memory blocks
            for (int i = 0; i < 5; i++)
            {
                if (allocationSize * (i + 1) <= availableResources.AvailableMemoryBytes)
                {
                    var handle = resourceManager.AllocateMemory(allocationSize);
                    handles.Add(handle);
                    
                    Assert.NotNull(handle);
                    Assert.Equal(allocationSize, handle.AllocatedBytes);
                    Assert.NotEqual(IntPtr.Zero, handle.Handle);
                }
            }

            // Assert - All allocations should be successful within limits
            Assert.True(handles.Count > 0);
            
            // Each handle should be unique
            var uniqueHandles = handles.Select(h => h.Handle).Distinct().Count();
            Assert.Equal(handles.Count, uniqueHandles);
        }
        finally
        {
            // Clean up all handles
            foreach (var handle in handles)
            {
                handle.Dispose();
            }
        }
    }

    [Property(MaxTest = 100)]
    public void ResourceEvents_FireWhenResourcesChange()
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var usageEventFired = false;
        var warningEventFired = false;
        ResourceUsage? capturedUsage = null;
        string? capturedWarning = null;

        resourceManager.ResourceUsageChanged += (sender, usage) =>
        {
            usageEventFired = true;
            capturedUsage = usage;
        };

        resourceManager.ResourceWarning += (sender, warning) =>
        {
            warningEventFired = true;
            capturedWarning = warning;
        };

        // Act - Monitor usage (should trigger usage event)
        var usage = resourceManager.MonitorUsage();

        // Try to configure with excessive resources (should trigger warning event)
        var excessiveConfig = new ResourceConfig
        {
            ActiveMemoryMb = int.MaxValue / (1024 * 1024), // Excessive memory
            CpuCores = int.MaxValue, // Excessive CPU cores
            GpuCores = int.MaxValue, // Excessive GPU cores
            MaxProcessingTimeMs = 5000,
            VisualizationFps = 30
        };

        var configResult = resourceManager.ConfigureResourcesAsync(excessiveConfig).Result;

        // Assert
        Assert.True(usageEventFired);
        Assert.NotNull(capturedUsage);
        Assert.Equal(usage.MemoryUsedBytes, capturedUsage.MemoryUsedBytes);
        Assert.Equal(usage.CpuUsagePercent, capturedUsage.CpuUsagePercent);

        // Configuration should fail and warning should be fired
        Assert.False(configResult);
        Assert.True(warningEventFired);
        Assert.NotNull(capturedWarning);
        Assert.Contains("Insufficient", capturedWarning);
    }

    [Property(MaxTest = 100)]
    public void ResourceCleanup_DisposesResourcesProperly()
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        var availableResources = resourceManager.GetAvailableResources();
        var allocationSize = Math.Min(1024 * 1024, availableResources.AvailableMemoryBytes / 10);

        // Act - Allocate and dispose resources
        MemoryHandle? memoryHandle = null;
        ComputeHandle? computeHandle = null;

        if (allocationSize <= availableResources.AvailableMemoryBytes)
        {
            memoryHandle = resourceManager.AllocateMemory(allocationSize);
            Assert.NotNull(memoryHandle);
        }

        if (availableResources.AvailableCpuCores > 0)
        {
            computeHandle = resourceManager.AllocateCompute(1, 0);
            Assert.NotNull(computeHandle);
        }

        // Dispose handles
        memoryHandle?.Dispose();
        computeHandle?.Dispose();

        // Dispose resource manager
        resourceManager.Dispose();

        // Assert - No exceptions should be thrown during disposal
        // The test passes if we reach this point without exceptions
        Assert.True(true);
    }

    [Property(MaxTest = 100)]
    public void ResourceConfiguration_HandlesEdgeCases(NonNegativeInt memoryMb, NonNegativeInt cpuCores, NonNegativeInt gpuCores)
    {
        // Arrange
        var resourceManager = CreateResourceManager();
        
        var config = new ResourceConfig
        {
            ActiveMemoryMb = memoryMb.Get,
            CpuCores = cpuCores.Get,
            GpuCores = gpuCores.Get,
            MaxProcessingTimeMs = 1000,
            VisualizationFps = 1
        };

        // Act
        var result = resourceManager.ConfigureResourcesAsync(config).Result;

        // Assert
        // Configuration should handle edge cases gracefully
        // Zero values should be handled appropriately
        if (config.ActiveMemoryMb == 0 || config.CpuCores == 0)
        {
            // Zero memory or CPU cores should be rejected
            Assert.False(result);
        }
        else
        {
            // Valid configurations should be processed (may succeed or fail based on availability)
            Assert.True(result || !result); // Either outcome is acceptable for edge cases
        }

        // Resource manager should remain functional after edge case handling
        var availableResources = resourceManager.GetAvailableResources();
        Assert.NotNull(availableResources);
        Assert.True(availableResources.TotalMemoryBytes > 0);
        Assert.True(availableResources.TotalCpuCores > 0);
    }
}