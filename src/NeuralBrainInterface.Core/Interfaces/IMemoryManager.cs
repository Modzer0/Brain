using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IMemoryManager
{
    Task<bool> StoreShortTermMemoryAsync(MemoryItem memory);
    Task<bool> StoreLongTermMemoryAsync(MemoryItem memory);
    Task<List<MemoryItem>> RecallMemoryAsync(MemoryQuery query);
    Task<List<MemoryItem>> SearchMemoriesAsync(string searchTerms, MemoryType memoryType);
    
    // Enhanced search and recall functionality (Requirements 14.4, 14.5)
    Task<List<MemoryItem>> SearchByContentAsync(string contentQuery, int maxResults = 100);
    Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria, int maxResults = 100);
    Task<List<MemoryItem>> SearchByTemporalRangeAsync(NodaTime.Instant startTime, NodaTime.Instant endTime, int maxResults = 100);
    Task<List<MemoryItem>> GetAssociatedMemoriesAsync(string memoryId, int maxDepth = 1);
    Task<bool> AddMemoryAssociationAsync(string sourceMemoryId, string targetMemoryId);
    Task<bool> RemoveMemoryAssociationAsync(string sourceMemoryId, string targetMemoryId);
    Task<List<MemoryItem>> SearchMultiModalAsync(MemoryQuery query);
    
    Task<bool> CompressToLongTermAsync();
    MemoryUsage GetMemoryUsage();
    Task<MemoryUsage> GetMemoryUsageAsync();
    Task<bool> OptimizeLongTermStorageAsync();
    Task<bool> ClearShortTermMemoryAsync();
    
    // State persistence methods for sleep/wake functionality
    Task<bool> SaveMemoryStateAsync();
    Task<bool> RestoreMemoryStateAsync();
    
    Task<Dictionary<string, object>> GetMemoryStatisticsAsync();
    Task<List<MemoryItem>> GetAllShortTermMemoriesAsync();
    Task<List<MemoryItem>> GetAllLongTermMemoriesAsync();
    
    // Memory organization and prioritization (Requirements 14.6)
    Task<List<MemoryItem>> OrganizeMemoriesByRelevanceAsync();
    Task<List<MemoryItem>> OrganizeMemoriesByRecencyAsync();
    Task<List<MemoryItem>> OrganizeMemoriesByImportanceAsync();
    Task<bool> UpdateMemoryPriorityAsync(string memoryId, float newImportanceScore);
    Task<List<MemoryItem>> GetPrioritizedMemoriesAsync(int count);
    MemoryOrganizationConfig GetOrganizationConfig();
    void SetOrganizationConfig(MemoryOrganizationConfig config);
    
    // Memory storage optimization (Requirements 14.7)
    Task<bool> OptimizeMemoryStorageAsync();
    Task<bool> ConsolidateMemoriesAsync();
    Task<bool> DefragmentLongTermStorageAsync();
    Task<float> GetStorageOptimizationLevelAsync();
    
    // Memory coherence across sessions (Requirements 14.8)
    Task<MemoryCoherenceState> GetCoherenceStateAsync();
    Task<bool> ValidateMemoryCoherenceAsync();
    Task<bool> RestoreMemoryCoherenceAsync();
    Task<bool> SyncMemoriesAcrossSessionsAsync();
    Task<bool> SaveCoherenceCheckpointAsync();
    Task<bool> LoadCoherenceCheckpointAsync(string sessionId);
    
    // Enhanced statistics and monitoring (Requirements 14.6, 14.7, 14.8)
    Task<MemoryStatistics> GetDetailedStatisticsAsync();
    
    event EventHandler<MemoryItem>? MemoryStored;
    event EventHandler<MemoryUsage>? MemoryUsageChanged;
    event EventHandler<string>? MemoryError;
    event EventHandler<MemoryStatistics>? StatisticsUpdated;
    event EventHandler<MemoryCoherenceState>? CoherenceStateChanged;
}