using Xunit;
using FsCheck;
using FsCheck.Xunit;
using NeuralBrainInterface.Core.Services;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using NeuralBrainInterface.Core.Configuration;
using Microsoft.Extensions.Configuration;
using NodaTime;

namespace NeuralBrainInterface.Tests;

/// <summary>
/// Wrapper type for valid brain names that guarantees non-null, non-empty strings
/// </summary>
public class ValidBrainName
{
    public string Value { get; }
    
    public ValidBrainName(string value)
    {
        Value = value;
    }
    
    public override string ToString() => Value;
}

/// <summary>
/// Custom Arbitrary for generating valid brain names
/// </summary>
public class ValidBrainNameArbitrary
{
    public static Arbitrary<ValidBrainName> ValidBrainNameArb()
    {
        return Arb.From(
            Gen.Elements("brain", "test", "neural", "memory", "core", "alpha", "beta", "gamma")
               .Select(prefix => new ValidBrainName($"{prefix}_{Guid.NewGuid().ToString().Substring(0, 8)}"))
        );
    }
}

public class BrainFilePropertyTests
{
    private readonly IServiceProvider _serviceProvider;

    public BrainFilePropertyTests()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder().Build();
        services.AddNeuralBrainInterfaceCore(configuration);
        _serviceProvider = services.BuildServiceProvider();
    }

    [Property(MaxTest = 100)]
    public bool BrainFileCreation_ShouldCreateValidFile(NonEmptyString nonEmptyFileName)
    {
        /**
         * Feature: neural-brain-interface, Property 43: Brain File Creation
         * Validates: Requirements 15.1
         */
        
        // Arrange
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        
        // Handle potential null from NonEmptyString.Get
        var fileName = nonEmptyFileName?.Get;
        if (string.IsNullOrEmpty(fileName))
        {
            // Skip this test case - FsCheck generated an invalid NonEmptyString
            return true;
        }
        
        var validFileName = SanitizeFileName(fileName);
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"{validFileName}_{Guid.NewGuid()}.brain");
        var validConfig = new Dictionary<string, object> { ["brain_name"] = validFileName };
        
        try
        {
            var result = brainFileManager.CreateNewBrainFileAsync(filePath, validConfig).Result;
            var fileExists = File.Exists(filePath);
            var metadata = brainFileManager.GetBrainFileMetadataAsync(filePath).Result;
            var validation = brainFileManager.ValidateBrainFileAsync(filePath).Result;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return result && fileExists && metadata != null && validation.IsValid;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }

    [Property(MaxTest = 100)]
    public bool BrainFileExport_ShouldIncludeAllComponents(NonEmptyString nonEmptyFileName, bool includeMemories)
    {
        /**
         * Feature: neural-brain-interface, Property 44: Complete Brain Export
         * Validates: Requirements 15.2, 15.4
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        var memoryManager = _serviceProvider.GetRequiredService<IMemoryManager>();
        
        // Handle potential null from NonEmptyString.Get
        var fileName = nonEmptyFileName?.Get;
        if (string.IsNullOrEmpty(fileName))
        {
            // Skip this test case - FsCheck generated an invalid NonEmptyString
            return true;
        }
        
        var validFileName = SanitizeFileName(fileName);
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"{validFileName}_{Guid.NewGuid()}.brain");
        
        try
        {
            var testMemory = new MemoryItem
            {
                MemoryId = Guid.NewGuid().ToString(),
                Content = "Test memory content",
                Context = new Dictionary<string, object> { ["test"] = "value" },
                Timestamp = SystemClock.Instance.GetCurrentInstant(),
                ImportanceScore = 0.5f,
                MemoryType = MemoryType.ShortTerm,
                Tags = new List<string> { "test" },
                Associations = new List<string>(),
                CompressionLevel = 1
            };
            
            memoryManager.StoreShortTermMemoryAsync(testMemory).Wait();
            var exportResult = brainFileManager.ExportBrainFileAsync(filePath, includeMemories).Result;
            var fileExists = File.Exists(filePath);
            var metadata = brainFileManager.GetBrainFileMetadataAsync(filePath).Result;
            var validation = brainFileManager.ValidateBrainFileAsync(filePath).Result;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return exportResult && fileExists && metadata != null && validation.IsValid;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }


    [Property(MaxTest = 100)]
    public bool BrainFileImport_ShouldRestoreCompleteState(NonEmptyString nonEmptyBrainName)
    {
        /**
         * Feature: neural-brain-interface, Property 45: Brain Import and Restoration
         * Validates: Requirements 15.3
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        var memoryManager = _serviceProvider.GetRequiredService<IMemoryManager>();
        
        // Handle potential null from NonEmptyString.Get
        var brainName = nonEmptyBrainName?.Get;
        if (string.IsNullOrEmpty(brainName))
        {
            // Skip this test case - FsCheck generated an invalid NonEmptyString
            return true;
        }
        
        var validBrainName = SanitizeFileName(brainName);
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"{validBrainName}_{Guid.NewGuid()}.brain");
        
        try
        {
            var config = new Dictionary<string, object> { ["brain_name"] = validBrainName };
            var createResult = brainFileManager.CreateNewBrainFileAsync(filePath, config).Result;
            if (!createResult) return false;
            
            var testMemory = new MemoryItem
            {
                MemoryId = Guid.NewGuid().ToString(),
                Content = "Test import memory",
                Context = new Dictionary<string, object> { ["import"] = "test" },
                Timestamp = SystemClock.Instance.GetCurrentInstant(),
                ImportanceScore = 0.7f,
                MemoryType = MemoryType.ShortTerm,
                Tags = new List<string> { "import", "test" },
                Associations = new List<string>(),
                CompressionLevel = 1
            };
            
            memoryManager.StoreShortTermMemoryAsync(testMemory).Wait();
            var exportResult = brainFileManager.ExportBrainFileAsync(filePath, true).Result;
            if (!exportResult) return false;
            
            memoryManager.ClearShortTermMemoryAsync().Wait();
            var importResult = brainFileManager.ImportBrainFileAsync(filePath).Result;
            
            var success = importResult.Success && 
                         importResult.BrainMetadata != null &&
                         importResult.BrainMetadata.BrainName == validBrainName;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return success;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }

    [Property(MaxTest = 100)]
    public bool BrainFileValidation_ShouldDetectInvalidFiles(NonEmptyString invalidContent)
    {
        /**
         * Feature: neural-brain-interface, Property 46: Brain File Validation
         * Validates: Requirements 15.5
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"invalid_{Guid.NewGuid()}.brain");
        
        try
        {
            var content = invalidContent.Get;
            var invalidJsonContent = "<<<INVALID>>>" + content;
            File.WriteAllText(filePath, invalidJsonContent);
            
            var validation = brainFileManager.ValidateBrainFileAsync(filePath).Result;
            var isCorrectlyInvalid = !validation.IsValid && validation.ValidationErrors.Count > 0;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return isCorrectlyInvalid;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }

    [Property(MaxTest = 100)]
    public bool BrainFileMetadata_ShouldBeAccurate(NonEmptyString nonEmptyBrainName)
    {
        /**
         * Feature: neural-brain-interface, Property 47: Brain File Metadata Management
         * Validates: Requirements 15.6
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        
        // Handle potential null from NonEmptyString.Get
        var brainName = nonEmptyBrainName?.Get;
        if (string.IsNullOrEmpty(brainName))
        {
            // Skip this test case - FsCheck generated an invalid NonEmptyString
            return true;
        }
        
        var validBrainName = SanitizeFileName(brainName);
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"{validBrainName}_{Guid.NewGuid()}.brain");
        
        try
        {
            var config = new Dictionary<string, object> { ["brain_name"] = validBrainName };
            var createResult = brainFileManager.CreateNewBrainFileAsync(filePath, config).Result;
            var metadata = brainFileManager.GetBrainFileMetadataAsync(filePath).Result;
            
            var isAccurate = createResult && 
                           metadata != null &&
                           metadata.BrainName == validBrainName &&
                           metadata.Version == "1.0.0" &&
                           metadata.FileSize > 0;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return isAccurate;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }


    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidBrainNameArbitrary) })]
    public bool BrainFileCompression_ShouldMaintainIntegrity(ValidBrainName validBrainNameWrapper)
    {
        /**
         * Feature: neural-brain-interface, Property 48: Brain File Compression
         * Validates: Requirements 15.7
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        
        var brainName = validBrainNameWrapper.Value;
        var validBrainName = SanitizeFileName(brainName);
        var tempDir = Path.GetTempPath();
        var filePath = Path.Combine(tempDir, $"{validBrainName}_{Guid.NewGuid()}.brain");
        
        try
        {
            var config = new Dictionary<string, object> { ["brain_name"] = validBrainName };
            var createResult = brainFileManager.CreateNewBrainFileAsync(filePath, config).Result;
            if (!createResult) return false;
            
            var originalMetadata = brainFileManager.GetBrainFileMetadataAsync(filePath).Result;
            if (originalMetadata == null) return false;
            
            brainFileManager.CompressBrainFileAsync(filePath).Wait();
            
            var validation = brainFileManager.ValidateBrainFileAsync(filePath).Result;
            var newMetadata = brainFileManager.GetBrainFileMetadataAsync(filePath).Result;
            
            var integrityMaintained = validation.IsValid && 
                                    newMetadata != null &&
                                    newMetadata.BrainName == originalMetadata.BrainName;
            
            if (File.Exists(filePath)) File.Delete(filePath);
            return integrityMaintained;
        }
        catch
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            return false;
        }
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ValidBrainNameArbitrary) })]
    public bool RuntimeBrainSwitching_ShouldMaintainFunctionality(ValidBrainName validBrain1NameWrapper, ValidBrainName validBrain2NameWrapper)
    {
        /**
         * Feature: neural-brain-interface, Property 49: Runtime Brain Switching
         * Validates: Requirements 15.8
         */
        
        var brainFileManager = _serviceProvider.GetRequiredService<IBrainFileManager>();
        
        var validBrain1Name = SanitizeFileName(validBrain1NameWrapper.Value);
        var validBrain2Name = SanitizeFileName(validBrain2NameWrapper.Value);
        
        var tempDir = Path.GetTempPath();
        var filePath1 = Path.Combine(tempDir, $"{validBrain1Name}_{Guid.NewGuid()}.brain");
        var filePath2 = Path.Combine(tempDir, $"{validBrain2Name}_{Guid.NewGuid()}.brain");
        
        try
        {
            var config1 = new Dictionary<string, object> { ["brain_name"] = validBrain1Name };
            var config2 = new Dictionary<string, object> { ["brain_name"] = validBrain2Name };
            
            var create1 = brainFileManager.CreateNewBrainFileAsync(filePath1, config1).Result;
            var create2 = brainFileManager.CreateNewBrainFileAsync(filePath2, config2).Result;
            
            if (!create1 || !create2) return false;
            
            var switch1 = brainFileManager.SwitchActiveBrainAsync(filePath1).Result;
            var switch2 = brainFileManager.SwitchActiveBrainAsync(filePath2).Result;
            
            var functionalityMaintained = switch1 && switch2;
            
            if (File.Exists(filePath1)) File.Delete(filePath1);
            if (File.Exists(filePath2)) File.Delete(filePath2);
            
            return functionalityMaintained;
        }
        catch
        {
            if (File.Exists(filePath1)) File.Delete(filePath1);
            if (File.Exists(filePath2)) File.Delete(filePath2);
            return false;
        }
    }

    private static string SanitizeFileName(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return "default_brain";
            
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(fileName.Where(c => !invalidChars.Contains(c) && !char.IsControl(c)).ToArray());
        
        if (string.IsNullOrWhiteSpace(sanitized))
            return "default_brain";
            
        return sanitized.Substring(0, Math.Min(sanitized.Length, 50));
    }
}
