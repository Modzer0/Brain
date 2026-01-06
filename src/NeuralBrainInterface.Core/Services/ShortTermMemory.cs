using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;
using System.Collections.Concurrent;

namespace NeuralBrainInterface.Core.Services;

public class ShortTermMemory : IShortTermMemory
{
    private readonly ConcurrentDictionary<string, MemoryItem> _memories;
    private readonly IClock _clock;
    private readonly long _maxCapacityBytes;
    private long _currentUsageBytes;
    
    public event EventHandler<MemoryItem>? MemoryAdded;
    public event EventHandler<float>? CapacityChanged;

    public ShortTermMemory(IClock clock, long maxCapacityBytes = 1024 * 1024 * 1024) // 1GB default
    {
        _memories = new ConcurrentDictionary<string, MemoryItem>();
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _maxCapacityBytes = maxCapacityBytes;
        _currentUsageBytes = 0;
    }

    public async Task<bool> AddMemoryAsync(MemoryItem memory)
    {
        if (memory == null)
            return false;

        try
        {
            // Estimate memory size
            var estimatedSize = EstimateMemorySize(memory);
            
            // Check if adding this memory would exceed capacity
            if (_currentUsageBytes + estimatedSize > _maxCapacityBytes)
            {
                // Try to make space by removing least important memories
                await MakeSpaceAsync(estimatedSize);
            }

            // Set timestamp if not already set
            if (memory.Timestamp == Instant.MinValue)
                memory.Timestamp = _clock.GetCurrentInstant();

            // Set memory type to short-term
            memory.MemoryType = MemoryType.ShortTerm;

            // Add or update memory
            _memories.AddOrUpdate(memory.MemoryId, memory, (key, existing) => memory);
            
            // Update usage tracking
            Interlocked.Add(ref _currentUsageBytes, estimatedSize);
            
            // Notify listeners
            MemoryAdded?.Invoke(this, memory);
            CapacityChanged?.Invoke(this, GetCapacityUsage());
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<MemoryItem?> GetMemoryAsync(string memoryId)
    {
        if (string.IsNullOrEmpty(memoryId))
            return null;

        return await Task.FromResult(_memories.TryGetValue(memoryId, out var memory) ? memory : null);
    }

    public async Task<List<MemoryItem>> SearchMemoriesAsync(string query)
    {
        if (string.IsNullOrEmpty(query))
            return new List<MemoryItem>();

        var searchTerms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        
        var results = await Task.Run(() =>
        {
            return _memories.Values
                .Where(memory => MatchesSearchTerms(memory, searchTerms))
                .OrderByDescending(m => CalculateRelevanceScore(m, searchTerms))
                .ThenByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
        });

        return results;
    }

    public async Task<List<MemoryItem>> GetRecentMemoriesAsync(int count)
    {
        if (count <= 0)
            return new List<MemoryItem>();

        return await Task.FromResult(
            _memories.Values
                .OrderByDescending(m => m.Timestamp)
                .Take(count)
                .ToList()
        );
    }

    public float GetCapacityUsage()
    {
        return _maxCapacityBytes > 0 ? (float)_currentUsageBytes / _maxCapacityBytes : 0f;
    }

    public async Task<List<MemoryItem>> PrepareForCompressionAsync()
    {
        // Select memories for compression based on age and importance
        var compressionCandidates = await Task.Run(() =>
        {
            var currentTime = _clock.GetCurrentInstant();
            var cutoffTime = currentTime - Duration.FromHours(1); // Memories older than 1 hour

            return _memories.Values
                .Where(m => m.Timestamp < cutoffTime || m.ImportanceScore < 0.3f)
                .OrderBy(m => m.ImportanceScore) // Least important first
                .ThenBy(m => m.Timestamp) // Oldest first
                .Take(_memories.Count / 2) // Compress up to half of memories
                .ToList();
        });

        // Remove selected memories from short-term storage
        foreach (var memory in compressionCandidates)
        {
            if (_memories.TryRemove(memory.MemoryId, out var removedMemory))
            {
                var estimatedSize = EstimateMemorySize(removedMemory);
                Interlocked.Add(ref _currentUsageBytes, -estimatedSize);
            }
        }

        // Notify capacity change
        CapacityChanged?.Invoke(this, GetCapacityUsage());

        return compressionCandidates;
    }

    public async Task<bool> ClearMemoryAsync()
    {
        try
        {
            _memories.Clear();
            Interlocked.Exchange(ref _currentUsageBytes, 0);
            
            CapacityChanged?.Invoke(this, 0f);
            
            return await Task.FromResult(true);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Search memories by context criteria (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria)
    {
        if (contextCriteria == null || !contextCriteria.Any())
            return new List<MemoryItem>();

        return await Task.Run(() =>
        {
            return _memories.Values
                .Where(memory => MatchesContextCriteria(memory, contextCriteria))
                .OrderByDescending(m => CalculateContextMatchScore(m, contextCriteria))
                .ThenByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
        });
    }

    /// <summary>
    /// Search memories within a temporal range (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByTemporalRangeAsync(Instant startTime, Instant endTime)
    {
        return await Task.Run(() =>
        {
            return _memories.Values
                .Where(memory => memory.Timestamp >= startTime && memory.Timestamp <= endTime)
                .OrderByDescending(m => m.Timestamp)
                .ThenByDescending(m => m.ImportanceScore)
                .ToList();
        });
    }

    /// <summary>
    /// Get memories by association ID (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> GetMemoriesByAssociationAsync(string associationId)
    {
        if (string.IsNullOrEmpty(associationId))
            return new List<MemoryItem>();

        return await Task.Run(() =>
        {
            return _memories.Values
                .Where(memory => memory.Associations.Contains(associationId) || 
                                memory.MemoryId == associationId)
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .ToList();
        });
    }

    /// <summary>
    /// Update memory associations (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<bool> UpdateMemoryAssociationsAsync(string memoryId, List<string> associations)
    {
        if (string.IsNullOrEmpty(memoryId) || associations == null)
            return false;

        try
        {
            if (_memories.TryGetValue(memoryId, out var memory))
            {
                // Add new associations while preserving existing ones
                var updatedAssociations = memory.Associations.Union(associations).ToList();
                memory.Associations = updatedAssociations;
                
                // Update the memory in the dictionary
                _memories.AddOrUpdate(memoryId, memory, (key, existing) => memory);
                
                return await Task.FromResult(true);
            }
            
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool MatchesContextCriteria(MemoryItem memory, Dictionary<string, object> contextCriteria)
    {
        foreach (var criterion in contextCriteria)
        {
            if (!memory.Context.TryGetValue(criterion.Key, out var memoryValue))
                return false;

            // Handle different comparison types
            if (criterion.Value == null && memoryValue == null)
                continue;

            if (criterion.Value == null || memoryValue == null)
                return false;

            // String comparison (case-insensitive contains)
            if (criterion.Value is string criterionStr && memoryValue is string memoryStr)
            {
                if (!memoryStr.Contains(criterionStr, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            // Exact match for other types
            else if (!criterion.Value.Equals(memoryValue))
            {
                return false;
            }
        }
        
        return true;
    }

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

    private long EstimateMemorySize(MemoryItem memory)
    {
        // Rough estimation of memory size in bytes
        long size = 0;
        
        // Content size
        size += memory.Content.Length * sizeof(char);
        
        // Context dictionary size (rough estimate)
        size += memory.Context.Count * 100; // Average 100 bytes per context item
        
        // Tags size
        size += memory.Tags.Sum(tag => tag.Length * sizeof(char));
        
        // Associations size
        size += memory.Associations.Sum(assoc => assoc.Length * sizeof(char));
        
        // Base object overhead
        size += 200; // Estimated object overhead
        
        return size;
    }

    private async Task MakeSpaceAsync(long requiredSpace)
    {
        var spaceFreed = 0L;
        var memoriesToRemove = new List<string>();

        // Remove least important and oldest memories until we have enough space
        var sortedMemories = _memories.Values
            .OrderBy(m => m.ImportanceScore)
            .ThenBy(m => m.Timestamp)
            .ToList();

        foreach (var memory in sortedMemories)
        {
            if (spaceFreed >= requiredSpace)
                break;

            var estimatedSize = EstimateMemorySize(memory);
            memoriesToRemove.Add(memory.MemoryId);
            spaceFreed += estimatedSize;
        }

        // Remove selected memories
        foreach (var memoryId in memoriesToRemove)
        {
            if (_memories.TryRemove(memoryId, out var removedMemory))
            {
                var estimatedSize = EstimateMemorySize(removedMemory);
                Interlocked.Add(ref _currentUsageBytes, -estimatedSize);
            }
        }

        await Task.CompletedTask;
    }

    private bool MatchesSearchTerms(MemoryItem memory, string[] searchTerms)
    {
        var searchableText = $"{memory.Content} {string.Join(" ", memory.Tags)} {string.Join(" ", memory.Context.Values.Select(v => v.ToString()))}".ToLowerInvariant();
        
        return searchTerms.Any(term => searchableText.Contains(term));
    }

    private float CalculateRelevanceScore(MemoryItem memory, string[] searchTerms)
    {
        var searchableText = memory.Content.ToLowerInvariant();
        var score = 0f;

        foreach (var term in searchTerms)
        {
            // Exact matches get higher scores
            if (searchableText.Contains(term))
            {
                score += 1f;
                
                // Bonus for matches in tags
                if (memory.Tags.Any(tag => tag.ToLowerInvariant().Contains(term)))
                    score += 0.5f;
            }
        }

        // Normalize by number of search terms
        return searchTerms.Length > 0 ? score / searchTerms.Length : 0f;
    }
}