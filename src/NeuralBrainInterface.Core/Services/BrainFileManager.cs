using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using System.IO.Compression;
using System.Text.Json;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;

namespace NeuralBrainInterface.Core.Services;

public class BrainFileManager : IBrainFileManager
{
    private readonly IMemoryManager _memoryManager;
    private readonly INeuralCore _neuralCore;
    private readonly IStateManager _stateManager;
    private readonly IClock _clock;
    private string? _currentActiveBrainPath;
    
    public event EventHandler<string>? BrainSwitched;
    public event EventHandler<BrainImportResult>? BrainImported;
    public event EventHandler<string>? BrainFileError;

    public BrainFileManager(
        IMemoryManager memoryManager,
        INeuralCore neuralCore,
        IStateManager stateManager,
        IClock clock)
    {
        _memoryManager = memoryManager ?? throw new ArgumentNullException(nameof(memoryManager));
        _neuralCore = neuralCore ?? throw new ArgumentNullException(nameof(neuralCore));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<bool> CreateNewBrainFileAsync(string filePath, Dictionary<string, object> config)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                BrainFileError?.Invoke(this, "File path cannot be empty");
                return false;
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Create default brain configuration
            var brainData = new BrainFileData
            {
                Metadata = new BrainMetadata
                {
                    BrainName = config.GetValueOrDefault("brain_name", Path.GetFileNameWithoutExtension(filePath))?.ToString() ?? "New Brain",
                    CreationDate = _clock.GetCurrentInstant(),
                    LastModified = _clock.GetCurrentInstant(),
                    Version = "1.0.0",
                    NeuralNetworkVersion = "1.0.0",
                    TotalMemories = 0,
                    FileSize = 0,
                    CompressionRatio = 1.0f,
                    CompatibilityVersion = "1.0.0"
                },
                NeuralState = await _neuralCore.GetCurrentStateAsync(),
                ShortTermMemories = new List<MemoryItem>(),
                LongTermMemories = new List<MemoryItem>(),
                DeviceSettings = new Dictionary<string, object>(),
                UserPreferences = config
            };

            // Serialize and save to file
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var jsonData = JsonSerializer.Serialize(brainData, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonData);

            // Update metadata with actual file size
            var fileInfo = new FileInfo(filePath);
            brainData.Metadata.FileSize = fileInfo.Length;

            // Re-save with updated metadata
            jsonData = JsonSerializer.Serialize(brainData, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonData);

            return true;
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to create brain file: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> ExportBrainFileAsync(string filePath, bool includeMemories)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath))
            {
                BrainFileError?.Invoke(this, "File path cannot be empty");
                return false;
            }

            // Ensure directory exists
            var directory = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // Try to read existing metadata to preserve brain name
            string brainName = Path.GetFileNameWithoutExtension(filePath);
            Instant creationDate = _clock.GetCurrentInstant();
            
            if (File.Exists(filePath))
            {
                var existingMetadata = await GetBrainFileMetadataAsync(filePath);
                if (existingMetadata != null)
                {
                    brainName = existingMetadata.BrainName;
                    creationDate = existingMetadata.CreationDate;
                }
            }

            // Gather current system state
            var currentState = await _neuralCore.GetCurrentStateAsync();
            var shortTermMemories = includeMemories ? await _memoryManager.GetAllShortTermMemoriesAsync() : new List<MemoryItem>();
            var longTermMemories = includeMemories ? await _memoryManager.GetAllLongTermMemoriesAsync() : new List<MemoryItem>();

            var brainData = new BrainFileData
            {
                Metadata = new BrainMetadata
                {
                    BrainName = brainName,
                    CreationDate = creationDate,
                    LastModified = _clock.GetCurrentInstant(),
                    Version = "1.0.0",
                    NeuralNetworkVersion = "1.0.0",
                    TotalMemories = shortTermMemories.Count + longTermMemories.Count,
                    FileSize = 0,
                    CompressionRatio = 1.0f,
                    CompatibilityVersion = "1.0.0"
                },
                NeuralState = currentState,
                ShortTermMemories = shortTermMemories,
                LongTermMemories = longTermMemories,
                DeviceSettings = new Dictionary<string, object>(), // TODO: Get from hardware controller
                UserPreferences = new Dictionary<string, object>()
            };

            // Serialize and save
            var jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var jsonData = JsonSerializer.Serialize(brainData, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonData);

            // Update metadata with actual file size
            var fileInfo = new FileInfo(filePath);
            brainData.Metadata.FileSize = fileInfo.Length;

            // Re-save with updated metadata
            jsonData = JsonSerializer.Serialize(brainData, jsonOptions);
            await File.WriteAllTextAsync(filePath, jsonData);

            return true;
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to export brain file: {ex.Message}");
            return false;
        }
    }

    public async Task<BrainImportResult> ImportBrainFileAsync(string filePath)
    {
        var result = new BrainImportResult();

        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                result.Success = false;
                result.Errors.Add("Brain file does not exist");
                return result;
            }

            // Validate file first
            var validationResult = await ValidateBrainFileAsync(filePath);
            if (!validationResult.IsValid)
            {
                result.Success = false;
                result.Errors.AddRange(validationResult.ValidationErrors);
                return result;
            }

            // Read and deserialize brain file
            var jsonData = await File.ReadAllTextAsync(filePath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var brainData = JsonSerializer.Deserialize<BrainFileData>(jsonData, jsonOptions);
            if (brainData == null)
            {
                result.Success = false;
                result.Errors.Add("Failed to deserialize brain file data");
                return result;
            }

            // Import neural state
            if (brainData.NeuralState != null)
            {
                await _neuralCore.LoadStateAsync(brainData.NeuralState);
            }

            // Import memories
            var importedMemoriesCount = 0;
            if (brainData.ShortTermMemories != null)
            {
                foreach (var memory in brainData.ShortTermMemories)
                {
                    if (await _memoryManager.StoreShortTermMemoryAsync(memory))
                    {
                        importedMemoriesCount++;
                    }
                }
            }

            if (brainData.LongTermMemories != null)
            {
                foreach (var memory in brainData.LongTermMemories)
                {
                    if (await _memoryManager.StoreLongTermMemoryAsync(memory))
                    {
                        importedMemoriesCount++;
                    }
                }
            }

            result.Success = true;
            result.BrainMetadata = brainData.Metadata;
            result.ImportedMemoriesCount = importedMemoriesCount;

            if (!validationResult.IsCompatible)
            {
                result.CompatibilityIssues.AddRange(validationResult.CompatibilityWarnings);
            }

            BrainImported?.Invoke(this, result);
            return result;
        }
        catch (Exception ex)
        {
            result.Success = false;
            result.Errors.Add($"Failed to import brain file: {ex.Message}");
            BrainFileError?.Invoke(this, $"Import failed: {ex.Message}");
            return result;
        }
    }

    public async Task<BrainValidationResult> ValidateBrainFileAsync(string filePath)
    {
        var result = new BrainValidationResult();

        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Brain file does not exist");
                return result;
            }

            // Check file integrity
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Brain file is empty");
                return result;
            }

            // Try to read and parse the file
            var jsonData = await File.ReadAllTextAsync(filePath);
            if (string.IsNullOrEmpty(jsonData))
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Brain file contains no data");
                return result;
            }

            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var brainData = JsonSerializer.Deserialize<BrainFileData>(jsonData, jsonOptions);
            if (brainData == null)
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Failed to parse brain file format");
                return result;
            }

            // Validate metadata
            if (brainData.Metadata == null)
            {
                result.IsValid = false;
                result.ValidationErrors.Add("Brain file missing metadata");
                return result;
            }

            // Check compatibility
            var currentVersion = new Version("1.0.0");
            if (Version.TryParse(brainData.Metadata.CompatibilityVersion, out var fileVersion))
            {
                result.IsCompatible = fileVersion <= currentVersion;
                if (!result.IsCompatible)
                {
                    result.CompatibilityWarnings.Add($"Brain file version {fileVersion} is newer than supported version {currentVersion}");
                }
            }
            else
            {
                result.IsCompatible = false;
                result.CompatibilityWarnings.Add("Unable to determine brain file version compatibility");
            }

            result.IsValid = true;
            result.Metadata = brainData.Metadata;
            result.FileIntegrityCheck = true;

            return result;
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ValidationErrors.Add($"Validation failed: {ex.Message}");
            return result;
        }
    }

    public async Task<BrainMetadata?> GetBrainFileMetadataAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return null;
            }

            var jsonData = await File.ReadAllTextAsync(filePath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var brainData = JsonSerializer.Deserialize<BrainFileData>(jsonData, jsonOptions);
            return brainData?.Metadata;
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to read metadata: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> CompressBrainFileAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                BrainFileError?.Invoke(this, "Brain file does not exist");
                return false;
            }

            // Read and parse the brain file
            var jsonData = await File.ReadAllTextAsync(filePath);
            var jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var brainData = JsonSerializer.Deserialize<BrainFileData>(jsonData, jsonOptions);
            if (brainData == null)
            {
                BrainFileError?.Invoke(this, "Failed to parse brain file for compression");
                return false;
            }

            // Update metadata to reflect compression attempt
            var originalSize = new FileInfo(filePath).Length;
            brainData.Metadata.LastModified = _clock.GetCurrentInstant();

            // Re-serialize with minimal whitespace for "compression"
            var compressedOptions = new JsonSerializerOptions
            {
                WriteIndented = false, // Remove indentation for smaller file size
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);

            var compressedJson = JsonSerializer.Serialize(brainData, compressedOptions);
            await File.WriteAllTextAsync(filePath, compressedJson);

            var newSize = new FileInfo(filePath).Length;
            var compressionRatio = (float)newSize / originalSize;

            brainData.Metadata.CompressionRatio = compressionRatio;
            brainData.Metadata.FileSize = newSize;

            // Re-save with updated metadata
            compressedJson = JsonSerializer.Serialize(brainData, compressedOptions);
            await File.WriteAllTextAsync(filePath, compressedJson);

            return true;
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to compress brain file: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SwitchActiveBrainAsync(string filePath)
    {
        try
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                BrainFileError?.Invoke(this, "Brain file does not exist");
                return false;
            }

            // Validate the brain file first
            var validationResult = await ValidateBrainFileAsync(filePath);
            if (!validationResult.IsValid)
            {
                BrainFileError?.Invoke(this, "Cannot switch to invalid brain file");
                return false;
            }

            // Save current state if there's an active brain
            if (!string.IsNullOrEmpty(_currentActiveBrainPath))
            {
                await ExportBrainFileAsync(_currentActiveBrainPath, true);
            }

            // Import the new brain
            var importResult = await ImportBrainFileAsync(filePath);
            if (!importResult.Success)
            {
                BrainFileError?.Invoke(this, "Failed to import new brain file");
                return false;
            }

            _currentActiveBrainPath = filePath;
            BrainSwitched?.Invoke(this, filePath);
            return true;
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to switch brain: {ex.Message}");
            return false;
        }
    }

    public async Task<List<BrainFileInfo>> GetAvailableBrainFilesAsync()
    {
        var brainFiles = new List<BrainFileInfo>();

        try
        {
            // Look for .brain files in common locations
            var searchPaths = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NeuralBrainInterface"),
                Directory.GetCurrentDirectory()
            };

            foreach (var searchPath in searchPaths)
            {
                if (!Directory.Exists(searchPath)) continue;

                var brainFilePaths = Directory.GetFiles(searchPath, "*.brain", SearchOption.AllDirectories);
                
                foreach (var filePath in brainFilePaths)
                {
                    var metadata = await GetBrainFileMetadataAsync(filePath);
                    if (metadata != null)
                    {
                        var fileInfo = new FileInfo(filePath);
                        brainFiles.Add(new BrainFileInfo
                        {
                            FilePath = filePath,
                            BrainName = metadata.BrainName,
                            Metadata = metadata,
                            IsCurrentActive = filePath == _currentActiveBrainPath,
                            LastAccessed = Instant.FromDateTimeUtc(fileInfo.LastAccessTimeUtc),
                            FileStatus = BrainFileStatus.Valid
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to scan for brain files: {ex.Message}");
        }

        return brainFiles;
    }

    public async Task<bool> BackupCurrentBrainAsync(string backupPath)
    {
        try
        {
            if (string.IsNullOrEmpty(_currentActiveBrainPath))
            {
                BrainFileError?.Invoke(this, "No active brain to backup");
                return false;
            }

            // Export current state to backup location
            return await ExportBrainFileAsync(backupPath, true);
        }
        catch (Exception ex)
        {
            BrainFileError?.Invoke(this, $"Failed to backup brain: {ex.Message}");
            return false;
        }
    }

    // Internal data structure for brain file serialization
    private class BrainFileData
    {
        public BrainMetadata Metadata { get; set; } = new();
        public NeuralState? NeuralState { get; set; }
        public List<MemoryItem> ShortTermMemories { get; set; } = new();
        public List<MemoryItem> LongTermMemories { get; set; } = new();
        public Dictionary<string, object> DeviceSettings { get; set; } = new();
        public Dictionary<string, object> UserPreferences { get; set; } = new();
    }
}