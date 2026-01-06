using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IFileFormatManager
{
    Task<FileFormat> DetectFileFormatAsync(string filePath);
    Task<ValidationResult> ValidateFileIntegrityAsync(string filePath);
    
    Task<ImageData> ConvertImageFileAsync(string filePath);
    Task<VideoData> ConvertVideoFileAsync(string filePath);
    Task<AudioData> ConvertAudioFileAsync(string filePath);
    Task<DocumentData> ConvertDocumentFileAsync(string filePath);
    Task<SpreadsheetData> ConvertSpreadsheetFileAsync(string filePath);
    
    Dictionary<MediaType, List<string>> GetSupportedFormats();
    bool IsFormatSupported(string fileExtension);
    Task<List<string>> GetConversionOptionsAsync(FileFormat fileFormat);
    
    // Enhanced error handling and format conversion methods
    Task<string> GetUnsupportedFormatErrorMessageAsync(string fileExtension);
    Task<bool> TryConvertToSupportedFormatAsync(string inputFilePath, string outputFilePath, string targetExtension);
    Task<ValidationResult> ValidateFileIntegrityWithDetailedErrorsAsync(string filePath);
    
    event EventHandler<string>? ConversionProgress;
    event EventHandler<string>? FormatError;
}