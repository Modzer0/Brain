using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface ILongTermMemory
{
    Task<bool> StoreCompressedMemoryAsync(MemoryItem memory);
    Task<MemoryItem?> RetrieveMemoryAsync(string memoryId);
    Task<List<MemoryItem>> SearchMemoriesAsync(MemoryQuery query);
    
    // Enhanced search functionality (Requirements 14.4, 14.5)
    Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria, int maxResults = 100);
    Task<List<MemoryItem>> SearchByTemporalRangeAsync(NodaTime.Instant startTime, NodaTime.Instant endTime, int maxResults = 100);
    Task<List<MemoryItem>> GetMemoriesByAssociationAsync(string associationId, int maxResults = 100);
    Task<bool> UpdateMemoryAssociationsAsync(string memoryId, List<string> associations);
    
    Task<bool> OptimizeStorageAsync();
    Task<Dictionary<string, object>> GetStorageStatisticsAsync();
    Task<bool> CreateMemoryIndexAsync();
    Task<bool> BackupMemoryFilesAsync();
    
    event EventHandler<MemoryItem>? MemoryStored;
    event EventHandler<string>? StorageOptimized;
}