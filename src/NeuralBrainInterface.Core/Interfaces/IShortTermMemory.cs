using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IShortTermMemory
{
    Task<bool> AddMemoryAsync(MemoryItem memory);
    Task<MemoryItem?> GetMemoryAsync(string memoryId);
    Task<List<MemoryItem>> SearchMemoriesAsync(string query);
    Task<List<MemoryItem>> GetRecentMemoriesAsync(int count);
    
    // Enhanced search functionality (Requirements 14.4, 14.5)
    Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria);
    Task<List<MemoryItem>> SearchByTemporalRangeAsync(NodaTime.Instant startTime, NodaTime.Instant endTime);
    Task<List<MemoryItem>> GetMemoriesByAssociationAsync(string associationId);
    Task<bool> UpdateMemoryAssociationsAsync(string memoryId, List<string> associations);
    
    float GetCapacityUsage();
    Task<List<MemoryItem>> PrepareForCompressionAsync();
    Task<bool> ClearMemoryAsync();
    
    event EventHandler<MemoryItem>? MemoryAdded;
    event EventHandler<float>? CapacityChanged;
}