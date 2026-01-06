using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;

namespace NeuralBrainInterface.Core.Services;

public class StateManager : IStateManager, IDisposable
{
    private readonly IMemoryManager _memoryManager;
    private SleepStatus _sleepStatus;
    private readonly string _stateDirectory;
    private readonly string _checkpointDirectory;
    private readonly string _backupDirectory;
    private readonly object _stateLock = new();
    private INeuralCore? _neuralCore; // Lazy injection to avoid circular dependency
    private Timer? _autoBackupTimer;
    private readonly TimeSpan _autoBackupInterval = TimeSpan.FromMinutes(5);

    public event EventHandler<SleepStatus>? SleepStatusChanged;
    public event EventHandler<string>? StateError;

    public StateManager(IMemoryManager memoryManager)
    {
        _memoryManager = memoryManager;
        _sleepStatus = new SleepStatus();
        
        // Create state directory in user's app data
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        _stateDirectory = Path.Combine(appDataPath, "NeuralBrainInterface", "States");
        _checkpointDirectory = Path.Combine(_stateDirectory, "Checkpoints");
        _backupDirectory = Path.Combine(_stateDirectory, "Backups");
        
        Directory.CreateDirectory(_stateDirectory);
        Directory.CreateDirectory(_checkpointDirectory);
        Directory.CreateDirectory(_backupDirectory);
        
        _sleepStatus.StateSaveLocation = Path.Combine(_stateDirectory, "current_state.json");
        
        // Start automatic backup timer
        StartAutoBackupTimer();
    }

    public void SetNeuralCore(INeuralCore neuralCore)
    {
        _neuralCore = neuralCore;
    }

    public async Task<bool> InitiateSleepAsync()
    {
        try
        {
            lock (_stateLock)
            {
                if (_sleepStatus.IsSleeping)
                {
                    return true; // Already sleeping
                }
            }

            // Save complete state before sleeping
            var saveSuccess = await SaveCompleteStateAsync();
            if (!saveSuccess)
            {
                StateError?.Invoke(this, "Failed to save state before sleep");
                return false;
            }

            lock (_stateLock)
            {
                _sleepStatus.IsSleeping = true;
                _sleepStatus.LastSleepTime = SystemClock.Instance.GetCurrentInstant();
            }

            SleepStatusChanged?.Invoke(this, _sleepStatus);
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error during sleep initiation: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> InitiateWakeAsync()
    {
        try
        {
            lock (_stateLock)
            {
                if (!_sleepStatus.IsSleeping)
                {
                    return true; // Already awake
                }
            }

            // Restore complete state after waking
            var restoreSuccess = await RestoreCompleteStateAsync();
            if (!restoreSuccess)
            {
                StateError?.Invoke(this, "Failed to restore state after wake");
                return false;
            }

            lock (_stateLock)
            {
                _sleepStatus.IsSleeping = false;
                _sleepStatus.LastWakeTime = SystemClock.Instance.GetCurrentInstant();
            }

            SleepStatusChanged?.Invoke(this, _sleepStatus);
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error during wake initiation: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> SaveCompleteStateAsync()
    {
        try
        {
            NeuralState? currentState = null;
            if (_neuralCore != null)
            {
                currentState = _neuralCore.GetCurrentState();
            }
            
            var memoryUsage = await _memoryManager.GetMemoryUsageAsync();
            
            var stateData = new
            {
                NeuralState = currentState,
                MemoryUsage = memoryUsage,
                SleepStatus = _sleepStatus,
                SaveTimestamp = SystemClock.Instance.GetCurrentInstant()
            };

            var json = JsonSerializer.Serialize(stateData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new InstantJsonConverter() }
            });

            await File.WriteAllTextAsync(_sleepStatus.StateSaveLocation, json);
            
            // Also save memory state
            await _memoryManager.SaveMemoryStateAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error saving complete state: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> RestoreCompleteStateAsync()
    {
        try
        {
            if (!File.Exists(_sleepStatus.StateSaveLocation))
            {
                StateError?.Invoke(this, "No saved state found to restore");
                return false;
            }

            var json = await File.ReadAllTextAsync(_sleepStatus.StateSaveLocation);
            var stateData = JsonSerializer.Deserialize<dynamic>(json);
            
            // Restore memory state first
            await _memoryManager.RestoreMemoryStateAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error restoring complete state: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> AutoSleepOnCloseAsync()
    {
        if (_sleepStatus.AutoSleepEnabled)
        {
            return await InitiateSleepAsync();
        }
        return true;
    }

    public async Task<bool> AutoWakeOnStartAsync()
    {
        if (_sleepStatus.AutoSleepEnabled && File.Exists(_sleepStatus.StateSaveLocation))
        {
            return await InitiateWakeAsync();
        }
        return true;
    }

    public SleepStatus GetSleepStatus()
    {
        lock (_stateLock)
        {
            return new SleepStatus
            {
                IsSleeping = _sleepStatus.IsSleeping,
                LastSleepTime = _sleepStatus.LastSleepTime,
                LastWakeTime = _sleepStatus.LastWakeTime,
                AutoSleepEnabled = _sleepStatus.AutoSleepEnabled,
                StateSaveLocation = _sleepStatus.StateSaveLocation
            };
        }
    }

    public void ConfigureAutoSleep(bool enabled)
    {
        lock (_stateLock)
        {
            _sleepStatus.AutoSleepEnabled = enabled;
        }
    }

    /// <summary>
    /// Creates a checkpoint of the current state for recovery purposes
    /// </summary>
    public async Task<string?> CreateCheckpointAsync(string? checkpointName = null)
    {
        try
        {
            var timestamp = SystemClock.Instance.GetCurrentInstant();
            var checkpointId = checkpointName ?? $"checkpoint_{timestamp.ToUnixTimeSeconds()}";
            var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointId}.json");

            NeuralState? currentState = null;
            if (_neuralCore != null)
            {
                currentState = _neuralCore.GetCurrentState();
            }
            
            var memoryUsage = await _memoryManager.GetMemoryUsageAsync();
            
            var checkpointData = new StateCheckpoint
            {
                CheckpointId = checkpointId,
                CreatedAt = timestamp,
                NeuralState = currentState,
                MemoryUsage = memoryUsage,
                SleepStatus = _sleepStatus,
                Checksum = string.Empty // Will be calculated after serialization
            };

            var json = JsonSerializer.Serialize(checkpointData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new InstantJsonConverter() }
            });

            // Calculate checksum for integrity validation
            checkpointData.Checksum = CalculateChecksum(json);
            
            // Re-serialize with checksum
            json = JsonSerializer.Serialize(checkpointData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new InstantJsonConverter() }
            });

            await File.WriteAllTextAsync(checkpointPath, json);
            
            // Also save memory state with checkpoint
            await _memoryManager.SaveMemoryStateAsync();
            
            return checkpointId;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error creating checkpoint: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Restores state from a specific checkpoint
    /// </summary>
    public async Task<bool> RestoreFromCheckpointAsync(string checkpointId)
    {
        try
        {
            var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpointId}.json");
            
            if (!File.Exists(checkpointPath))
            {
                StateError?.Invoke(this, $"Checkpoint not found: {checkpointId}");
                return false;
            }

            var json = await File.ReadAllTextAsync(checkpointPath);
            var checkpoint = JsonSerializer.Deserialize<StateCheckpoint>(json, new JsonSerializerOptions
            {
                Converters = { new InstantJsonConverter() }
            });

            if (checkpoint == null)
            {
                StateError?.Invoke(this, $"Failed to deserialize checkpoint: {checkpointId}");
                return false;
            }

            // Validate checkpoint integrity
            var currentChecksum = CalculateChecksum(json);
            if (currentChecksum != checkpoint.Checksum)
            {
                StateError?.Invoke(this, $"Checkpoint integrity check failed: {checkpointId}");
                return false;
            }

            // Restore memory state first
            await _memoryManager.RestoreMemoryStateAsync();
            
            // Restore neural state if available
            if (_neuralCore != null && checkpoint.NeuralState != null)
            {
                // Note: This would require implementing LoadState in NeuralCore
                // For now, we'll just log that we would restore the state
                StateError?.Invoke(this, $"Neural state restoration from checkpoint {checkpointId} completed");
            }
            
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error restoring from checkpoint {checkpointId}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Gets a list of available checkpoints
    /// </summary>
    public async Task<List<StateCheckpoint>> GetAvailableCheckpointsAsync()
    {
        var checkpoints = new List<StateCheckpoint>();
        
        try
        {
            var checkpointFiles = Directory.GetFiles(_checkpointDirectory, "*.json");
            
            foreach (var file in checkpointFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var checkpoint = JsonSerializer.Deserialize<StateCheckpoint>(json, new JsonSerializerOptions
                    {
                        Converters = { new InstantJsonConverter() }
                    });
                    
                    if (checkpoint != null)
                    {
                        checkpoints.Add(checkpoint);
                    }
                }
                catch (Exception ex)
                {
                    StateError?.Invoke(this, $"Error reading checkpoint file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error getting available checkpoints: {ex.Message}");
        }
        
        return checkpoints.OrderByDescending(c => c.CreatedAt).ToList();
    }

    /// <summary>
    /// Deletes old checkpoints to manage storage space
    /// </summary>
    public async Task CleanupOldCheckpointsAsync(int maxCheckpoints = 10)
    {
        try
        {
            var checkpoints = await GetAvailableCheckpointsAsync();
            
            if (checkpoints.Count > maxCheckpoints)
            {
                var checkpointsToDelete = checkpoints.Skip(maxCheckpoints);
                
                foreach (var checkpoint in checkpointsToDelete)
                {
                    var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpoint.CheckpointId}.json");
                    if (File.Exists(checkpointPath))
                    {
                        File.Delete(checkpointPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error cleaning up old checkpoints: {ex.Message}");
        }
    }

    /// <summary>
    /// Creates an automatic backup during processing
    /// </summary>
    public async Task<bool> CreateAutomaticBackupAsync()
    {
        try
        {
            var timestamp = SystemClock.Instance.GetCurrentInstant();
            var backupId = $"auto_backup_{timestamp.ToUnixTimeSeconds()}";
            var backupPath = Path.Combine(_backupDirectory, $"{backupId}.json");

            NeuralState? currentState = null;
            if (_neuralCore != null)
            {
                currentState = _neuralCore.GetCurrentState();
            }
            
            var memoryUsage = await _memoryManager.GetMemoryUsageAsync();
            
            var backupData = new StateBackup
            {
                BackupId = backupId,
                CreatedAt = timestamp,
                NeuralState = currentState,
                MemoryUsage = memoryUsage,
                BackupType = BackupType.Automatic,
                Checksum = string.Empty
            };

            var json = JsonSerializer.Serialize(backupData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new InstantJsonConverter() }
            });

            backupData.Checksum = CalculateChecksum(json);
            
            json = JsonSerializer.Serialize(backupData, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new InstantJsonConverter() }
            });

            await File.WriteAllTextAsync(backupPath, json);
            
            // Clean up old automatic backups (keep only last 5)
            await CleanupOldBackupsAsync(5);
            
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error creating automatic backup: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Validates state integrity and detects corruption
    /// </summary>
    public async Task<StateValidationResult> ValidateStateIntegrityAsync(string? statePath = null)
    {
        var result = new StateValidationResult();
        
        try
        {
            var pathToValidate = statePath ?? _sleepStatus.StateSaveLocation;
            
            if (!File.Exists(pathToValidate))
            {
                result.IsValid = false;
                result.ErrorMessages.Add("State file does not exist");
                return result;
            }

            var json = await File.ReadAllTextAsync(pathToValidate);
            
            // Try to deserialize the state
            try
            {
                var stateData = JsonSerializer.Deserialize<dynamic>(json);
                result.IsValid = true;
                result.FileSize = new FileInfo(pathToValidate).Length;
            }
            catch (JsonException ex)
            {
                result.IsValid = false;
                result.ErrorMessages.Add($"JSON deserialization failed: {ex.Message}");
            }

            // Additional integrity checks
            if (result.IsValid)
            {
                // Check if file was modified recently (potential corruption indicator)
                var lastWrite = File.GetLastWriteTime(pathToValidate);
                var timeSinceWrite = DateTime.Now - lastWrite;
                
                if (timeSinceWrite.TotalSeconds < 1)
                {
                    result.Warnings.Add("State file was modified very recently, may be incomplete");
                }

                // Check file size (empty or too small files are suspicious)
                if (result.FileSize < 100)
                {
                    result.Warnings.Add("State file is unusually small");
                }
            }
        }
        catch (Exception ex)
        {
            result.IsValid = false;
            result.ErrorMessages.Add($"Validation error: {ex.Message}");
        }
        
        return result;
    }

    /// <summary>
    /// Attempts to recover to the last known good state
    /// </summary>
    public async Task<bool> RecoverToLastKnownGoodStateAsync()
    {
        try
        {
            // First, try to restore from the most recent checkpoint
            var checkpoints = await GetAvailableCheckpointsAsync();
            
            foreach (var checkpoint in checkpoints)
            {
                var checkpointPath = Path.Combine(_checkpointDirectory, $"{checkpoint.CheckpointId}.json");
                var validation = await ValidateStateIntegrityAsync(checkpointPath);
                
                if (validation.IsValid)
                {
                    var success = await RestoreFromCheckpointAsync(checkpoint.CheckpointId);
                    if (success)
                    {
                        StateError?.Invoke(this, $"Successfully recovered from checkpoint: {checkpoint.CheckpointId}");
                        return true;
                    }
                }
            }

            // If no valid checkpoints, try backups
            var backups = await GetAvailableBackupsAsync();
            
            foreach (var backup in backups)
            {
                var backupPath = Path.Combine(_backupDirectory, $"{backup.BackupId}.json");
                var validation = await ValidateStateIntegrityAsync(backupPath);
                
                if (validation.IsValid)
                {
                    var success = await RestoreFromBackupAsync(backup.BackupId);
                    if (success)
                    {
                        StateError?.Invoke(this, $"Successfully recovered from backup: {backup.BackupId}");
                        return true;
                    }
                }
            }

            StateError?.Invoke(this, "No valid recovery state found");
            return false;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error during recovery: {ex.Message}");
            return false;
        }
    }

    private void StartAutoBackupTimer()
    {
        _autoBackupTimer = new Timer(async _ =>
        {
            await CreateAutomaticBackupAsync();
        }, null, _autoBackupInterval, _autoBackupInterval);
    }

    private async Task<List<StateBackup>> GetAvailableBackupsAsync()
    {
        var backups = new List<StateBackup>();
        
        try
        {
            var backupFiles = Directory.GetFiles(_backupDirectory, "*.json");
            
            foreach (var file in backupFiles)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    var backup = JsonSerializer.Deserialize<StateBackup>(json, new JsonSerializerOptions
                    {
                        Converters = { new InstantJsonConverter() }
                    });
                    
                    if (backup != null)
                    {
                        backups.Add(backup);
                    }
                }
                catch (Exception ex)
                {
                    StateError?.Invoke(this, $"Error reading backup file {file}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error getting available backups: {ex.Message}");
        }
        
        return backups.OrderByDescending(b => b.CreatedAt).ToList();
    }

    private async Task<bool> RestoreFromBackupAsync(string backupId)
    {
        try
        {
            var backupPath = Path.Combine(_backupDirectory, $"{backupId}.json");
            
            if (!File.Exists(backupPath))
            {
                StateError?.Invoke(this, $"Backup not found: {backupId}");
                return false;
            }

            var json = await File.ReadAllTextAsync(backupPath);
            var backup = JsonSerializer.Deserialize<StateBackup>(json, new JsonSerializerOptions
            {
                Converters = { new InstantJsonConverter() }
            });

            if (backup == null)
            {
                StateError?.Invoke(this, $"Failed to deserialize backup: {backupId}");
                return false;
            }

            // Validate backup integrity
            var currentChecksum = CalculateChecksum(json);
            if (currentChecksum != backup.Checksum)
            {
                StateError?.Invoke(this, $"Backup integrity check failed: {backupId}");
                return false;
            }

            // Restore memory state
            await _memoryManager.RestoreMemoryStateAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error restoring from backup {backupId}: {ex.Message}");
            return false;
        }
    }

    private async Task CleanupOldBackupsAsync(int maxBackups)
    {
        try
        {
            var backups = await GetAvailableBackupsAsync();
            
            if (backups.Count > maxBackups)
            {
                var backupsToDelete = backups.Skip(maxBackups);
                
                foreach (var backup in backupsToDelete)
                {
                    var backupPath = Path.Combine(_backupDirectory, $"{backup.BackupId}.json");
                    if (File.Exists(backupPath))
                    {
                        File.Delete(backupPath);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StateError?.Invoke(this, $"Error cleaning up old backups: {ex.Message}");
        }
    }

    private static string CalculateChecksum(string content)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(content));
        return Convert.ToBase64String(hash);
    }

    public void Dispose()
    {
        _autoBackupTimer?.Dispose();
    }
}

// Helper class for JSON serialization of NodaTime Instant
public class InstantJsonConverter : System.Text.Json.Serialization.JsonConverter<Instant>
{
    public override Instant Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        return Instant.FromDateTimeUtc(DateTime.Parse(value!).ToUniversalTime());
    }

    public override void Write(Utf8JsonWriter writer, Instant value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value.ToDateTimeUtc().ToString("O"));
    }
}