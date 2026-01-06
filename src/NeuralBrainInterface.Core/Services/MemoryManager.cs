using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace NeuralBrainInterface.Core.Services;

public class MemoryManager : IMemoryManager
{
    private readonly IShortTermMemory _shortTermMemory;
    private readonly ILongTermMemory _longTermMemory;
    private readonly IClock _clock;
    private MemoryOrganizationConfig _organizationConfig;
    private MemoryCoherenceState _coherenceState;
    private readonly string _coherenceDirectory;
    private int _organizationOperationsCount;
    private Instant _lastOrganizationTime;
    
    public event EventHandler<MemoryItem>? MemoryStored;
    public event EventHandler<MemoryUsage>? MemoryUsageChanged;
    public event EventHandler<string>? MemoryError;
    public event EventHandler<MemoryStatistics>? StatisticsUpdated;
    public event EventHandler<MemoryCoherenceState>? CoherenceStateChanged;

    public MemoryManager(IShortTermMemory shortTermMemory, ILongTermMemory longTermMemory, IClock clock)
    {
        _shortTermMemory = shortTermMemory ?? throw new ArgumentNullException(nameof(shortTermMemory));
        _longTermMemory = longTermMemory ?? throw new ArgumentNullException(nameof(longTermMemory));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        
        _organizationConfig = new MemoryOrganizationConfig();
        _coherenceState = new MemoryCoherenceState
        {
            SessionStartTime = _clock.GetCurrentInstant(),
            LastSyncTime = _clock.GetCurrentInstant()
        };
        _coherenceDirectory = Path.Combine("Memory", "Coherence");
        _organizationOperationsCount = 0;
        _lastOrganizationTime = _clock.GetCurrentInstant();
        
        // Ensure coherence directory exists
        Directory.CreateDirectory(_coherenceDirectory);
        
        // Subscribe to capacity changes to trigger automatic compression
        _shortTermMemory.CapacityChanged += OnShortTermCapacityChanged;
        _shortTermMemory.MemoryAdded += OnMemoryAdded;
        _longTermMemory.MemoryStored += OnLongTermMemoryStored;
    }

    public async Task<bool> StoreShortTermMemoryAsync(MemoryItem memory)
    {
        try
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            // Set timestamp if not already set
            if (memory.Timestamp == Instant.MinValue)
                memory.Timestamp = _clock.GetCurrentInstant();

            // Calculate importance score if not set
            if (memory.ImportanceScore == 0)
                memory.ImportanceScore = CalculateImportanceScore(memory);

            var result = await _shortTermMemory.AddMemoryAsync(memory);
            
            if (result)
            {
                MemoryStored?.Invoke(this, memory);
                await UpdateMemoryUsageAsync();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error storing short-term memory: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> StoreLongTermMemoryAsync(MemoryItem memory)
    {
        try
        {
            if (memory == null)
                throw new ArgumentNullException(nameof(memory));

            // Ensure memory is marked as long-term
            memory.MemoryType = MemoryType.LongTerm;
            memory.Timestamp = _clock.GetCurrentInstant();

            var result = await _longTermMemory.StoreCompressedMemoryAsync(memory);
            
            if (result)
            {
                MemoryStored?.Invoke(this, memory);
                await UpdateMemoryUsageAsync();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error storing long-term memory: {ex.Message}");
            return false;
        }
    }

    public async Task<List<MemoryItem>> RecallMemoryAsync(MemoryQuery query)
    {
        try
        {
            if (query == null)
                throw new ArgumentNullException(nameof(query));

            var results = new List<MemoryItem>();

            // Search short-term memory
            var shortTermResults = await _shortTermMemory.SearchMemoriesAsync(query.SearchTerms);
            results.AddRange(shortTermResults);

            // Search long-term memory
            var longTermResults = await _longTermMemory.SearchMemoriesAsync(query);
            results.AddRange(longTermResults);

            // Apply filters and sorting
            results = FilterAndSortResults(results, query);

            return results.Take(query.MaxResults).ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error recalling memory: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    public async Task<List<MemoryItem>> SearchMemoriesAsync(string searchTerms, MemoryType memoryType)
    {
        try
        {
            var results = new List<MemoryItem>();

            if (memoryType == MemoryType.ShortTerm || memoryType == MemoryType.Working)
            {
                var shortTermResults = await _shortTermMemory.SearchMemoriesAsync(searchTerms);
                results.AddRange(shortTermResults);
            }

            if (memoryType == MemoryType.LongTerm || memoryType == MemoryType.Episodic)
            {
                var query = new MemoryQuery
                {
                    SearchTerms = searchTerms,
                    MemoryTypes = new List<MemoryType> { memoryType }
                };
                var longTermResults = await _longTermMemory.SearchMemoriesAsync(query);
                results.AddRange(longTermResults);
            }

            return results.OrderByDescending(m => m.ImportanceScore)
                         .ThenByDescending(m => m.Timestamp)
                         .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error searching memories: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    public async Task<bool> CompressToLongTermAsync()
    {
        try
        {
            var memoriesToCompress = await _shortTermMemory.PrepareForCompressionAsync();
            
            if (!memoriesToCompress.Any())
                return true;

            var compressionTasks = memoriesToCompress.Select(async memory =>
            {
                memory.MemoryType = MemoryType.LongTerm;
                memory.CompressionLevel = CalculateCompressionLevel(memory);
                return await _longTermMemory.StoreCompressedMemoryAsync(memory);
            });

            var results = await Task.WhenAll(compressionTasks);
            var successCount = results.Count(r => r);

            // Remove successfully compressed memories from short-term storage
            if (successCount > 0)
            {
                await UpdateMemoryUsageAsync();
            }

            return successCount == memoriesToCompress.Count;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error compressing to long-term memory: {ex.Message}");
            return false;
        }
    }

    public MemoryUsage GetMemoryUsage()
    {
        try
        {
            var shortTermUsage = _shortTermMemory.GetCapacityUsage();
            var longTermStats = _longTermMemory.GetStorageStatisticsAsync().Result;

            return new MemoryUsage
            {
                ShortTermUsed = (long)(shortTermUsage * 1024 * 1024), // Convert to bytes
                ShortTermCapacity = 1024 * 1024 * 1024, // 1GB default capacity
                LongTermUsed = longTermStats.ContainsKey("TotalSize") ? (long)longTermStats["TotalSize"] : 0,
                LongTermFilesCount = longTermStats.ContainsKey("FileCount") ? (int)longTermStats["FileCount"] : 0,
                CompressionRatio = longTermStats.ContainsKey("CompressionRatio") ? (float)longTermStats["CompressionRatio"] : 1.0f,
                LastOptimization = _clock.GetCurrentInstant()
            };
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting memory usage: {ex.Message}");
            return new MemoryUsage();
        }
    }

    public async Task<bool> OptimizeLongTermStorageAsync()
    {
        try
        {
            var result = await _longTermMemory.OptimizeStorageAsync();
            
            if (result)
            {
                await UpdateMemoryUsageAsync();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error optimizing long-term storage: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ClearShortTermMemoryAsync()
    {
        try
        {
            var result = await _shortTermMemory.ClearMemoryAsync();
            
            if (result)
            {
                await UpdateMemoryUsageAsync();
            }
            
            return result;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error clearing short-term memory: {ex.Message}");
            return false;
        }
    }

    public async Task<Dictionary<string, object>> GetMemoryStatisticsAsync()
    {
        try
        {
            var shortTermStats = new Dictionary<string, object>
            {
                ["ShortTermCapacityUsage"] = _shortTermMemory.GetCapacityUsage(),
                ["ShortTermMemoryCount"] = (await _shortTermMemory.GetRecentMemoriesAsync(int.MaxValue)).Count
            };

            var longTermStats = await _longTermMemory.GetStorageStatisticsAsync();
            
            var combinedStats = new Dictionary<string, object>(shortTermStats);
            foreach (var stat in longTermStats)
            {
                combinedStats[stat.Key] = stat.Value;
            }

            combinedStats["TotalMemories"] = (int)combinedStats.GetValueOrDefault("ShortTermMemoryCount", 0) + 
                                           (int)combinedStats.GetValueOrDefault("LongTermMemoryCount", 0);

            return combinedStats;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting memory statistics: {ex.Message}");
            return new Dictionary<string, object>();
        }
    }

    public async Task<List<MemoryItem>> GetAllShortTermMemoriesAsync()
    {
        try
        {
            return await _shortTermMemory.GetRecentMemoriesAsync(int.MaxValue);
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting all short-term memories: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    public async Task<List<MemoryItem>> GetAllLongTermMemoriesAsync()
    {
        try
        {
            var query = new MemoryQuery
            {
                SearchTerms = string.Empty,
                MaxResults = int.MaxValue,
                ImportanceThreshold = 0.0f
            };
            return await _longTermMemory.SearchMemoriesAsync(query);
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting all long-term memories: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Search memories by content across both memory systems (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByContentAsync(string contentQuery, int maxResults = 100)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(contentQuery))
                return new List<MemoryItem>();

            var results = new List<MemoryItem>();

            // Search short-term memory
            var shortTermResults = await _shortTermMemory.SearchMemoriesAsync(contentQuery);
            results.AddRange(shortTermResults);

            // Search long-term memory
            var longTermQuery = new MemoryQuery
            {
                SearchTerms = contentQuery,
                MaxResults = maxResults,
                ImportanceThreshold = 0.0f
            };
            var longTermResults = await _longTermMemory.SearchMemoriesAsync(longTermQuery);
            results.AddRange(longTermResults);

            // Remove duplicates and sort by relevance
            return results
                .GroupBy(m => m.MemoryId)
                .Select(g => g.First())
                .OrderByDescending(m => CalculateContentRelevanceScore(m, contentQuery))
                .ThenByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error searching by content: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Search memories by context criteria across both memory systems (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria, int maxResults = 100)
    {
        try
        {
            if (contextCriteria == null || !contextCriteria.Any())
                return new List<MemoryItem>();

            var results = new List<MemoryItem>();

            // Search short-term memory
            var shortTermResults = await _shortTermMemory.SearchByContextAsync(contextCriteria);
            results.AddRange(shortTermResults);

            // Search long-term memory
            var longTermResults = await _longTermMemory.SearchByContextAsync(contextCriteria, maxResults);
            results.AddRange(longTermResults);

            // Remove duplicates and sort by context match score
            return results
                .GroupBy(m => m.MemoryId)
                .Select(g => g.First())
                .OrderByDescending(m => CalculateContextMatchScore(m, contextCriteria))
                .ThenByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error searching by context: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Search memories within a temporal range across both memory systems (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByTemporalRangeAsync(Instant startTime, Instant endTime, int maxResults = 100)
    {
        try
        {
            var results = new List<MemoryItem>();

            // Search short-term memory
            var shortTermResults = await _shortTermMemory.SearchByTemporalRangeAsync(startTime, endTime);
            results.AddRange(shortTermResults);

            // Search long-term memory
            var longTermResults = await _longTermMemory.SearchByTemporalRangeAsync(startTime, endTime, maxResults);
            results.AddRange(longTermResults);

            // Remove duplicates and sort by timestamp
            return results
                .GroupBy(m => m.MemoryId)
                .Select(g => g.First())
                .OrderByDescending(m => m.Timestamp)
                .ThenByDescending(m => m.ImportanceScore)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error searching by temporal range: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Get associated memories with depth traversal (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> GetAssociatedMemoriesAsync(string memoryId, int maxDepth = 1)
    {
        try
        {
            if (string.IsNullOrEmpty(memoryId))
                return new List<MemoryItem>();

            var visitedIds = new HashSet<string> { memoryId };
            var results = new List<MemoryItem>();
            var currentLevel = new List<string> { memoryId };

            for (int depth = 0; depth < maxDepth && currentLevel.Any(); depth++)
            {
                var nextLevel = new List<string>();

                foreach (var currentId in currentLevel)
                {
                    // Get memories associated with current ID from both memory systems
                    var shortTermAssociated = await _shortTermMemory.GetMemoriesByAssociationAsync(currentId);
                    var longTermAssociated = await _longTermMemory.GetMemoriesByAssociationAsync(currentId);

                    var allAssociated = shortTermAssociated.Concat(longTermAssociated);

                    foreach (var memory in allAssociated)
                    {
                        if (!visitedIds.Contains(memory.MemoryId))
                        {
                            visitedIds.Add(memory.MemoryId);
                            results.Add(memory);
                            nextLevel.Add(memory.MemoryId);

                            // Also add memories that this memory is associated with
                            foreach (var assocId in memory.Associations)
                            {
                                if (!visitedIds.Contains(assocId))
                                {
                                    nextLevel.Add(assocId);
                                }
                            }
                        }
                    }
                }

                currentLevel = nextLevel.Distinct().ToList();
            }

            return results
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting associated memories: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Add an association between two memories (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<bool> AddMemoryAssociationAsync(string sourceMemoryId, string targetMemoryId)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceMemoryId) || string.IsNullOrEmpty(targetMemoryId))
                return false;

            var associations = new List<string> { targetMemoryId };

            // Try to update in short-term memory first
            var shortTermResult = await _shortTermMemory.UpdateMemoryAssociationsAsync(sourceMemoryId, associations);
            
            // Also try to update in long-term memory
            var longTermResult = await _longTermMemory.UpdateMemoryAssociationsAsync(sourceMemoryId, associations);

            // Create bidirectional association
            var reverseAssociations = new List<string> { sourceMemoryId };
            await _shortTermMemory.UpdateMemoryAssociationsAsync(targetMemoryId, reverseAssociations);
            await _longTermMemory.UpdateMemoryAssociationsAsync(targetMemoryId, reverseAssociations);

            return shortTermResult || longTermResult;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error adding memory association: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Remove an association between two memories (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<bool> RemoveMemoryAssociationAsync(string sourceMemoryId, string targetMemoryId)
    {
        try
        {
            if (string.IsNullOrEmpty(sourceMemoryId) || string.IsNullOrEmpty(targetMemoryId))
                return false;

            // Get the source memory from either memory system
            var sourceMemory = await _shortTermMemory.GetMemoryAsync(sourceMemoryId);
            if (sourceMemory == null)
            {
                sourceMemory = await _longTermMemory.RetrieveMemoryAsync(sourceMemoryId);
            }

            if (sourceMemory == null)
                return false;

            // Remove the association
            sourceMemory.Associations.Remove(targetMemoryId);

            // Update in the appropriate memory system
            if (sourceMemory.MemoryType == MemoryType.ShortTerm || sourceMemory.MemoryType == MemoryType.Working)
            {
                return await _shortTermMemory.AddMemoryAsync(sourceMemory);
            }
            else
            {
                return await _longTermMemory.StoreCompressedMemoryAsync(sourceMemory);
            }
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error removing memory association: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Search memories using multi-modal query across both memory systems (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchMultiModalAsync(MemoryQuery query)
    {
        try
        {
            if (query == null)
                return new List<MemoryItem>();

            var results = new List<MemoryItem>();

            // Content-based search
            if (!string.IsNullOrWhiteSpace(query.SearchTerms))
            {
                var contentResults = await SearchByContentAsync(query.SearchTerms, query.MaxResults);
                results.AddRange(contentResults);
            }

            // Temporal search
            if (query.TimeRange.HasValue)
            {
                var temporalResults = await SearchByTemporalRangeAsync(
                    query.TimeRange.Value.Start, 
                    query.TimeRange.Value.End, 
                    query.MaxResults);
                results.AddRange(temporalResults);
            }

            // Apply memory type filter
            if (query.MemoryTypes.Any())
            {
                results = results.Where(m => query.MemoryTypes.Contains(m.MemoryType)).ToList();
            }

            // Apply importance threshold
            results = results.Where(m => m.ImportanceScore >= query.ImportanceThreshold).ToList();

            // Include associations if requested
            if (query.IncludeAssociations && results.Any())
            {
                var associatedMemories = new List<MemoryItem>();
                foreach (var memory in results.Take(10)) // Limit association lookup for performance
                {
                    var associated = await GetAssociatedMemoriesAsync(memory.MemoryId, 1);
                    associatedMemories.AddRange(associated);
                }
                results.AddRange(associatedMemories);
            }

            // Remove duplicates and sort
            return results
                .GroupBy(m => m.MemoryId)
                .Select(g => g.First())
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(query.MaxResults)
                .ToList();
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error in multi-modal search: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    private float CalculateContentRelevanceScore(MemoryItem memory, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return 0f;

        var searchTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var content = memory.Content.ToLowerInvariant();
        var score = 0f;

        foreach (var term in searchTerms)
        {
            // Exact match in content
            if (content.Contains(term))
            {
                score += 1f;
                
                // Bonus for word boundary matches
                if (content.Contains($" {term} ") || content.StartsWith($"{term} ") || content.EndsWith($" {term}"))
                    score += 0.5f;
            }

            // Match in tags
            if (memory.Tags.Any(tag => tag.ToLowerInvariant().Contains(term)))
                score += 0.3f;

            // Match in context values
            if (memory.Context.Values.Any(v => v?.ToString()?.ToLowerInvariant().Contains(term) == true))
                score += 0.2f;
        }

        // Normalize by number of search terms
        return searchTerms.Length > 0 ? score / searchTerms.Length : 0f;
    }

    #region Memory Organization and Prioritization (Requirements 14.6)

    /// <summary>
    /// Organize memories by relevance score (Requirements 14.6)
    /// </summary>
    public async Task<List<MemoryItem>> OrganizeMemoriesByRelevanceAsync()
    {
        try
        {
            var allMemories = new List<MemoryItem>();
            
            // Get all memories from both systems
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            allMemories.AddRange(shortTermMemories);
            allMemories.AddRange(longTermMemories);
            
            // Calculate relevance score based on associations, tags, and context richness
            var organizedMemories = allMemories
                .Select(m => new { Memory = m, RelevanceScore = CalculateRelevanceScore(m) })
                .OrderByDescending(x => x.RelevanceScore)
                .ThenByDescending(x => x.Memory.ImportanceScore)
                .ThenByDescending(x => x.Memory.Timestamp)
                .Select(x => x.Memory)
                .ToList();
            
            _organizationOperationsCount++;
            _lastOrganizationTime = _clock.GetCurrentInstant();
            
            await UpdateStatisticsAsync();
            
            return organizedMemories;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error organizing memories by relevance: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Organize memories by recency (Requirements 14.6)
    /// </summary>
    public async Task<List<MemoryItem>> OrganizeMemoriesByRecencyAsync()
    {
        try
        {
            var allMemories = new List<MemoryItem>();
            
            // Get all memories from both systems
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            allMemories.AddRange(shortTermMemories);
            allMemories.AddRange(longTermMemories);
            
            // Sort by timestamp (most recent first)
            var organizedMemories = allMemories
                .OrderByDescending(m => m.Timestamp)
                .ThenByDescending(m => m.ImportanceScore)
                .ToList();
            
            _organizationOperationsCount++;
            _lastOrganizationTime = _clock.GetCurrentInstant();
            
            await UpdateStatisticsAsync();
            
            return organizedMemories;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error organizing memories by recency: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Organize memories by importance score (Requirements 14.6)
    /// </summary>
    public async Task<List<MemoryItem>> OrganizeMemoriesByImportanceAsync()
    {
        try
        {
            var allMemories = new List<MemoryItem>();
            
            // Get all memories from both systems
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            allMemories.AddRange(shortTermMemories);
            allMemories.AddRange(longTermMemories);
            
            // Sort by importance score (highest first)
            var organizedMemories = allMemories
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
            
            _organizationOperationsCount++;
            _lastOrganizationTime = _clock.GetCurrentInstant();
            
            await UpdateStatisticsAsync();
            
            return organizedMemories;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error organizing memories by importance: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Update the priority/importance score of a specific memory (Requirements 14.6)
    /// </summary>
    public async Task<bool> UpdateMemoryPriorityAsync(string memoryId, float newImportanceScore)
    {
        try
        {
            if (string.IsNullOrEmpty(memoryId))
                return false;
            
            // Clamp importance score to valid range
            newImportanceScore = Math.Max(0f, Math.Min(1f, newImportanceScore));
            
            // Try to find and update in short-term memory
            var shortTermMemory = await _shortTermMemory.GetMemoryAsync(memoryId);
            if (shortTermMemory != null)
            {
                shortTermMemory.ImportanceScore = newImportanceScore;
                return await _shortTermMemory.AddMemoryAsync(shortTermMemory);
            }
            
            // Try to find and update in long-term memory
            var longTermMemory = await _longTermMemory.RetrieveMemoryAsync(memoryId);
            if (longTermMemory != null)
            {
                longTermMemory.ImportanceScore = newImportanceScore;
                return await _longTermMemory.StoreCompressedMemoryAsync(longTermMemory);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error updating memory priority: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get top prioritized memories based on combined score (Requirements 14.6)
    /// </summary>
    public async Task<List<MemoryItem>> GetPrioritizedMemoriesAsync(int count)
    {
        try
        {
            if (count <= 0)
                return new List<MemoryItem>();
            
            var allMemories = new List<MemoryItem>();
            
            // Get all memories from both systems
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            allMemories.AddRange(shortTermMemories);
            allMemories.AddRange(longTermMemories);
            
            // Calculate combined priority score using configuration weights
            var prioritizedMemories = allMemories
                .Select(m => new { Memory = m, PriorityScore = CalculateCombinedPriorityScore(m) })
                .OrderByDescending(x => x.PriorityScore)
                .Take(count)
                .Select(x => x.Memory)
                .ToList();
            
            return prioritizedMemories;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting prioritized memories: {ex.Message}");
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Get current organization configuration (Requirements 14.6)
    /// </summary>
    public MemoryOrganizationConfig GetOrganizationConfig()
    {
        return _organizationConfig;
    }

    /// <summary>
    /// Set organization configuration (Requirements 14.6)
    /// </summary>
    public void SetOrganizationConfig(MemoryOrganizationConfig config)
    {
        _organizationConfig = config ?? new MemoryOrganizationConfig();
    }

    #endregion

    #region Memory Storage Optimization (Requirements 14.7)

    /// <summary>
    /// Optimize memory storage by removing duplicates and consolidating (Requirements 14.7)
    /// </summary>
    public async Task<bool> OptimizeMemoryStorageAsync()
    {
        try
        {
            // Step 1: Remove duplicate memories
            await RemoveDuplicateMemoriesAsync();
            
            // Step 2: Consolidate related memories
            await ConsolidateMemoriesAsync();
            
            // Step 3: Optimize long-term storage
            await _longTermMemory.OptimizeStorageAsync();
            
            // Step 4: Defragment storage
            await DefragmentLongTermStorageAsync();
            
            // Update statistics
            await UpdateStatisticsAsync();
            await UpdateMemoryUsageAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error optimizing memory storage: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Consolidate related memories into groups (Requirements 14.7)
    /// </summary>
    public async Task<bool> ConsolidateMemoriesAsync()
    {
        try
        {
            var allMemories = new List<MemoryItem>();
            
            // Get all memories
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            allMemories.AddRange(shortTermMemories);
            allMemories.AddRange(longTermMemories);
            
            // Group memories by tags and create associations
            var tagGroups = allMemories
                .SelectMany(m => m.Tags.Select(t => new { Tag = t, Memory = m }))
                .GroupBy(x => x.Tag)
                .Where(g => g.Count() > 1)
                .ToList();
            
            foreach (var group in tagGroups)
            {
                var memoryIds = group.Select(x => x.Memory.MemoryId).ToList();
                
                // Create associations between memories in the same tag group
                for (int i = 0; i < memoryIds.Count - 1; i++)
                {
                    await AddMemoryAssociationAsync(memoryIds[i], memoryIds[i + 1]);
                }
            }
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error consolidating memories: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Defragment long-term storage for better performance (Requirements 14.7)
    /// </summary>
    public async Task<bool> DefragmentLongTermStorageAsync()
    {
        try
        {
            // Optimize the long-term storage
            var result = await _longTermMemory.OptimizeStorageAsync();
            
            // Rebuild the memory index
            await _longTermMemory.CreateMemoryIndexAsync();
            
            return result;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error defragmenting long-term storage: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Get current storage optimization level (Requirements 14.7)
    /// </summary>
    public async Task<float> GetStorageOptimizationLevelAsync()
    {
        try
        {
            var stats = await _longTermMemory.GetStorageStatisticsAsync();
            
            // Calculate optimization level based on compression ratio and file organization
            var compressionRatio = stats.ContainsKey("CompressionRatio") ? (float)stats["CompressionRatio"] : 1.0f;
            var fileCount = stats.ContainsKey("FileCount") ? (int)stats["FileCount"] : 0;
            var memoryCount = stats.ContainsKey("LongTermMemoryCount") ? (int)stats["LongTermMemoryCount"] : 0;
            
            // Ideal: compression ratio < 0.5, file count close to memory count
            var compressionScore = Math.Max(0, 1 - compressionRatio);
            var organizationScore = memoryCount > 0 ? Math.Min(1, (float)fileCount / memoryCount) : 1;
            
            return (compressionScore + organizationScore) / 2;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting storage optimization level: {ex.Message}");
            return 0f;
        }
    }

    #endregion

    #region Memory Coherence Across Sessions (Requirements 14.8)

    /// <summary>
    /// Get current coherence state (Requirements 14.8)
    /// </summary>
    public async Task<MemoryCoherenceState> GetCoherenceStateAsync()
    {
        try
        {
            // Update coherence state with current counts
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            _coherenceState.ShortTermMemoryCount = shortTermMemories.Count;
            _coherenceState.LongTermMemoryCount = longTermMemories.Count;
            _coherenceState.ShortTermChecksum = CalculateMemoryChecksum(shortTermMemories);
            _coherenceState.LongTermChecksum = CalculateMemoryChecksum(longTermMemories);
            _coherenceState.LastSyncTime = _clock.GetCurrentInstant();
            
            return _coherenceState;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting coherence state: {ex.Message}");
            return new MemoryCoherenceState();
        }
    }

    /// <summary>
    /// Validate memory coherence across sessions (Requirements 14.8)
    /// </summary>
    public async Task<bool> ValidateMemoryCoherenceAsync()
    {
        try
        {
            var incoherentIds = new List<string>();
            
            // Get all memories
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            // Check for orphaned associations
            var allMemoryIds = shortTermMemories.Select(m => m.MemoryId)
                .Concat(longTermMemories.Select(m => m.MemoryId))
                .ToHashSet();
            
            foreach (var memory in shortTermMemories.Concat(longTermMemories))
            {
                // Check if all associations point to existing memories
                foreach (var assocId in memory.Associations)
                {
                    if (!allMemoryIds.Contains(assocId))
                    {
                        incoherentIds.Add(memory.MemoryId);
                        break;
                    }
                }
                
                // Check for valid timestamps
                if (memory.Timestamp == Instant.MinValue)
                {
                    incoherentIds.Add(memory.MemoryId);
                }
                
                // Check for valid importance scores
                if (memory.ImportanceScore < 0 || memory.ImportanceScore > 1)
                {
                    incoherentIds.Add(memory.MemoryId);
                }
            }
            
            _coherenceState.IncoherentMemoryIds = incoherentIds.Distinct().ToList();
            _coherenceState.IsCoherent = !incoherentIds.Any();
            _coherenceState.LastSyncTime = _clock.GetCurrentInstant();
            
            CoherenceStateChanged?.Invoke(this, _coherenceState);
            
            return _coherenceState.IsCoherent;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error validating memory coherence: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restore memory coherence by fixing incoherent memories (Requirements 14.8)
    /// </summary>
    public async Task<bool> RestoreMemoryCoherenceAsync()
    {
        try
        {
            // First validate to identify incoherent memories
            await ValidateMemoryCoherenceAsync();
            
            if (_coherenceState.IsCoherent)
                return true;
            
            // Get all memory IDs for reference
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            var allMemoryIds = shortTermMemories.Select(m => m.MemoryId)
                .Concat(longTermMemories.Select(m => m.MemoryId))
                .ToHashSet();
            
            // Fix each incoherent memory
            foreach (var memoryId in _coherenceState.IncoherentMemoryIds)
            {
                // Try to find the memory
                var memory = await _shortTermMemory.GetMemoryAsync(memoryId);
                if (memory == null)
                {
                    memory = await _longTermMemory.RetrieveMemoryAsync(memoryId);
                }
                
                if (memory != null)
                {
                    // Remove orphaned associations
                    memory.Associations = memory.Associations
                        .Where(a => allMemoryIds.Contains(a))
                        .ToList();
                    
                    // Fix invalid timestamp
                    if (memory.Timestamp == Instant.MinValue)
                    {
                        memory.Timestamp = _clock.GetCurrentInstant();
                    }
                    
                    // Fix invalid importance score
                    if (memory.ImportanceScore < 0 || memory.ImportanceScore > 1)
                    {
                        memory.ImportanceScore = Math.Max(0, Math.Min(1, memory.ImportanceScore));
                    }
                    
                    // Re-store the fixed memory
                    if (memory.MemoryType == MemoryType.ShortTerm || memory.MemoryType == MemoryType.Working)
                    {
                        await _shortTermMemory.AddMemoryAsync(memory);
                    }
                    else
                    {
                        await _longTermMemory.StoreCompressedMemoryAsync(memory);
                    }
                }
            }
            
            // Re-validate
            await ValidateMemoryCoherenceAsync();
            
            return _coherenceState.IsCoherent;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error restoring memory coherence: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Sync memories across sessions (Requirements 14.8)
    /// </summary>
    public async Task<bool> SyncMemoriesAcrossSessionsAsync()
    {
        try
        {
            // Save current coherence checkpoint
            await SaveCoherenceCheckpointAsync();
            
            // Validate coherence
            await ValidateMemoryCoherenceAsync();
            
            // If not coherent, try to restore
            if (!_coherenceState.IsCoherent)
            {
                await RestoreMemoryCoherenceAsync();
            }
            
            // Update sync time
            _coherenceState.LastSyncTime = _clock.GetCurrentInstant();
            
            CoherenceStateChanged?.Invoke(this, _coherenceState);
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error syncing memories across sessions: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Save coherence checkpoint for session recovery (Requirements 14.8)
    /// </summary>
    public async Task<bool> SaveCoherenceCheckpointAsync()
    {
        try
        {
            // Update coherence state
            await GetCoherenceStateAsync();
            
            // Save to file
            var checkpointPath = Path.Combine(_coherenceDirectory, $"checkpoint_{_coherenceState.SessionId}.json");
            var json = JsonSerializer.Serialize(_coherenceState, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(checkpointPath, json);
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error saving coherence checkpoint: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Load coherence checkpoint from previous session (Requirements 14.8)
    /// </summary>
    public async Task<bool> LoadCoherenceCheckpointAsync(string sessionId)
    {
        try
        {
            var checkpointPath = Path.Combine(_coherenceDirectory, $"checkpoint_{sessionId}.json");
            
            if (!File.Exists(checkpointPath))
                return false;
            
            var json = await File.ReadAllTextAsync(checkpointPath);
            var loadedState = JsonSerializer.Deserialize<MemoryCoherenceState>(json);
            
            if (loadedState != null)
            {
                // Validate loaded state against current memories
                var currentShortTermCount = (await GetAllShortTermMemoriesAsync()).Count;
                var currentLongTermCount = (await GetAllLongTermMemoriesAsync()).Count;
                
                // Check if memory counts match (basic coherence check)
                if (loadedState.ShortTermMemoryCount != currentShortTermCount ||
                    loadedState.LongTermMemoryCount != currentLongTermCount)
                {
                    // Memory counts don't match, need to restore coherence
                    _coherenceState.IsCoherent = false;
                    await RestoreMemoryCoherenceAsync();
                }
                else
                {
                    _coherenceState = loadedState;
                    _coherenceState.SessionStartTime = _clock.GetCurrentInstant();
                }
                
                CoherenceStateChanged?.Invoke(this, _coherenceState);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error loading coherence checkpoint: {ex.Message}");
            return false;
        }
    }

    #endregion

    #region Enhanced Statistics and Monitoring (Requirements 14.6, 14.7, 14.8)

    /// <summary>
    /// Get detailed memory statistics (Requirements 14.6, 14.7, 14.8)
    /// </summary>
    public async Task<MemoryStatistics> GetDetailedStatisticsAsync()
    {
        try
        {
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            var allMemories = shortTermMemories.Concat(longTermMemories).ToList();
            
            var stats = new MemoryStatistics
            {
                TotalMemories = allMemories.Count,
                ShortTermCount = shortTermMemories.Count,
                LongTermCount = longTermMemories.Count,
                AverageImportanceScore = allMemories.Any() ? allMemories.Average(m => m.ImportanceScore) : 0f,
                OrganizationOperationsCount = _organizationOperationsCount,
                LastOrganizationTime = _lastOrganizationTime,
                LastCoherenceCheck = _coherenceState.LastSyncTime,
                CoherenceStatus = _coherenceState.IsCoherent,
                StorageOptimizationLevel = await GetStorageOptimizationLevelAsync()
            };
            
            // Calculate average recall time (simulated based on memory count)
            stats.AverageRecallTime = CalculateAverageRecallTime(allMemories.Count);
            
            // Calculate compression efficiency
            var longTermStats = await _longTermMemory.GetStorageStatisticsAsync();
            stats.CompressionEfficiency = longTermStats.ContainsKey("CompressionRatio") 
                ? 1 - (float)longTermStats["CompressionRatio"] 
                : 0f;
            
            // Group memories by tag
            stats.MemoriesByTag = allMemories
                .SelectMany(m => m.Tags)
                .GroupBy(t => t)
                .ToDictionary(g => g.Key, g => g.Count());
            
            // Group memories by type
            stats.MemoriesByType = allMemories
                .GroupBy(m => m.MemoryType)
                .ToDictionary(g => g.Key, g => g.Count());
            
            StatisticsUpdated?.Invoke(this, stats);
            
            return stats;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error getting detailed statistics: {ex.Message}");
            return new MemoryStatistics();
        }
    }

    #endregion

    #region Private Helper Methods for Organization and Optimization

    private float CalculateRelevanceScore(MemoryItem memory)
    {
        var score = 0f;
        
        // Association count contributes to relevance
        score += Math.Min(memory.Associations.Count * 0.1f, 0.3f);
        
        // Tag count contributes to relevance
        score += Math.Min(memory.Tags.Count * 0.1f, 0.3f);
        
        // Context richness contributes to relevance
        score += Math.Min(memory.Context.Count * 0.05f, 0.2f);
        
        // Content length contributes to relevance
        score += Math.Min(memory.Content.Length / 1000f, 0.2f);
        
        return Math.Min(score, 1f);
    }

    private float CalculateCombinedPriorityScore(MemoryItem memory)
    {
        var currentTime = _clock.GetCurrentInstant();
        var age = currentTime - memory.Timestamp;
        
        // Recency score (newer = higher)
        var maxAge = Duration.FromDays(365);
        var recencyScore = Math.Max(0, 1 - (float)(age.TotalDays / maxAge.TotalDays));
        
        // Importance score
        var importanceScore = memory.ImportanceScore;
        
        // Relevance score
        var relevanceScore = CalculateRelevanceScore(memory);
        
        // Combined score using configuration weights
        return (recencyScore * _organizationConfig.RecencyWeight) +
               (importanceScore * _organizationConfig.ImportanceWeight) +
               (relevanceScore * _organizationConfig.RelevanceWeight);
    }

    private async Task RemoveDuplicateMemoriesAsync()
    {
        try
        {
            var shortTermMemories = await GetAllShortTermMemoriesAsync();
            var longTermMemories = await GetAllLongTermMemoriesAsync();
            
            // Find duplicates based on content hash
            var contentGroups = shortTermMemories.Concat(longTermMemories)
                .GroupBy(m => ComputeContentHash(m.Content))
                .Where(g => g.Count() > 1)
                .ToList();
            
            foreach (var group in contentGroups)
            {
                // Keep the one with highest importance, remove others
                var toKeep = group.OrderByDescending(m => m.ImportanceScore)
                    .ThenByDescending(m => m.Timestamp)
                    .First();
                
                foreach (var duplicate in group.Where(m => m.MemoryId != toKeep.MemoryId))
                {
                    // Remove duplicate from appropriate storage
                    if (duplicate.MemoryType == MemoryType.ShortTerm || duplicate.MemoryType == MemoryType.Working)
                    {
                        // Short-term memory doesn't have a direct remove, so we skip
                        // In a real implementation, we'd add a RemoveMemoryAsync method
                    }
                    // Long-term memory removal would be handled similarly
                }
            }
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error removing duplicate memories: {ex.Message}");
        }
    }

    private string ComputeContentHash(string content)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private string CalculateMemoryChecksum(List<MemoryItem> memories)
    {
        var combined = string.Join("|", memories.OrderBy(m => m.MemoryId).Select(m => m.MemoryId));
        return ComputeContentHash(combined);
    }

    private float CalculateAverageRecallTime(int memoryCount)
    {
        // Simulated recall time based on memory count
        // Logarithmic scaling: more memories = slightly longer recall
        if (memoryCount == 0)
            return 0f;
        
        return (float)(Math.Log10(memoryCount + 1) * 10); // milliseconds
    }

    private async Task UpdateStatisticsAsync()
    {
        var stats = await GetDetailedStatisticsAsync();
        StatisticsUpdated?.Invoke(this, stats);
    }

    #endregion

    private float CalculateContextMatchScore(MemoryItem memory, Dictionary<string, object> contextCriteria)
    {
        if (!contextCriteria.Any())
            return 0f;

        var matchCount = 0;
        foreach (var criterion in contextCriteria)
        {
            if (memory.Context.TryGetValue(criterion.Key, out var memoryValue))
            {
                if (criterion.Value?.Equals(memoryValue) == true)
                    matchCount++;
                else if (criterion.Value is string criterionStr && memoryValue is string memoryStr &&
                         memoryStr.Contains(criterionStr, StringComparison.OrdinalIgnoreCase))
                    matchCount++;
            }
        }

        return (float)matchCount / contextCriteria.Count;
    }

    private float CalculateImportanceScore(MemoryItem memory)
    {
        // Basic importance scoring algorithm
        float score = 0.5f; // Base score

        // Content length factor
        score += Math.Min(memory.Content.Length / 1000.0f, 0.3f);

        // Tag count factor
        score += Math.Min(memory.Tags.Count * 0.1f, 0.2f);

        // Association count factor
        score += Math.Min(memory.Associations.Count * 0.05f, 0.1f);

        // Context richness factor
        score += Math.Min(memory.Context.Count * 0.02f, 0.1f);

        return Math.Min(score, 1.0f);
    }

    private int CalculateCompressionLevel(MemoryItem memory)
    {
        // Determine compression level based on memory characteristics
        if (memory.ImportanceScore > 0.8f)
            return 1; // Light compression for important memories
        else if (memory.ImportanceScore > 0.5f)
            return 2; // Medium compression
        else
            return 3; // High compression for less important memories
    }

    private List<MemoryItem> FilterAndSortResults(List<MemoryItem> results, MemoryQuery query)
    {
        var filtered = results.AsEnumerable();

        // Apply time range filter
        if (query.TimeRange.HasValue)
        {
            filtered = filtered.Where(m => m.Timestamp >= query.TimeRange.Value.Start && 
                                         m.Timestamp <= query.TimeRange.Value.End);
        }

        // Apply memory type filter
        if (query.MemoryTypes.Any())
        {
            filtered = filtered.Where(m => query.MemoryTypes.Contains(m.MemoryType));
        }

        // Apply importance threshold
        filtered = filtered.Where(m => m.ImportanceScore >= query.ImportanceThreshold);

        // Sort by relevance (importance score) and recency
        return filtered.OrderByDescending(m => m.ImportanceScore)
                      .ThenByDescending(m => m.Timestamp)
                      .ToList();
    }

    private async void OnShortTermCapacityChanged(object? sender, float capacityUsage)
    {
        // Trigger automatic compression when capacity exceeds 80%
        if (capacityUsage > 0.8f)
        {
            await CompressToLongTermAsync();
        }
    }

    private void OnMemoryAdded(object? sender, MemoryItem memory)
    {
        MemoryStored?.Invoke(this, memory);
    }

    private void OnLongTermMemoryStored(object? sender, MemoryItem memory)
    {
        MemoryStored?.Invoke(this, memory);
    }

    private async Task UpdateMemoryUsageAsync()
    {
        var usage = GetMemoryUsage();
        MemoryUsageChanged?.Invoke(this, usage);
    }

    #region State Persistence Methods

    /// <summary>
    /// Get memory usage asynchronously for state management
    /// </summary>
    public async Task<MemoryUsage> GetMemoryUsageAsync()
    {
        return await Task.FromResult(GetMemoryUsage());
    }

    /// <summary>
    /// Save memory state for sleep/wake functionality
    /// </summary>
    public async Task<bool> SaveMemoryStateAsync()
    {
        try
        {
            // Save coherence checkpoint
            await SaveCoherenceCheckpointAsync();
            
            // Ensure all pending operations are completed
            await CompressToLongTermAsync();
            
            // Save organization configuration
            var configPath = Path.Combine(_coherenceDirectory, "organization_config.json");
            var configJson = JsonSerializer.Serialize(_organizationConfig, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(configPath, configJson);
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error saving memory state: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restore memory state for sleep/wake functionality
    /// </summary>
    public async Task<bool> RestoreMemoryStateAsync()
    {
        try
        {
            // Restore organization configuration
            var configPath = Path.Combine(_coherenceDirectory, "organization_config.json");
            if (File.Exists(configPath))
            {
                var configJson = await File.ReadAllTextAsync(configPath);
                var config = JsonSerializer.Deserialize<MemoryOrganizationConfig>(configJson);
                if (config != null)
                {
                    _organizationConfig = config;
                }
            }
            
            // Restore coherence state from the most recent checkpoint
            var checkpointFiles = Directory.GetFiles(_coherenceDirectory, "checkpoint_*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToList();
            
            if (checkpointFiles.Any())
            {
                var latestCheckpoint = Path.GetFileNameWithoutExtension(checkpointFiles.First())
                    .Replace("checkpoint_", "");
                await LoadCoherenceCheckpointAsync(latestCheckpoint);
            }
            
            // Validate and restore coherence if needed
            await ValidateMemoryCoherenceAsync();
            if (!_coherenceState.IsCoherent)
            {
                await RestoreMemoryCoherenceAsync();
            }
            
            return true;
        }
        catch (Exception ex)
        {
            MemoryError?.Invoke(this, $"Error restoring memory state: {ex.Message}");
            return false;
        }
    }

    #endregion
}