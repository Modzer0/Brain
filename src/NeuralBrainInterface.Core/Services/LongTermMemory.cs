using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.Json;

namespace NeuralBrainInterface.Core.Services;

public class LongTermMemory : ILongTermMemory
{
    private readonly IClock _clock;
    private readonly string _storageDirectory;
    private readonly ConcurrentDictionary<string, string> _memoryIndex; // MemoryId -> FilePath
    private readonly object _fileLock = new object();
    
    public event EventHandler<MemoryItem>? MemoryStored;
    public event EventHandler<string>? StorageOptimized;

    public LongTermMemory(IClock clock, string storageDirectory = "Memory/LongTerm")
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _storageDirectory = storageDirectory;
        _memoryIndex = new ConcurrentDictionary<string, string>();
        
        // Ensure storage directory exists
        Directory.CreateDirectory(_storageDirectory);
        
        // Load existing memory index
        LoadMemoryIndex();
    }

    public async Task<bool> StoreCompressedMemoryAsync(MemoryItem memory)
    {
        if (memory == null)
            return false;

        try
        {
            // Set timestamp if not already set
            if (memory.Timestamp == Instant.MinValue)
                memory.Timestamp = _clock.GetCurrentInstant();

            // Generate file path based on timestamp and memory ID
            var fileName = $"{memory.Timestamp.ToUnixTimeSeconds()}_{memory.MemoryId}.longterm";
            var filePath = Path.Combine(_storageDirectory, fileName);

            // Serialize and compress memory
            var compressedData = await CompressMemoryAsync(memory);
            
            lock (_fileLock)
            {
                File.WriteAllBytes(filePath, compressedData);
            }

            // Update index
            _memoryIndex.AddOrUpdate(memory.MemoryId, filePath, (key, existing) => filePath);
            
            // Save updated index
            await SaveMemoryIndexAsync();
            
            MemoryStored?.Invoke(this, memory);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<MemoryItem?> RetrieveMemoryAsync(string memoryId)
    {
        if (string.IsNullOrEmpty(memoryId))
            return null;

        try
        {
            if (!_memoryIndex.TryGetValue(memoryId, out var filePath))
                return null;

            if (!File.Exists(filePath))
            {
                // Remove stale index entry
                _memoryIndex.TryRemove(memoryId, out _);
                return null;
            }

            byte[] compressedData;
            lock (_fileLock)
            {
                compressedData = File.ReadAllBytes(filePath);
            }

            var memory = await DecompressMemoryAsync(compressedData);
            return memory;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public async Task<List<MemoryItem>> SearchMemoriesAsync(MemoryQuery query)
    {
        if (query == null)
            return new List<MemoryItem>();

        try
        {
            var results = new List<MemoryItem>();
            var searchTasks = new List<Task<MemoryItem?>>();

            // Search through all indexed memories
            foreach (var kvp in _memoryIndex)
            {
                searchTasks.Add(RetrieveMemoryAsync(kvp.Key));
            }

            var memories = await Task.WhenAll(searchTasks);
            
            foreach (var memory in memories.Where(m => m != null))
            {
                if (MatchesQuery(memory!, query))
                {
                    results.Add(memory!);
                }
            }

            // Apply sorting and limits
            results = results
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(query.MaxResults)
                .ToList();

            return results;
        }
        catch (Exception)
        {
            return new List<MemoryItem>();
        }
    }

    public async Task<bool> OptimizeStorageAsync()
    {
        try
        {
            var optimizedCount = 0;
            var filesToProcess = new List<string>();

            // Collect all .longterm files
            lock (_fileLock)
            {
                filesToProcess = Directory.GetFiles(_storageDirectory, "*.longterm").ToList();
            }

            // Group files by age for consolidation
            var fileGroups = filesToProcess
                .GroupBy(f => GetFileAgeGroup(f))
                .Where(g => g.Count() > 1)
                .ToList();

            foreach (var group in fileGroups)
            {
                if (await ConsolidateFiles(group.ToList()))
                {
                    optimizedCount++;
                }
            }

            // Clean up empty or corrupted files
            await CleanupCorruptedFilesAsync();

            StorageOptimized?.Invoke(this, $"Optimized {optimizedCount} file groups");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<Dictionary<string, object>> GetStorageStatisticsAsync()
    {
        try
        {
            var stats = new Dictionary<string, object>();
            
            var files = Directory.GetFiles(_storageDirectory, "*.longterm");
            var totalSize = files.Sum(f => new FileInfo(f).Length);
            var memoryCount = _memoryIndex.Count;
            
            // Calculate compression ratio by sampling some files
            var compressionRatio = await CalculateCompressionRatioAsync();
            
            stats["TotalSize"] = totalSize;
            stats["FileCount"] = files.Length;
            stats["LongTermMemoryCount"] = memoryCount;
            stats["CompressionRatio"] = compressionRatio;
            stats["AverageFileSize"] = files.Length > 0 ? totalSize / files.Length : 0;
            stats["LastOptimization"] = _clock.GetCurrentInstant();
            
            return stats;
        }
        catch (Exception)
        {
            return new Dictionary<string, object>();
        }
    }

    public async Task<bool> CreateMemoryIndexAsync()
    {
        try
        {
            _memoryIndex.Clear();
            
            var files = Directory.GetFiles(_storageDirectory, "*.longterm");
            var indexTasks = files.Select(async filePath =>
            {
                try
                {
                    var compressedData = File.ReadAllBytes(filePath);
                    var memory = await DecompressMemoryAsync(compressedData);
                    if (memory != null)
                    {
                        _memoryIndex.TryAdd(memory.MemoryId, filePath);
                    }
                }
                catch
                {
                    // Skip corrupted files
                }
            });

            await Task.WhenAll(indexTasks);
            await SaveMemoryIndexAsync();
            
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> BackupMemoryFilesAsync()
    {
        try
        {
            var backupDirectory = Path.Combine(_storageDirectory, "Backup", DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
            Directory.CreateDirectory(backupDirectory);

            var files = Directory.GetFiles(_storageDirectory, "*.longterm");
            var copyTasks = files.Select(async file =>
            {
                var fileName = Path.GetFileName(file);
                var backupPath = Path.Combine(backupDirectory, fileName);
                
                using var source = File.OpenRead(file);
                using var destination = File.Create(backupPath);
                await source.CopyToAsync(destination);
            });

            await Task.WhenAll(copyTasks);
            
            // Also backup the index
            var indexPath = Path.Combine(_storageDirectory, "memory_index.json");
            if (File.Exists(indexPath))
            {
                var backupIndexPath = Path.Combine(backupDirectory, "memory_index.json");
                File.Copy(indexPath, backupIndexPath);
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Search memories by context criteria (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByContextAsync(Dictionary<string, object> contextCriteria, int maxResults = 100)
    {
        if (contextCriteria == null || !contextCriteria.Any())
            return new List<MemoryItem>();

        try
        {
            var results = new List<MemoryItem>();
            var searchTasks = new List<Task<MemoryItem?>>();

            // Search through all indexed memories
            foreach (var kvp in _memoryIndex)
            {
                searchTasks.Add(RetrieveMemoryAsync(kvp.Key));
            }

            var memories = await Task.WhenAll(searchTasks);
            
            foreach (var memory in memories.Where(m => m != null))
            {
                if (MatchesContextCriteria(memory!, contextCriteria))
                {
                    results.Add(memory!);
                }
            }

            // Apply sorting and limits
            return results
                .OrderByDescending(m => CalculateContextMatchScore(m, contextCriteria))
                .ThenByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception)
        {
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Search memories within a temporal range (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> SearchByTemporalRangeAsync(Instant startTime, Instant endTime, int maxResults = 100)
    {
        try
        {
            var results = new List<MemoryItem>();
            var searchTasks = new List<Task<MemoryItem?>>();

            // Search through all indexed memories
            foreach (var kvp in _memoryIndex)
            {
                searchTasks.Add(RetrieveMemoryAsync(kvp.Key));
            }

            var memories = await Task.WhenAll(searchTasks);
            
            foreach (var memory in memories.Where(m => m != null))
            {
                if (memory!.Timestamp >= startTime && memory.Timestamp <= endTime)
                {
                    results.Add(memory);
                }
            }

            // Apply sorting and limits
            return results
                .OrderByDescending(m => m.Timestamp)
                .ThenByDescending(m => m.ImportanceScore)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception)
        {
            return new List<MemoryItem>();
        }
    }

    /// <summary>
    /// Get memories by association ID (Requirements 14.4, 14.5)
    /// </summary>
    public async Task<List<MemoryItem>> GetMemoriesByAssociationAsync(string associationId, int maxResults = 100)
    {
        if (string.IsNullOrEmpty(associationId))
            return new List<MemoryItem>();

        try
        {
            var results = new List<MemoryItem>();
            var searchTasks = new List<Task<MemoryItem?>>();

            // Search through all indexed memories
            foreach (var kvp in _memoryIndex)
            {
                searchTasks.Add(RetrieveMemoryAsync(kvp.Key));
            }

            var memories = await Task.WhenAll(searchTasks);
            
            foreach (var memory in memories.Where(m => m != null))
            {
                if (memory!.Associations.Contains(associationId) || memory.MemoryId == associationId)
                {
                    results.Add(memory);
                }
            }

            // Apply sorting and limits
            return results
                .OrderByDescending(m => m.ImportanceScore)
                .ThenByDescending(m => m.Timestamp)
                .Take(maxResults)
                .ToList();
        }
        catch (Exception)
        {
            return new List<MemoryItem>();
        }
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
            // Retrieve the memory
            var memory = await RetrieveMemoryAsync(memoryId);
            if (memory == null)
                return false;

            // Update associations
            var updatedAssociations = memory.Associations.Union(associations).ToList();
            memory.Associations = updatedAssociations;

            // Re-store the memory with updated associations
            return await StoreCompressedMemoryAsync(memory);
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

    private async Task<byte[]> CompressMemoryAsync(MemoryItem memory)
    {
        var json = JsonSerializer.Serialize(memory, new JsonSerializerOptions
        {
            WriteIndented = false
        });
        
        var jsonBytes = System.Text.Encoding.UTF8.GetBytes(json);
        
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            await gzip.WriteAsync(jsonBytes, 0, jsonBytes.Length);
        }
        
        return output.ToArray();
    }

    private async Task<MemoryItem?> DecompressMemoryAsync(byte[] compressedData)
    {
        try
        {
            using var input = new MemoryStream(compressedData);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            
            await gzip.CopyToAsync(output);
            var jsonBytes = output.ToArray();
            var json = System.Text.Encoding.UTF8.GetString(jsonBytes);
            
            var memory = JsonSerializer.Deserialize<MemoryItem>(json);
            return memory;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private bool MatchesQuery(MemoryItem memory, MemoryQuery query)
    {
        // Check search terms
        if (!string.IsNullOrEmpty(query.SearchTerms))
        {
            var searchTerms = query.SearchTerms.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var searchableText = $"{memory.Content} {string.Join(" ", memory.Tags)} {string.Join(" ", memory.Context.Values.Select(v => v.ToString()))}".ToLowerInvariant();
            
            if (!searchTerms.Any(term => searchableText.Contains(term)))
                return false;
        }

        // Check time range
        if (query.TimeRange.HasValue)
        {
            if (memory.Timestamp < query.TimeRange.Value.Start || memory.Timestamp > query.TimeRange.Value.End)
                return false;
        }

        // Check memory types
        if (query.MemoryTypes.Any() && !query.MemoryTypes.Contains(memory.MemoryType))
            return false;

        // Check importance threshold
        if (memory.ImportanceScore < query.ImportanceThreshold)
            return false;

        return true;
    }

    private void LoadMemoryIndex()
    {
        try
        {
            var indexPath = Path.Combine(_storageDirectory, "memory_index.json");
            if (File.Exists(indexPath))
            {
                var json = File.ReadAllText(indexPath);
                var index = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
                if (index != null)
                {
                    foreach (var kvp in index)
                    {
                        _memoryIndex.TryAdd(kvp.Key, kvp.Value);
                    }
                }
            }
        }
        catch
        {
            // If index is corrupted, rebuild it
            _ = Task.Run(CreateMemoryIndexAsync);
        }
    }

    private async Task SaveMemoryIndexAsync()
    {
        try
        {
            var indexPath = Path.Combine(_storageDirectory, "memory_index.json");
            var index = _memoryIndex.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            var json = JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true });
            
            await File.WriteAllTextAsync(indexPath, json);
        }
        catch
        {
            // Ignore index save failures
        }
    }

    private string GetFileAgeGroup(string filePath)
    {
        try
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            var parts = fileName.Split('_');
            if (parts.Length > 0 && long.TryParse(parts[0], out var timestamp))
            {
                var instant = Instant.FromUnixTimeSeconds(timestamp);
                var age = _clock.GetCurrentInstant() - instant;
                
                if (age < Duration.FromDays(1))
                    return "recent";
                else if (age < Duration.FromDays(7))
                    return "week";
                else if (age < Duration.FromDays(30))
                    return "month";
                else
                    return "old";
            }
        }
        catch
        {
            // Ignore parsing errors
        }
        
        return "unknown";
    }

    private async Task<bool> ConsolidateFiles(List<string> files)
    {
        try
        {
            if (files.Count <= 1)
                return false;

            var memories = new List<MemoryItem>();
            
            // Load all memories from the files
            foreach (var file in files)
            {
                var compressedData = File.ReadAllBytes(file);
                var memory = await DecompressMemoryAsync(compressedData);
                if (memory != null)
                {
                    memories.Add(memory);
                }
            }

            if (memories.Count == 0)
                return false;

            // Create consolidated file
            var consolidatedFileName = $"consolidated_{_clock.GetCurrentInstant().ToUnixTimeSeconds()}.longterm";
            var consolidatedPath = Path.Combine(_storageDirectory, consolidatedFileName);
            
            // Store consolidated memories (this is a simplified approach - in practice, you might want a more sophisticated format)
            var consolidatedData = await CompressMemoryAsync(memories.First()); // For now, just store the first one
            File.WriteAllBytes(consolidatedPath, consolidatedData);

            // Update index and remove old files
            lock (_fileLock)
            {
                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }

            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private async Task CleanupCorruptedFilesAsync()
    {
        var files = Directory.GetFiles(_storageDirectory, "*.longterm");
        var corruptedFiles = new List<string>();

        foreach (var file in files)
        {
            try
            {
                var compressedData = File.ReadAllBytes(file);
                var memory = await DecompressMemoryAsync(compressedData);
                if (memory == null)
                {
                    corruptedFiles.Add(file);
                }
            }
            catch
            {
                corruptedFiles.Add(file);
            }
        }

        // Remove corrupted files and their index entries
        foreach (var file in corruptedFiles)
        {
            try
            {
                File.Delete(file);
                
                // Remove from index
                var entryToRemove = _memoryIndex.FirstOrDefault(kvp => kvp.Value == file);
                if (!entryToRemove.Equals(default(KeyValuePair<string, string>)))
                {
                    _memoryIndex.TryRemove(entryToRemove.Key, out _);
                }
            }
            catch
            {
                // Ignore cleanup failures
            }
        }

        if (corruptedFiles.Any())
        {
            await SaveMemoryIndexAsync();
        }
    }

    private async Task<float> CalculateCompressionRatioAsync()
    {
        try
        {
            var files = Directory.GetFiles(_storageDirectory, "*.longterm").Take(10).ToList();
            if (!files.Any())
                return 1.0f;

            var totalCompressed = 0L;
            var totalUncompressed = 0L;

            foreach (var file in files)
            {
                var compressedData = File.ReadAllBytes(file);
                var memory = await DecompressMemoryAsync(compressedData);
                
                if (memory != null)
                {
                    var uncompressedSize = System.Text.Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(memory));
                    totalCompressed += compressedData.Length;
                    totalUncompressed += uncompressedSize;
                }
            }

            return totalUncompressed > 0 ? (float)totalCompressed / totalUncompressed : 1.0f;
        }
        catch
        {
            return 1.0f;
        }
    }
}