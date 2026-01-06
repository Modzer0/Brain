using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IStateManager
{
    Task<bool> InitiateSleepAsync();
    Task<bool> InitiateWakeAsync();
    Task<bool> SaveCompleteStateAsync();
    Task<bool> RestoreCompleteStateAsync();
    Task<bool> AutoSleepOnCloseAsync();
    Task<bool> AutoWakeOnStartAsync();
    
    SleepStatus GetSleepStatus();
    void ConfigureAutoSleep(bool enabled);
    
    // Checkpoint management
    Task<string?> CreateCheckpointAsync(string? checkpointName = null);
    Task<bool> RestoreFromCheckpointAsync(string checkpointId);
    Task<List<StateCheckpoint>> GetAvailableCheckpointsAsync();
    Task CleanupOldCheckpointsAsync(int maxCheckpoints = 10);
    
    // Automatic backup
    Task<bool> CreateAutomaticBackupAsync();
    
    // State validation and recovery
    Task<StateValidationResult> ValidateStateIntegrityAsync(string? statePath = null);
    Task<bool> RecoverToLastKnownGoodStateAsync();
    
    event EventHandler<SleepStatus>? SleepStatusChanged;
    event EventHandler<string>? StateError;
}