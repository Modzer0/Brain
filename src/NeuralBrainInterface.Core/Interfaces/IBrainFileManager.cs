using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IBrainFileManager
{
    Task<bool> CreateNewBrainFileAsync(string filePath, Dictionary<string, object> config);
    Task<bool> ExportBrainFileAsync(string filePath, bool includeMemories);
    Task<BrainImportResult> ImportBrainFileAsync(string filePath);
    
    Task<BrainValidationResult> ValidateBrainFileAsync(string filePath);
    Task<BrainMetadata?> GetBrainFileMetadataAsync(string filePath);
    Task<bool> CompressBrainFileAsync(string filePath);
    
    Task<bool> SwitchActiveBrainAsync(string filePath);
    Task<List<BrainFileInfo>> GetAvailableBrainFilesAsync();
    Task<bool> BackupCurrentBrainAsync(string backupPath);
    
    event EventHandler<string>? BrainSwitched;
    event EventHandler<BrainImportResult>? BrainImported;
    event EventHandler<string>? BrainFileError;
}