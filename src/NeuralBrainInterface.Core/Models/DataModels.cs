using NodaTime;

namespace NeuralBrainInterface.Core.Models;

public enum DeviceType
{
    Microphone,
    Speaker,
    Webcam
}

public enum MemoryType
{
    ShortTerm,
    LongTerm,
    Working,
    Episodic
}

public enum MediaType
{
    Image,
    Video,
    Audio,
    Document,
    Spreadsheet,
    Text
}

public enum VisualizationMode
{
    NetworkTopology,
    ActivationPatterns,
    AttentionMaps,
    ProcessingFlow
}

public enum BrainFileStatus
{
    Valid,
    Corrupted,
    Incompatible,
    Missing
}

public enum BackupType
{
    Manual,
    Automatic,
    Checkpoint
}

public class NeuralState
{
    public Dictionary<string, float[]> ActivationPatterns { get; set; } = new();
    public Dictionary<string, float[]> AttentionWeights { get; set; } = new();
    public Dictionary<string, object> MemoryContents { get; set; } = new();
    public Instant ProcessingTimestamp { get; set; }
    public Dictionary<string, float> ConfidenceScores { get; set; } = new();
    public Dictionary<DeviceType, bool> DeviceContext { get; set; } = new();
    public TimeInfo TemporalContext { get; set; } = new();
    public SleepStatus SleepStatus { get; set; } = new();
}

public class ProcessingResult
{
    public bool Success { get; set; }
    public NeuralState? UpdatedState { get; set; }
    public string? GeneratedOutput { get; set; }
    public float ProcessingTime { get; set; }
    public ResourceUsage ResourceUsage { get; set; } = new();
}

public class DeviceStatus
{
    public DeviceType DeviceType { get; set; }
    public bool IsEnabled { get; set; }
    public bool IsAvailable { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public bool PermissionGranted { get; set; }
    public string? ErrorMessage { get; set; }
}

public class AudioSettings
{
    public int SampleRate { get; set; } = 44100;
    public int BitDepth { get; set; } = 16;
    public int Channels { get; set; } = 1;
    public int BufferSize { get; set; } = 1024;
    public bool NoiseReduction { get; set; } = true;
}

public class VideoSettings
{
    public (int Width, int Height) Resolution { get; set; } = (640, 480);
    public int FrameRate { get; set; } = 30;
    public string ColorFormat { get; set; } = "RGB24";
    public float CompressionQuality { get; set; } = 0.8f;
}

public class VoiceSettings
{
    public string VoiceType { get; set; } = "Default";
    public float SpeechRate { get; set; } = 1.0f;
    public float Volume { get; set; } = 0.8f;
    public float Pitch { get; set; } = 1.0f;
    public string Language { get; set; } = "en-US";
}

public class ResourceConfig
{
    public int ActiveMemoryMb { get; set; } = 1024;
    public int CpuCores { get; set; } = Environment.ProcessorCount / 2;
    public int GpuCores { get; set; } = 0;
    public int MaxProcessingTimeMs { get; set; } = 5000;
    public int VisualizationFps { get; set; } = 30;
}

public class ResourceUsage
{
    public long MemoryUsedBytes { get; set; }
    public float CpuUsagePercent { get; set; }
    public float GpuUsagePercent { get; set; }
    public TimeSpan ProcessingTime { get; set; }
}

public class ResourceInfo
{
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public int TotalCpuCores { get; set; }
    public int AvailableCpuCores { get; set; }
    public int TotalGpuCores { get; set; }
    public int AvailableGpuCores { get; set; }
}

public class TimeInfo
{
    public Instant CurrentDateTime { get; set; }
    public Instant SessionStartTime { get; set; }
    public Duration TimeSinceWake { get; set; }
    public DateTimeZone TimeZone { get; set; } = DateTimeZone.Utc;
    public bool IsDaylightSaving { get; set; }
}

public class SleepStatus
{
    public bool IsSleeping { get; set; }
    public Instant? LastSleepTime { get; set; }
    public Instant? LastWakeTime { get; set; }
    public bool AutoSleepEnabled { get; set; } = true;
    public string StateSaveLocation { get; set; } = string.Empty;
}

public class FileFormat
{
    public MediaType MediaType { get; set; }
    public string FileExtension { get; set; } = string.Empty;
    public string MimeType { get; set; } = string.Empty;
    public bool IsSupported { get; set; }
    public bool RequiresConversion { get; set; }
    public long EstimatedSize { get; set; }
}

public class ValidationResult
{
    public bool IsValid { get; set; }
    public long FileSize { get; set; }
    public FileFormat FormatDetected { get; set; } = new();
    public List<string> ErrorMessages { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public bool CanProcess { get; set; }
}

public class MemoryItem
{
    public string MemoryId { get; set; } = Guid.NewGuid().ToString();
    public string Content { get; set; } = string.Empty;
    public Dictionary<string, object> Context { get; set; } = new();
    public Instant Timestamp { get; set; }
    public float ImportanceScore { get; set; }
    public MemoryType MemoryType { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Associations { get; set; } = new();
    public int CompressionLevel { get; set; }
}

public class MemoryQuery
{
    public string SearchTerms { get; set; } = string.Empty;
    public (Instant Start, Instant End)? TimeRange { get; set; }
    public List<MemoryType> MemoryTypes { get; set; } = new();
    public float ImportanceThreshold { get; set; } = 0.0f;
    public int MaxResults { get; set; } = 100;
    public bool IncludeAssociations { get; set; } = true;
}

public class MemoryUsage
{
    public long ShortTermUsed { get; set; }
    public long ShortTermCapacity { get; set; }
    public long LongTermUsed { get; set; }
    public int LongTermFilesCount { get; set; }
    public float CompressionRatio { get; set; }
    public Instant LastOptimization { get; set; }
}

/// <summary>
/// Represents brain configuration for creating new brain files
/// </summary>
public class BrainConfig
{
    public string BrainName { get; set; } = string.Empty;
    public string NeuralNetworkType { get; set; } = "Standard";
    public Dictionary<string, object> InitialParameters { get; set; } = new();
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Represents comprehensive memory statistics for performance monitoring (Requirements 14.6, 14.7, 14.8)
/// </summary>
public class MemoryStatistics
{
    public int TotalMemories { get; set; }
    public int ShortTermCount { get; set; }
    public int LongTermCount { get; set; }
    public float AverageRecallTime { get; set; }
    public float CompressionEfficiency { get; set; }
    public float StorageOptimizationLevel { get; set; }
    public float AverageImportanceScore { get; set; }
    public int OrganizationOperationsCount { get; set; }
    public Instant LastOrganizationTime { get; set; }
    public Instant LastCoherenceCheck { get; set; }
    public bool CoherenceStatus { get; set; }
    public Dictionary<string, int> MemoriesByTag { get; set; } = new();
    public Dictionary<MemoryType, int> MemoriesByType { get; set; } = new();
}

/// <summary>
/// Represents memory organization configuration (Requirements 14.6)
/// </summary>
public class MemoryOrganizationConfig
{
    public float ImportanceThreshold { get; set; } = 0.3f;
    public float RecencyWeight { get; set; } = 0.3f;
    public float ImportanceWeight { get; set; } = 0.5f;
    public float RelevanceWeight { get; set; } = 0.2f;
    public int MaxMemoriesPerCategory { get; set; } = 1000;
    public Duration MaxMemoryAge { get; set; } = Duration.FromDays(365);
    public bool AutoOrganizeEnabled { get; set; } = true;
    public Duration OrganizationInterval { get; set; } = Duration.FromHours(24);
}

/// <summary>
/// Represents memory coherence state across sessions (Requirements 14.8)
/// </summary>
public class MemoryCoherenceState
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public Instant SessionStartTime { get; set; }
    public Instant LastSyncTime { get; set; }
    public int ShortTermMemoryCount { get; set; }
    public int LongTermMemoryCount { get; set; }
    public string ShortTermChecksum { get; set; } = string.Empty;
    public string LongTermChecksum { get; set; } = string.Empty;
    public bool IsCoherent { get; set; } = true;
    public List<string> IncoherentMemoryIds { get; set; } = new();
}

public class BrainMetadata
{
    public string BrainName { get; set; } = string.Empty;
    public Instant CreationDate { get; set; }
    public Instant LastModified { get; set; }
    public string Version { get; set; } = "1.0.0";
    public string NeuralNetworkVersion { get; set; } = "1.0.0";
    public int TotalMemories { get; set; }
    public long FileSize { get; set; }
    public float CompressionRatio { get; set; }
    public string CompatibilityVersion { get; set; } = "1.0.0";
}

public class BrainImportResult
{
    public bool Success { get; set; }
    public BrainMetadata? BrainMetadata { get; set; }
    public int ImportedMemoriesCount { get; set; }
    public List<string> Warnings { get; set; } = new();
    public List<string> Errors { get; set; } = new();
    public List<string> CompatibilityIssues { get; set; } = new();
}

public class BrainValidationResult
{
    public bool IsValid { get; set; }
    public bool IsCompatible { get; set; }
    public BrainMetadata? Metadata { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> CompatibilityWarnings { get; set; } = new();
    public bool FileIntegrityCheck { get; set; }
}

public class BrainFileInfo
{
    public string FilePath { get; set; } = string.Empty;
    public string BrainName { get; set; } = string.Empty;
    public BrainMetadata Metadata { get; set; } = new();
    public bool IsCurrentActive { get; set; }
    public Instant LastAccessed { get; set; }
    public BrainFileStatus FileStatus { get; set; }
}

// Data containers for different input types
public class ImageData
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public string Format { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class VideoData
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public TimeSpan Duration { get; set; }
    public string Format { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class AudioData
{
    public byte[] Data { get; set; } = Array.Empty<byte>();
    public int SampleRate { get; set; }
    public int Channels { get; set; }
    public int BitDepth { get; set; }
    public TimeSpan Duration { get; set; }
    public string Format { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class DocumentData
{
    public string TextContent { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
    public object? FormattingInfo { get; set; }
    public List<object> EmbeddedMedia { get; set; } = new();
    public int PageCount { get; set; }
    public int WordCount { get; set; }
}

public class SpreadsheetData
{
    public Dictionary<string, object> Sheets { get; set; } = new();
    public Dictionary<string, object> Metadata { get; set; } = new();
    public List<object> Formulas { get; set; } = new();
    public List<object> Charts { get; set; } = new();
    public int TotalRows { get; set; }
    public int TotalColumns { get; set; }
}

public class VisualFrame
{
    public byte[] FrameData { get; set; } = Array.Empty<byte>();
    public int Width { get; set; }
    public int Height { get; set; }
    public VisualizationMode Mode { get; set; }
    public Instant Timestamp { get; set; }
    public Dictionary<string, object> RenderingParameters { get; set; } = new();
}

// Stream types for real-time processing
public interface IAudioStream
{
    event EventHandler<AudioData>? AudioDataReceived;
    bool IsActive { get; }
    void Start();
    void Stop();
}

public interface IVideoStream
{
    event EventHandler<ImageData>? FrameReceived;
    bool IsActive { get; }
    void Start();
    void Stop();
}

// Handle types for resource management
public class MemoryHandle : IDisposable
{
    public long AllocatedBytes { get; set; }
    public IntPtr Handle { get; set; }
    
    public void Dispose()
    {
        // Implementation for memory cleanup
        GC.SuppressFinalize(this);
    }
}

public class ComputeHandle : IDisposable
{
    public int CpuCores { get; set; }
    public int GpuCores { get; set; }
    
    public void Dispose()
    {
        // Implementation for compute resource cleanup
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Represents a state checkpoint for recovery purposes
/// </summary>
public class StateCheckpoint
{
    public string CheckpointId { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
    public NeuralState? NeuralState { get; set; }
    public MemoryUsage? MemoryUsage { get; set; }
    public SleepStatus SleepStatus { get; set; } = new();
    public string Checksum { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// Represents an automatic backup of system state
/// </summary>
public class StateBackup
{
    public string BackupId { get; set; } = string.Empty;
    public Instant CreatedAt { get; set; }
    public NeuralState? NeuralState { get; set; }
    public MemoryUsage? MemoryUsage { get; set; }
    public BackupType BackupType { get; set; }
    public string Checksum { get; set; } = string.Empty;
    public long FileSize { get; set; }
}

/// <summary>
/// Represents the result of state validation
/// </summary>
public class StateValidationResult
{
    public bool IsValid { get; set; }
    public long FileSize { get; set; }
    public List<string> ErrorMessages { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public string? CorruptionDetails { get; set; }
    public bool CanRecover { get; set; }
}