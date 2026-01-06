using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using System.Text;

namespace NeuralBrainInterface.Core.Services;

public class FileFormatManager : IFileFormatManager
{
    private readonly Dictionary<MediaType, List<string>> _supportedFormats;
    private readonly Dictionary<string, string> _mimeTypes;
    
    public event EventHandler<string>? ConversionProgress;
    public event EventHandler<string>? FormatError;

    public FileFormatManager()
    {
        _supportedFormats = InitializeSupportedFormats();
        _mimeTypes = InitializeMimeTypes();
    }

    public async Task<FileFormat> DetectFileFormatAsync(string filePath)
    {
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            return new FileFormat
            {
                MediaType = MediaType.Text,
                FileExtension = string.Empty,
                MimeType = string.Empty,
                IsSupported = false,
                RequiresConversion = false,
                EstimatedSize = 0
            };
        }

        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var mediaType = DetermineMediaType(extension);
        var isSupported = IsFormatSupported(extension);
        var mimeType = GetMimeType(extension);

        // Perform basic file validation
        var requiresConversion = DetermineConversionRequirement(extension, mediaType);

        return await Task.FromResult(new FileFormat
        {
            MediaType = mediaType,
            FileExtension = extension,
            MimeType = mimeType,
            IsSupported = isSupported,
            RequiresConversion = requiresConversion,
            EstimatedSize = fileInfo.Length
        });
    }

    public async Task<ValidationResult> ValidateFileIntegrityAsync(string filePath)
    {
        var result = new ValidationResult();
        
        if (string.IsNullOrEmpty(filePath))
        {
            result.ErrorMessages.Add("File path cannot be null or empty");
            result.ErrorMessages.Add("• Please provide a valid file path");
            return result;
        }

        if (!File.Exists(filePath))
        {
            result.ErrorMessages.Add($"File does not exist: {filePath}");
            result.ErrorMessages.Add("• Verify the file path is correct");
            result.ErrorMessages.Add("• Check if the file has been moved or deleted");
            result.ErrorMessages.Add("• Ensure you have permission to access the file location");
            return result;
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            result.FileSize = fileInfo.Length;
            result.FormatDetected = await DetectFileFormatAsync(filePath);

            // Basic file integrity checks
            if (fileInfo.Length == 0)
            {
                result.ErrorMessages.Add("File is empty");
                result.ErrorMessages.Add("• The file appears to be empty or corrupted");
                result.ErrorMessages.Add("• Try opening the file in its native application");
                result.ErrorMessages.Add("• Consider using a backup copy if available");
                result.CanProcess = false;
            }
            else if (fileInfo.Length < 10) // Files smaller than 10 bytes are likely truncated
            {
                result.ErrorMessages.Add("File appears to be truncated or corrupted (too small)");
                result.ErrorMessages.Add("• The file is unusually small and may be incomplete");
                result.ErrorMessages.Add("• Try re-downloading or obtaining a fresh copy");
                result.ErrorMessages.Add("• Verify the file transfer completed successfully");
                result.CanProcess = false;
            }
            else if (fileInfo.Length > 100 * 1024 * 1024) // 100MB limit
            {
                result.Warnings.Add("File is very large and may take time to process");
                result.Warnings.Add("• Consider breaking large files into smaller chunks");
                result.Warnings.Add("• Ensure sufficient system memory is available");
            }

            // Check if file is readable
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[Math.Min(1024, fileInfo.Length)];
            await stream.ReadAsync(buffer, 0, buffer.Length);

            // Enhanced file header validation with recovery suggestions
            var headerValidation = await ValidateFileHeaderWithRecoveryAsync(filePath, result.FormatDetected.FileExtension, buffer);
            if (!headerValidation.IsValid)
            {
                result.ErrorMessages.AddRange(headerValidation.ErrorMessages);
                result.CanProcess = false;
            }

            // Additional format-specific validation
            await PerformFormatSpecificValidationAsync(filePath, result);

            result.IsValid = result.ErrorMessages.Count == 0;
            result.CanProcess = result.IsValid && (result.FormatDetected.IsSupported || result.FormatDetected.RequiresConversion);
            
            // If format is not supported, provide helpful error message
            if (!result.FormatDetected.IsSupported && !result.FormatDetected.RequiresConversion)
            {
                var errorMessage = await GetUnsupportedFormatErrorMessageAsync(result.FormatDetected.FileExtension);
                result.ErrorMessages.Add(errorMessage);
                result.CanProcess = false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            result.ErrorMessages.Add("Access denied to file");
            result.ErrorMessages.Add("• Check file permissions and ensure you have read access");
            result.ErrorMessages.Add("• Close the file if it's open in another application");
            result.ErrorMessages.Add("• Run the application with appropriate privileges");
        }
        catch (IOException ex)
        {
            result.ErrorMessages.Add($"IO error reading file: {ex.Message}");
            result.ErrorMessages.Add("• The file may be locked by another process");
            result.ErrorMessages.Add("• Check available disk space");
            result.ErrorMessages.Add("• Verify the storage device is functioning properly");
        }
        catch (Exception ex)
        {
            result.ErrorMessages.Add($"Unexpected error validating file: {ex.Message}");
            result.ErrorMessages.Add("• Try restarting the application");
            result.ErrorMessages.Add("• Check system resources and available memory");
            result.ErrorMessages.Add("• Contact support if the problem persists");
        }

        return result;
    }

    public async Task<ImageData> ConvertImageFileAsync(string filePath)
    {
        var validation = await ValidateFileIntegrityAsync(filePath);
        if (!validation.IsValid || validation.FormatDetected.MediaType != MediaType.Image)
        {
            throw new InvalidOperationException($"Cannot convert file to image: {string.Join(", ", validation.ErrorMessages)}");
        }

        try
        {
            ConversionProgress?.Invoke(this, "Starting image conversion...");
            
            var imageData = new ImageData
            {
                Data = await File.ReadAllBytesAsync(filePath),
                Format = validation.FormatDetected.FileExtension,
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalPath"] = filePath,
                    ["FileSize"] = validation.FileSize,
                    ["ConvertedAt"] = DateTime.UtcNow
                }
            };

            // Basic image dimension detection (simplified)
            var dimensions = await DetectImageDimensionsAsync(filePath, validation.FormatDetected.FileExtension);
            imageData.Width = dimensions.Width;
            imageData.Height = dimensions.Height;

            ConversionProgress?.Invoke(this, "Image conversion completed");
            return imageData;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error converting image: {ex.Message}");
            throw;
        }
    }

    public async Task<VideoData> ConvertVideoFileAsync(string filePath)
    {
        var validation = await ValidateFileIntegrityAsync(filePath);
        if (!validation.IsValid || validation.FormatDetected.MediaType != MediaType.Video)
        {
            throw new InvalidOperationException($"Cannot convert file to video: {string.Join(", ", validation.ErrorMessages)}");
        }

        try
        {
            ConversionProgress?.Invoke(this, "Starting video conversion...");
            
            var videoData = new VideoData
            {
                Data = await File.ReadAllBytesAsync(filePath),
                Format = validation.FormatDetected.FileExtension,
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalPath"] = filePath,
                    ["FileSize"] = validation.FileSize,
                    ["ConvertedAt"] = DateTime.UtcNow
                }
            };

            // Basic video metadata detection (simplified)
            var metadata = await DetectVideoMetadataAsync(filePath, validation.FormatDetected.FileExtension);
            videoData.Width = metadata.Width;
            videoData.Height = metadata.Height;
            videoData.FrameRate = metadata.FrameRate;
            videoData.Duration = metadata.Duration;

            ConversionProgress?.Invoke(this, "Video conversion completed");
            return videoData;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error converting video: {ex.Message}");
            throw;
        }
    }

    public async Task<AudioData> ConvertAudioFileAsync(string filePath)
    {
        var validation = await ValidateFileIntegrityAsync(filePath);
        if (!validation.IsValid || validation.FormatDetected.MediaType != MediaType.Audio)
        {
            throw new InvalidOperationException($"Cannot convert file to audio: {string.Join(", ", validation.ErrorMessages)}");
        }

        try
        {
            ConversionProgress?.Invoke(this, "Starting audio conversion...");
            
            var audioData = new AudioData
            {
                Data = await File.ReadAllBytesAsync(filePath),
                Format = validation.FormatDetected.FileExtension,
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalPath"] = filePath,
                    ["FileSize"] = validation.FileSize,
                    ["ConvertedAt"] = DateTime.UtcNow
                }
            };

            // Basic audio metadata detection (simplified)
            var metadata = await DetectAudioMetadataAsync(filePath, validation.FormatDetected.FileExtension);
            audioData.SampleRate = metadata.SampleRate;
            audioData.Channels = metadata.Channels;
            audioData.BitDepth = metadata.BitDepth;
            audioData.Duration = metadata.Duration;

            ConversionProgress?.Invoke(this, "Audio conversion completed");
            return audioData;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error converting audio: {ex.Message}");
            throw;
        }
    }

    public async Task<DocumentData> ConvertDocumentFileAsync(string filePath)
    {
        var validation = await ValidateFileIntegrityAsync(filePath);
        if (!validation.IsValid || validation.FormatDetected.MediaType != MediaType.Document)
        {
            throw new InvalidOperationException($"Cannot convert file to document: {string.Join(", ", validation.ErrorMessages)}");
        }

        try
        {
            ConversionProgress?.Invoke(this, "Starting document conversion...");
            
            var documentData = new DocumentData
            {
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalPath"] = filePath,
                    ["FileSize"] = validation.FileSize,
                    ["ConvertedAt"] = DateTime.UtcNow,
                    ["FileExtension"] = validation.FormatDetected.FileExtension,
                    ["MimeType"] = validation.FormatDetected.MimeType
                }
            };

            // Extract text content based on format
            documentData.TextContent = await ExtractDocumentTextAsync(filePath, validation.FormatDetected.FileExtension);
            documentData.WordCount = CountWords(documentData.TextContent);
            documentData.PageCount = EstimatePageCount(documentData.TextContent);

            // Add content analysis metadata
            await AnalyzeDocumentContentAsync(documentData);

            ConversionProgress?.Invoke(this, "Document conversion completed");
            return documentData;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error converting document: {ex.Message}");
            throw;
        }
    }

    public async Task<SpreadsheetData> ConvertSpreadsheetFileAsync(string filePath)
    {
        var validation = await ValidateFileIntegrityAsync(filePath);
        if (!validation.IsValid || validation.FormatDetected.MediaType != MediaType.Spreadsheet)
        {
            throw new InvalidOperationException($"Cannot convert file to spreadsheet: {string.Join(", ", validation.ErrorMessages)}");
        }

        try
        {
            ConversionProgress?.Invoke(this, "Starting spreadsheet conversion...");
            
            var spreadsheetData = new SpreadsheetData
            {
                Metadata = new Dictionary<string, object>
                {
                    ["OriginalPath"] = filePath,
                    ["FileSize"] = validation.FileSize,
                    ["ConvertedAt"] = DateTime.UtcNow,
                    ["FileExtension"] = validation.FormatDetected.FileExtension,
                    ["MimeType"] = validation.FormatDetected.MimeType
                }
            };

            // Extract spreadsheet data based on format
            var sheetData = await ExtractSpreadsheetDataAsync(filePath, validation.FormatDetected.FileExtension);
            spreadsheetData.Sheets = sheetData.Sheets;
            spreadsheetData.TotalRows = sheetData.TotalRows;
            spreadsheetData.TotalColumns = sheetData.TotalColumns;

            // Add content analysis metadata
            await AnalyzeSpreadsheetContentAsync(spreadsheetData);

            ConversionProgress?.Invoke(this, "Spreadsheet conversion completed");
            return spreadsheetData;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error converting spreadsheet: {ex.Message}");
            throw;
        }
    }

    public Dictionary<MediaType, List<string>> GetSupportedFormats()
    {
        return new Dictionary<MediaType, List<string>>(_supportedFormats);
    }

    public bool IsFormatSupported(string fileExtension)
    {
        if (string.IsNullOrEmpty(fileExtension))
            return false;

        var extension = fileExtension.ToLowerInvariant();
        if (!extension.StartsWith("."))
            extension = "." + extension;

        return _supportedFormats.Values.Any(formats => formats.Contains(extension));
    }

    public async Task<List<string>> GetConversionOptionsAsync(FileFormat fileFormat)
    {
        var options = new List<string>();

        if (!fileFormat.IsSupported)
        {
            // Provide helpful error message with supported formats
            var supportedFormats = GetSupportedFormatsForMediaType(fileFormat.MediaType);
            if (supportedFormats.Any())
            {
                options.Add($"Format '{fileFormat.FileExtension}' is not supported.");
                options.Add($"Supported {fileFormat.MediaType.ToString().ToLower()} formats: {string.Join(", ", supportedFormats)}");
                options.Add("Please convert your file to a supported format and try again.");
            }
            else
            {
                options.Add($"Format '{fileFormat.FileExtension}' is not supported and no conversion options are available.");
                options.Add("Please check the file format and try with a supported file type.");
            }
            return await Task.FromResult(options);
        }

        switch (fileFormat.MediaType)
        {
            case MediaType.Image:
                options.AddRange(new[] { "Convert to PNG", "Convert to JPEG", "Resize image", "Extract metadata" });
                if (fileFormat.RequiresConversion)
                {
                    options.Add("Convert to standard format for optimal processing");
                }
                break;
            case MediaType.Video:
                options.AddRange(new[] { "Extract frames", "Convert to MP4", "Extract audio track", "Generate thumbnail" });
                if (fileFormat.RequiresConversion)
                {
                    options.Add("Convert to MP4 for optimal processing");
                }
                break;
            case MediaType.Audio:
                options.AddRange(new[] { "Convert to WAV", "Convert to MP3", "Extract waveform", "Normalize audio" });
                if (fileFormat.RequiresConversion)
                {
                    options.Add("Convert to WAV for optimal processing");
                }
                break;
            case MediaType.Document:
                options.AddRange(new[] { "Extract text", "Convert to plain text", "Extract images", "Parse structure" });
                if (fileFormat.RequiresConversion)
                {
                    options.Add("Convert to modern format for better text extraction");
                }
                break;
            case MediaType.Spreadsheet:
                options.AddRange(new[] { "Convert to CSV", "Extract data", "Parse formulas", "Export charts" });
                if (fileFormat.RequiresConversion)
                {
                    options.Add("Convert to XLSX for optimal processing");
                }
                break;
        }

        return await Task.FromResult(options);
    }

    public async Task<string> GetUnsupportedFormatErrorMessageAsync(string fileExtension)
    {
        var extension = fileExtension.ToLowerInvariant();
        if (!extension.StartsWith("."))
            extension = "." + extension;

        var mediaType = DetermineMediaType(extension);
        var supportedFormats = GetSupportedFormatsForMediaType(mediaType);
        
        var message = new StringBuilder();
        message.AppendLine($"The file format '{extension}' is not supported.");
        
        if (supportedFormats.Any())
        {
            message.AppendLine($"Supported {mediaType.ToString().ToLower()} formats include:");
            message.AppendLine(string.Join(", ", supportedFormats));
        }
        else
        {
            message.AppendLine("No supported formats are available for this media type.");
        }
        
        message.AppendLine();
        message.AppendLine("Recommendations:");
        message.AppendLine("• Convert your file to a supported format using appropriate software");
        message.AppendLine("• Check if the file extension is correct");
        message.AppendLine("• Verify that the file is not corrupted");
        
        return await Task.FromResult(message.ToString());
    }

    public async Task<bool> TryConvertToSupportedFormatAsync(string inputFilePath, string outputFilePath, string targetExtension)
    {
        try
        {
            ConversionProgress?.Invoke(this, $"Starting conversion to {targetExtension}...");
            
            var inputFormat = await DetectFileFormatAsync(inputFilePath);
            if (!inputFormat.IsSupported && !inputFormat.RequiresConversion)
            {
                FormatError?.Invoke(this, $"Cannot convert unsupported format: {inputFormat.FileExtension}");
                return false;
            }

            var targetFormat = targetExtension.ToLowerInvariant();
            if (!targetFormat.StartsWith("."))
                targetFormat = "." + targetFormat;

            // Validate target format is supported
            if (!IsFormatSupported(targetFormat))
            {
                FormatError?.Invoke(this, $"Target format {targetFormat} is not supported");
                return false;
            }

            // Perform format-specific conversion
            var success = await PerformFormatConversionAsync(inputFilePath, outputFilePath, inputFormat, targetFormat);
            
            if (success)
            {
                ConversionProgress?.Invoke(this, $"Conversion to {targetExtension} completed successfully");
            }
            else
            {
                FormatError?.Invoke(this, $"Conversion to {targetExtension} failed");
            }
            
            return success;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Error during format conversion: {ex.Message}");
            return false;
        }
    }

    public async Task<ValidationResult> ValidateFileIntegrityWithDetailedErrorsAsync(string filePath)
    {
        var result = await ValidateFileIntegrityAsync(filePath);
        
        // Enhance error messages with more detailed information
        if (!result.IsValid)
        {
            var enhancedErrors = new List<string>();
            
            foreach (var error in result.ErrorMessages)
            {
                enhancedErrors.Add(error);
                
                // Add specific recommendations based on error type
                if (error.Contains("File does not exist"))
                {
                    enhancedErrors.Add("• Verify the file path is correct");
                    enhancedErrors.Add("• Check if the file has been moved or deleted");
                    enhancedErrors.Add("• Ensure you have permission to access the file location");
                }
                else if (error.Contains("File is empty"))
                {
                    enhancedErrors.Add("• The file appears to be empty or corrupted");
                    enhancedErrors.Add("• Try opening the file in its native application");
                    enhancedErrors.Add("• Consider using a backup copy if available");
                }
                else if (error.Contains("File header does not match"))
                {
                    enhancedErrors.Add("• The file may be corrupted or have an incorrect extension");
                    enhancedErrors.Add("• Try renaming the file with the correct extension");
                    enhancedErrors.Add("• Verify the file was not damaged during transfer");
                }
                else if (error.Contains("Access denied"))
                {
                    enhancedErrors.Add("• Check file permissions and ensure you have read access");
                    enhancedErrors.Add("• Close the file if it's open in another application");
                    enhancedErrors.Add("• Run the application with appropriate privileges");
                }
            }
            
            result.ErrorMessages.Clear();
            result.ErrorMessages.AddRange(enhancedErrors);
        }
        
        return result;
    }

    private Dictionary<MediaType, List<string>> InitializeSupportedFormats()
    {
        return new Dictionary<MediaType, List<string>>
        {
            [MediaType.Image] = new List<string> { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".svg" },
            [MediaType.Video] = new List<string> { ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv" },
            [MediaType.Audio] = new List<string> { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" },
            [MediaType.Document] = new List<string> { ".txt", ".rtf", ".pdf", ".doc", ".docx", ".md" },
            [MediaType.Spreadsheet] = new List<string> { ".xls", ".xlsx", ".csv", ".ods" }
        };
    }

    private Dictionary<string, string> InitializeMimeTypes()
    {
        return new Dictionary<string, string>
        {
            // Image formats
            [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
            [".gif"] = "image/gif", [".bmp"] = "image/bmp", [".tiff"] = "image/tiff",
            [".tif"] = "image/tiff", [".webp"] = "image/webp", [".svg"] = "image/svg+xml",
            
            // Video formats
            [".mp4"] = "video/mp4", [".avi"] = "video/x-msvideo", [".mov"] = "video/quicktime",
            [".wmv"] = "video/x-ms-wmv", [".flv"] = "video/x-flv", [".webm"] = "video/webm",
            [".mkv"] = "video/x-matroska",
            
            // Audio formats
            [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav", [".flac"] = "audio/flac",
            [".aac"] = "audio/aac", [".ogg"] = "audio/ogg", [".m4a"] = "audio/mp4",
            
            // Document formats
            [".txt"] = "text/plain", [".rtf"] = "application/rtf", [".pdf"] = "application/pdf",
            [".doc"] = "application/msword", [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            [".md"] = "text/markdown",
            
            // Spreadsheet formats
            [".xls"] = "application/vnd.ms-excel", [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            [".csv"] = "text/csv", [".ods"] = "application/vnd.oasis.opendocument.spreadsheet"
        };
    }

    private MediaType DetermineMediaType(string extension)
    {
        foreach (var (mediaType, extensions) in _supportedFormats)
        {
            if (extensions.Contains(extension))
                return mediaType;
        }
        return MediaType.Text;
    }

    private string GetMimeType(string extension)
    {
        return _mimeTypes.TryGetValue(extension, out var mimeType) ? mimeType : "application/octet-stream";
    }

    private bool DetermineConversionRequirement(string extension, MediaType mediaType)
    {
        // Some formats might need conversion for optimal processing
        return mediaType switch
        {
            MediaType.Image => extension is ".tiff" or ".tif" or ".svg",
            MediaType.Video => extension is ".flv" or ".wmv",
            MediaType.Audio => extension is ".flac" or ".ogg",
            MediaType.Document => extension is ".doc" or ".rtf",
            MediaType.Spreadsheet => extension is ".ods",
            _ => false
        };
    }

    private async Task<bool> ValidateFileHeaderAsync(string filePath, string extension, byte[] headerBytes)
    {
        if (headerBytes.Length == 0)
            return false;

        // Basic file signature validation
        return extension switch
        {
            ".jpg" or ".jpeg" => headerBytes.Length >= 2 && headerBytes[0] == 0xFF && headerBytes[1] == 0xD8,
            ".png" => headerBytes.Length >= 8 && headerBytes[0] == 0x89 && headerBytes[1] == 0x50 && headerBytes[2] == 0x4E && headerBytes[3] == 0x47,
            ".gif" => headerBytes.Length >= 6 && Encoding.ASCII.GetString(headerBytes, 0, 6) == "GIF89a" || Encoding.ASCII.GetString(headerBytes, 0, 6) == "GIF87a",
            ".pdf" => headerBytes.Length >= 4 && Encoding.ASCII.GetString(headerBytes, 0, 4) == "%PDF",
            ".zip" or ".docx" or ".xlsx" => headerBytes.Length >= 4 && headerBytes[0] == 0x50 && headerBytes[1] == 0x4B,
            _ => await Task.FromResult(true) // Default to valid for other formats
        };
    }

    private async Task<(int Width, int Height)> DetectImageDimensionsAsync(string filePath, string extension)
    {
        // Simplified dimension detection - in a real implementation, you'd use proper image libraries
        return await Task.FromResult((800, 600)); // Default dimensions
    }

    private async Task<(int Width, int Height, double FrameRate, TimeSpan Duration)> DetectVideoMetadataAsync(string filePath, string extension)
    {
        // Simplified metadata detection - in a real implementation, you'd use proper video libraries
        return await Task.FromResult((1920, 1080, 30.0, TimeSpan.FromMinutes(5))); // Default metadata
    }

    private async Task<(int SampleRate, int Channels, int BitDepth, TimeSpan Duration)> DetectAudioMetadataAsync(string filePath, string extension)
    {
        // Simplified metadata detection - in a real implementation, you'd use proper audio libraries
        return await Task.FromResult((44100, 2, 16, TimeSpan.FromMinutes(3))); // Default metadata
    }

    private async Task<string> ExtractDocumentTextAsync(string filePath, string extension)
    {
        try
        {
            switch (extension.ToLowerInvariant())
            {
                case ".txt":
                    return await File.ReadAllTextAsync(filePath);
                
                case ".md":
                    var markdownContent = await File.ReadAllTextAsync(filePath);
                    // Basic markdown processing - remove common markdown syntax
                    return ProcessMarkdownText(markdownContent);
                
                case ".rtf":
                    return await ExtractRtfTextAsync(filePath);
                
                case ".pdf":
                    return await ExtractPdfTextAsync(filePath);
                
                case ".doc":
                    return await ExtractDocTextAsync(filePath);
                
                case ".docx":
                    return await ExtractDocxTextAsync(filePath);
                
                default:
                    return $"[Text content extracted from {extension} file: {Path.GetFileName(filePath)}]";
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract text from {extension} file: {ex.Message}", ex);
        }
    }

    private async Task<(Dictionary<string, object> Sheets, int TotalRows, int TotalColumns)> ExtractSpreadsheetDataAsync(string filePath, string extension)
    {
        try
        {
            switch (extension.ToLowerInvariant())
            {
                case ".csv":
                    return await ExtractCsvDataAsync(filePath);
                
                case ".xls":
                    return await ExtractXlsDataAsync(filePath);
                
                case ".xlsx":
                    return await ExtractXlsxDataAsync(filePath);
                
                case ".ods":
                    return await ExtractOdsDataAsync(filePath);
                
                default:
                    var defaultSheets = new Dictionary<string, object>
                    {
                        ["Sheet1"] = $"[Data extracted from {extension} file: {Path.GetFileName(filePath)}]"
                    };
                    return (defaultSheets, 1, 1);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to extract data from {extension} file: {ex.Message}", ex);
        }
    }

    private int CountWords(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        
        return text.Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length;
    }

    private int EstimatePageCount(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
        
        // Rough estimate: 250 words per page
        var wordCount = CountWords(text);
        return Math.Max(1, (int)Math.Ceiling(wordCount / 250.0));
    }

    // Document processing methods
    private string ProcessMarkdownText(string markdownContent)
    {
        if (string.IsNullOrEmpty(markdownContent))
            return string.Empty;

        // Remove common markdown syntax for plain text extraction
        var text = markdownContent;
        
        // Remove headers
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^#{1,6}\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        
        // Remove bold and italic
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*\*(.*?)\*\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\*(.*?)\*", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"__(.*?)__", "$1");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"_(.*?)_", "$1");
        
        // Remove links
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[([^\]]+)\]\([^\)]+\)", "$1");
        
        // Remove code blocks
        text = System.Text.RegularExpressions.Regex.Replace(text, @"```[\s\S]*?```", "");
        text = System.Text.RegularExpressions.Regex.Replace(text, @"`([^`]+)`", "$1");
        
        // Remove list markers
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*[-\*\+]\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"^[\s]*\d+\.\s+", "", System.Text.RegularExpressions.RegexOptions.Multiline);
        
        return text.Trim();
    }

    private async Task<string> ExtractRtfTextAsync(string filePath)
    {
        // Basic RTF text extraction - removes RTF control codes
        var rtfContent = await File.ReadAllTextAsync(filePath);
        
        if (string.IsNullOrEmpty(rtfContent) || !rtfContent.StartsWith(@"{\rtf"))
        {
            throw new InvalidOperationException("Invalid RTF file format");
        }

        // Simple RTF parsing - remove control words and extract plain text
        var text = new StringBuilder();
        var inControlWord = false;
        var inGroup = 0;
        
        for (int i = 0; i < rtfContent.Length; i++)
        {
            char c = rtfContent[i];
            
            switch (c)
            {
                case '\\':
                    inControlWord = true;
                    // Skip control word
                    while (i + 1 < rtfContent.Length && (char.IsLetter(rtfContent[i + 1]) || char.IsDigit(rtfContent[i + 1]) || rtfContent[i + 1] == '-'))
                    {
                        i++;
                    }
                    // Skip optional space after control word
                    if (i + 1 < rtfContent.Length && rtfContent[i + 1] == ' ')
                    {
                        i++;
                    }
                    inControlWord = false;
                    break;
                    
                case '{':
                    inGroup++;
                    break;
                    
                case '}':
                    inGroup--;
                    break;
                    
                default:
                    if (!inControlWord && inGroup > 0)
                    {
                        text.Append(c);
                    }
                    break;
            }
        }
        
        return text.ToString().Trim();
    }

    private async Task<string> ExtractPdfTextAsync(string filePath)
    {
        // Simplified PDF text extraction
        // In a real implementation, you would use a PDF library like iTextSharp or PdfSharp
        var fileInfo = new FileInfo(filePath);
        
        // Basic PDF validation
        var header = new byte[5];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 5);
        }
        
        if (Encoding.ASCII.GetString(header) != "%PDF-")
        {
            throw new InvalidOperationException("Invalid PDF file format");
        }
        
        // For now, return a placeholder indicating PDF processing
        return await Task.FromResult($"[PDF text content from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]");
    }

    private async Task<string> ExtractDocTextAsync(string filePath)
    {
        // Simplified DOC text extraction
        // In a real implementation, you would use a library like Microsoft.Office.Interop.Word or Open XML SDK
        var fileInfo = new FileInfo(filePath);
        
        // Basic DOC validation - check for OLE compound document signature
        var header = new byte[8];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 8);
        }
        
        // DOC files start with OLE compound document signature
        if (header[0] != 0xD0 || header[1] != 0xCF || header[2] != 0x11 || header[3] != 0xE0)
        {
            throw new InvalidOperationException("Invalid DOC file format");
        }
        
        // For now, return a placeholder indicating DOC processing
        return await Task.FromResult($"[DOC text content from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]");
    }

    private async Task<string> ExtractDocxTextAsync(string filePath)
    {
        // Simplified DOCX text extraction
        // In a real implementation, you would use Open XML SDK or similar library
        var fileInfo = new FileInfo(filePath);
        
        // Basic DOCX validation - it's a ZIP file
        var header = new byte[4];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 4);
        }
        
        // DOCX files are ZIP archives (PK signature)
        if (header[0] != 0x50 || header[1] != 0x4B)
        {
            throw new InvalidOperationException("Invalid DOCX file format");
        }
        
        // For now, return a placeholder indicating DOCX processing
        return await Task.FromResult($"[DOCX text content from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]");
    }

    // Spreadsheet processing methods
    private async Task<(Dictionary<string, object> Sheets, int TotalRows, int TotalColumns)> ExtractCsvDataAsync(string filePath)
    {
        var lines = await File.ReadAllLinesAsync(filePath);
        var sheets = new Dictionary<string, object>();
        var rows = new List<string[]>();
        int maxColumns = 0;
        
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
                
            var columns = ParseCsvLine(line);
            rows.Add(columns);
            maxColumns = Math.Max(maxColumns, columns.Length);
        }
        
        sheets["Sheet1"] = rows;
        return (sheets, rows.Count, maxColumns);
    }

    private async Task<(Dictionary<string, object> Sheets, int TotalRows, int TotalColumns)> ExtractXlsDataAsync(string filePath)
    {
        // Simplified XLS data extraction
        // In a real implementation, you would use a library like EPPlus or ClosedXML
        var fileInfo = new FileInfo(filePath);
        
        // Basic XLS validation - check for OLE compound document signature
        var header = new byte[8];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 8);
        }
        
        // XLS files start with OLE compound document signature
        if (header[0] != 0xD0 || header[1] != 0xCF || header[2] != 0x11 || header[3] != 0xE0)
        {
            throw new InvalidOperationException("Invalid XLS file format");
        }
        
        var sheets = new Dictionary<string, object>
        {
            ["Sheet1"] = $"[XLS data from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]"
        };
        
        return await Task.FromResult((sheets, 10, 5)); // Placeholder data
    }

    private async Task<(Dictionary<string, object> Sheets, int TotalRows, int TotalColumns)> ExtractXlsxDataAsync(string filePath)
    {
        // Simplified XLSX data extraction
        // In a real implementation, you would use Open XML SDK or EPPlus
        var fileInfo = new FileInfo(filePath);
        
        // Basic XLSX validation - it's a ZIP file
        var header = new byte[4];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 4);
        }
        
        // XLSX files are ZIP archives (PK signature)
        if (header[0] != 0x50 || header[1] != 0x4B)
        {
            throw new InvalidOperationException("Invalid XLSX file format");
        }
        
        var sheets = new Dictionary<string, object>
        {
            ["Sheet1"] = $"[XLSX data from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]"
        };
        
        return await Task.FromResult((sheets, 15, 8)); // Placeholder data
    }

    private async Task<(Dictionary<string, object> Sheets, int TotalRows, int TotalColumns)> ExtractOdsDataAsync(string filePath)
    {
        // Simplified ODS data extraction
        // In a real implementation, you would use a library that supports OpenDocument format
        var fileInfo = new FileInfo(filePath);
        
        // Basic ODS validation - it's a ZIP file
        var header = new byte[4];
        using (var stream = File.OpenRead(filePath))
        {
            await stream.ReadAsync(header, 0, 4);
        }
        
        // ODS files are ZIP archives (PK signature)
        if (header[0] != 0x50 || header[1] != 0x4B)
        {
            throw new InvalidOperationException("Invalid ODS file format");
        }
        
        var sheets = new Dictionary<string, object>
        {
            ["Sheet1"] = $"[ODS data from {Path.GetFileName(filePath)} - {fileInfo.Length} bytes]"
        };
        
        return await Task.FromResult((sheets, 12, 6)); // Placeholder data
    }

    private string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    // Escaped quote
                    current.Append('"');
                    i++; // Skip next quote
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }
        
        result.Add(current.ToString());
        return result.ToArray();
    }

    // Content analysis methods
    private async Task AnalyzeDocumentContentAsync(DocumentData documentData)
    {
        if (string.IsNullOrWhiteSpace(documentData.TextContent))
            return;

        var text = documentData.TextContent;
        
        // Basic text analysis
        var sentences = CountSentences(text);
        var paragraphs = CountParagraphs(text);
        var averageWordsPerSentence = sentences > 0 ? (double)documentData.WordCount / sentences : 0;
        var readingTimeMinutes = Math.Ceiling(documentData.WordCount / 200.0); // Average reading speed
        
        // Language detection (simplified)
        var detectedLanguage = DetectLanguage(text);
        
        // Content type analysis
        var contentType = AnalyzeContentType(text);
        
        // Add analysis results to metadata
        documentData.Metadata["SentenceCount"] = sentences;
        documentData.Metadata["ParagraphCount"] = paragraphs;
        documentData.Metadata["AverageWordsPerSentence"] = Math.Round(averageWordsPerSentence, 2);
        documentData.Metadata["EstimatedReadingTimeMinutes"] = readingTimeMinutes;
        documentData.Metadata["DetectedLanguage"] = detectedLanguage;
        documentData.Metadata["ContentType"] = contentType;
        documentData.Metadata["CharacterCount"] = text.Length;
        documentData.Metadata["CharacterCountNoSpaces"] = text.Count(c => !char.IsWhiteSpace(c));
        
        await Task.CompletedTask;
    }

    private async Task AnalyzeSpreadsheetContentAsync(SpreadsheetData spreadsheetData)
    {
        var totalCells = spreadsheetData.TotalRows * spreadsheetData.TotalColumns;
        var sheetCount = spreadsheetData.Sheets.Count;
        
        // Analyze sheet structure
        var hasHeaders = AnalyzeSheetHeaders(spreadsheetData.Sheets);
        var dataTypes = AnalyzeDataTypes(spreadsheetData.Sheets);
        
        // Add analysis results to metadata
        spreadsheetData.Metadata["SheetCount"] = sheetCount;
        spreadsheetData.Metadata["TotalCells"] = totalCells;
        spreadsheetData.Metadata["HasHeaders"] = hasHeaders;
        spreadsheetData.Metadata["DataTypes"] = dataTypes;
        spreadsheetData.Metadata["Density"] = CalculateDataDensity(spreadsheetData.Sheets, totalCells);
        
        await Task.CompletedTask;
    }

    private int CountSentences(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
            
        var sentenceEnders = new[] { '.', '!', '?' };
        return text.Count(c => sentenceEnders.Contains(c));
    }

    private int CountParagraphs(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return 0;
            
        var paragraphs = text.Split(new[] { "\n\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries);
        return paragraphs.Length;
    }

    private string DetectLanguage(string text)
    {
        // Simplified language detection based on common words
        if (string.IsNullOrWhiteSpace(text))
            return "unknown";
            
        var lowerText = text.ToLowerInvariant();
        
        // English indicators
        var englishWords = new[] { "the", "and", "is", "in", "to", "of", "a", "that", "it", "with" };
        var englishCount = englishWords.Count(word => lowerText.Contains($" {word} "));
        
        // Spanish indicators
        var spanishWords = new[] { "el", "la", "de", "que", "y", "en", "un", "es", "se", "no" };
        var spanishCount = spanishWords.Count(word => lowerText.Contains($" {word} "));
        
        // French indicators
        var frenchWords = new[] { "le", "de", "et", "à", "un", "il", "être", "et", "en", "avoir" };
        var frenchCount = frenchWords.Count(word => lowerText.Contains($" {word} "));
        
        if (englishCount >= spanishCount && englishCount >= frenchCount)
            return "en";
        else if (spanishCount >= frenchCount)
            return "es";
        else if (frenchCount > 0)
            return "fr";
        else
            return "unknown";
    }

    private string AnalyzeContentType(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "empty";
            
        var lowerText = text.ToLowerInvariant();
        
        // Technical document indicators
        if (lowerText.Contains("function") || lowerText.Contains("class") || lowerText.Contains("method") ||
            lowerText.Contains("algorithm") || lowerText.Contains("implementation"))
            return "technical";
            
        // Academic/research indicators
        if (lowerText.Contains("abstract") || lowerText.Contains("conclusion") || lowerText.Contains("methodology") ||
            lowerText.Contains("references") || lowerText.Contains("bibliography"))
            return "academic";
            
        // Business document indicators
        if (lowerText.Contains("proposal") || lowerText.Contains("budget") || lowerText.Contains("revenue") ||
            lowerText.Contains("quarterly") || lowerText.Contains("stakeholder"))
            return "business";
            
        // Legal document indicators
        if (lowerText.Contains("whereas") || lowerText.Contains("hereby") || lowerText.Contains("pursuant") ||
            lowerText.Contains("agreement") || lowerText.Contains("contract"))
            return "legal";
            
        return "general";
    }

    private bool AnalyzeSheetHeaders(Dictionary<string, object> sheets)
    {
        // Simple heuristic: if first row contains mostly text and subsequent rows contain mixed data types
        foreach (var sheet in sheets.Values)
        {
            if (sheet is List<string[]> rows && rows.Count > 1)
            {
                var firstRow = rows[0];
                var secondRow = rows.Count > 1 ? rows[1] : null;
                
                // Check if first row looks like headers (mostly text, no numbers)
                var firstRowHasNumbers = firstRow.Any(cell => double.TryParse(cell, out _));
                var secondRowHasNumbers = secondRow?.Any(cell => double.TryParse(cell, out _)) ?? false;
                
                if (!firstRowHasNumbers && secondRowHasNumbers)
                    return true;
            }
        }
        
        return false;
    }

    private List<string> AnalyzeDataTypes(Dictionary<string, object> sheets)
    {
        var dataTypes = new HashSet<string>();
        
        foreach (var sheet in sheets.Values)
        {
            if (sheet is List<string[]> rows)
            {
                foreach (var row in rows)
                {
                    foreach (var cell in row)
                    {
                        if (string.IsNullOrWhiteSpace(cell))
                            dataTypes.Add("empty");
                        else if (double.TryParse(cell, out _))
                            dataTypes.Add("numeric");
                        else if (DateTime.TryParse(cell, out _))
                            dataTypes.Add("date");
                        else if (cell.Length == 1 && char.IsLetter(cell[0]))
                            dataTypes.Add("single_char");
                        else
                            dataTypes.Add("text");
                    }
                }
            }
        }
        
        return dataTypes.ToList();
    }

    private double CalculateDataDensity(Dictionary<string, object> sheets, int totalCells)
    {
        if (totalCells == 0)
            return 0.0;
            
        int nonEmptyCells = 0;
        
        foreach (var sheet in sheets.Values)
        {
            if (sheet is List<string[]> rows)
            {
                foreach (var row in rows)
                {
                    nonEmptyCells += row.Count(cell => !string.IsNullOrWhiteSpace(cell));
                }
            }
        }
        
        return Math.Round((double)nonEmptyCells / totalCells, 3);
    }

    private List<string> GetSupportedFormatsForMediaType(MediaType mediaType)
    {
        return _supportedFormats.TryGetValue(mediaType, out var formats) ? formats : new List<string>();
    }

    private async Task<bool> PerformFormatConversionAsync(string inputPath, string outputPath, FileFormat inputFormat, string targetFormat)
    {
        try
        {
            switch (inputFormat.MediaType)
            {
                case MediaType.Image:
                    return await ConvertImageFormatAsync(inputPath, outputPath, inputFormat.FileExtension, targetFormat);
                
                case MediaType.Video:
                    return await ConvertVideoFormatAsync(inputPath, outputPath, inputFormat.FileExtension, targetFormat);
                
                case MediaType.Audio:
                    return await ConvertAudioFormatAsync(inputPath, outputPath, inputFormat.FileExtension, targetFormat);
                
                case MediaType.Document:
                    return await ConvertDocumentFormatAsync(inputPath, outputPath, inputFormat.FileExtension, targetFormat);
                
                case MediaType.Spreadsheet:
                    return await ConvertSpreadsheetFormatAsync(inputPath, outputPath, inputFormat.FileExtension, targetFormat);
                
                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Format conversion failed: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConvertImageFormatAsync(string inputPath, string outputPath, string inputFormat, string targetFormat)
    {
        // Simplified image format conversion
        // In a real implementation, you would use image processing libraries like ImageSharp or System.Drawing
        
        ConversionProgress?.Invoke(this, $"Converting image from {inputFormat} to {targetFormat}...");
        
        try
        {
            // For now, just copy the file and log the conversion
            // In a real implementation, this would perform actual format conversion
            File.Copy(inputPath, outputPath, true);
            
            ConversionProgress?.Invoke(this, "Image format conversion completed");
            return true;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Image conversion error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConvertVideoFormatAsync(string inputPath, string outputPath, string inputFormat, string targetFormat)
    {
        // Simplified video format conversion
        // In a real implementation, you would use video processing libraries like FFMpegCore
        
        ConversionProgress?.Invoke(this, $"Converting video from {inputFormat} to {targetFormat}...");
        
        try
        {
            // For now, just copy the file and log the conversion
            // In a real implementation, this would perform actual format conversion using FFmpeg
            File.Copy(inputPath, outputPath, true);
            
            ConversionProgress?.Invoke(this, "Video format conversion completed");
            return true;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Video conversion error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConvertAudioFormatAsync(string inputPath, string outputPath, string inputFormat, string targetFormat)
    {
        // Simplified audio format conversion
        // In a real implementation, you would use audio processing libraries like NAudio
        
        ConversionProgress?.Invoke(this, $"Converting audio from {inputFormat} to {targetFormat}...");
        
        try
        {
            // For now, just copy the file and log the conversion
            // In a real implementation, this would perform actual format conversion
            File.Copy(inputPath, outputPath, true);
            
            ConversionProgress?.Invoke(this, "Audio format conversion completed");
            return true;
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Audio conversion error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConvertDocumentFormatAsync(string inputPath, string outputPath, string inputFormat, string targetFormat)
    {
        // Simplified document format conversion
        // In a real implementation, you would use document processing libraries
        
        ConversionProgress?.Invoke(this, $"Converting document from {inputFormat} to {targetFormat}...");
        
        try
        {
            if (targetFormat == ".txt")
            {
                // Convert to plain text by extracting text content
                var textContent = await ExtractDocumentTextAsync(inputPath, inputFormat);
                await File.WriteAllTextAsync(outputPath, textContent);
                
                ConversionProgress?.Invoke(this, "Document converted to plain text");
                return true;
            }
            else
            {
                // For other conversions, just copy for now
                File.Copy(inputPath, outputPath, true);
                ConversionProgress?.Invoke(this, "Document format conversion completed");
                return true;
            }
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Document conversion error: {ex.Message}");
            return false;
        }
    }

    private async Task<bool> ConvertSpreadsheetFormatAsync(string inputPath, string outputPath, string inputFormat, string targetFormat)
    {
        // Simplified spreadsheet format conversion
        // In a real implementation, you would use spreadsheet processing libraries like EPPlus
        
        ConversionProgress?.Invoke(this, $"Converting spreadsheet from {inputFormat} to {targetFormat}...");
        
        try
        {
            if (targetFormat == ".csv")
            {
                // Convert to CSV by extracting data
                var spreadsheetData = await ExtractSpreadsheetDataAsync(inputPath, inputFormat);
                await ConvertToCsvAsync(spreadsheetData, outputPath);
                
                ConversionProgress?.Invoke(this, "Spreadsheet converted to CSV");
                return true;
            }
            else
            {
                // For other conversions, just copy for now
                File.Copy(inputPath, outputPath, true);
                ConversionProgress?.Invoke(this, "Spreadsheet format conversion completed");
                return true;
            }
        }
        catch (Exception ex)
        {
            FormatError?.Invoke(this, $"Spreadsheet conversion error: {ex.Message}");
            return false;
        }
    }

    private async Task ConvertToCsvAsync((Dictionary<string, object> Sheets, int TotalRows, int TotalColumns) spreadsheetData, string outputPath)
    {
        var csvContent = new StringBuilder();
        
        // Convert first sheet to CSV
        var firstSheet = spreadsheetData.Sheets.FirstOrDefault();
        if (firstSheet.Value is List<string[]> rows)
        {
            foreach (var row in rows)
            {
                var csvRow = string.Join(",", row.Select(cell => 
                {
                    // Escape CSV values that contain commas or quotes
                    if (cell.Contains(",") || cell.Contains("\"") || cell.Contains("\n"))
                    {
                        return $"\"{cell.Replace("\"", "\"\"")}\"";
                    }
                    return cell;
                }));
                csvContent.AppendLine(csvRow);
            }
        }
        
        await File.WriteAllTextAsync(outputPath, csvContent.ToString());
    }

    private async Task<ValidationResult> ValidateFileHeaderWithRecoveryAsync(string filePath, string extension, byte[] headerBytes)
    {
        var result = new ValidationResult
        {
            IsValid = true,
            CanProcess = true
        };

        if (headerBytes.Length == 0)
        {
            result.IsValid = false;
            result.ErrorMessages.Add("File appears to be empty or unreadable");
            result.ErrorMessages.Add("• Check if the file is currently being used by another application");
            result.ErrorMessages.Add("• Verify the file is not corrupted");
            return result;
        }

        var isValidHeader = await ValidateFileHeaderAsync(filePath, extension, headerBytes);
        
        if (!isValidHeader)
        {
            result.IsValid = false;
            result.ErrorMessages.Add($"File header does not match expected format for {extension} files");
            
            // Provide recovery suggestions based on file type
            switch (extension.ToLowerInvariant())
            {
                case ".jpg" or ".jpeg":
                    result.ErrorMessages.Add("• JPEG files should start with FF D8 bytes");
                    result.ErrorMessages.Add("• Try opening the file in an image editor to verify it's a valid JPEG");
                    break;
                    
                case ".png":
                    result.ErrorMessages.Add("• PNG files should start with 89 50 4E 47 bytes");
                    result.ErrorMessages.Add("• Try opening the file in an image editor to verify it's a valid PNG");
                    break;
                    
                case ".pdf":
                    result.ErrorMessages.Add("• PDF files should start with %PDF");
                    result.ErrorMessages.Add("• Try opening the file in a PDF reader to verify it's valid");
                    break;
                    
                case ".docx" or ".xlsx":
                    result.ErrorMessages.Add("• Office documents should be valid ZIP archives");
                    result.ErrorMessages.Add("• Try opening the file in Microsoft Office to verify it's valid");
                    break;
                    
                default:
                    result.ErrorMessages.Add("• The file may have an incorrect extension");
                    result.ErrorMessages.Add("• Try determining the actual file type using file analysis tools");
                    break;
            }
            
            result.ErrorMessages.Add("• Consider re-downloading or obtaining a fresh copy of the file");
        }

        return result;
    }

    private async Task PerformFormatSpecificValidationAsync(string filePath, ValidationResult result)
    {
        var extension = result.FormatDetected.FileExtension.ToLowerInvariant();
        
        try
        {
            switch (extension)
            {
                case ".jpg" or ".jpeg":
                    await ValidateJpegSpecificAsync(filePath, result);
                    break;
                    
                case ".png":
                    await ValidatePngSpecificAsync(filePath, result);
                    break;
                    
                case ".pdf":
                    await ValidatePdfSpecificAsync(filePath, result);
                    break;
                    
                case ".docx" or ".xlsx" or ".pptx":
                    await ValidateOfficeDocumentAsync(filePath, result);
                    break;
                    
                case ".csv":
                    await ValidateCsvSpecificAsync(filePath, result);
                    break;
                    
                case ".mp4":
                    await ValidateMp4SpecificAsync(filePath, result);
                    break;
                    
                case ".mp3":
                    await ValidateMp3SpecificAsync(filePath, result);
                    break;
                    
                case ".wav":
                    await ValidateWavSpecificAsync(filePath, result);
                    break;
                    
                default:
                    // No specific validation for other formats
                    break;
            }
        }
        catch (Exception ex)
        {
            result.Warnings.Add($"Format-specific validation failed: {ex.Message}");
        }
    }

    private async Task ValidateJpegSpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[10];
        await stream.ReadAsync(buffer, 0, buffer.Length);
        
        // Check for JPEG end marker
        stream.Seek(-2, SeekOrigin.End);
        var endBuffer = new byte[2];
        await stream.ReadAsync(endBuffer, 0, 2);
        
        if (endBuffer[0] != 0xFF || endBuffer[1] != 0xD9)
        {
            result.Warnings.Add("JPEG file may be truncated or corrupted (missing end marker)");
        }
    }

    private async Task ValidatePngSpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[8];
        await stream.ReadAsync(buffer, 0, buffer.Length);
        
        // PNG signature: 89 50 4E 47 0D 0A 1A 0A
        var expectedSignature = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        
        for (int i = 0; i < expectedSignature.Length; i++)
        {
            if (buffer[i] != expectedSignature[i])
            {
                result.ErrorMessages.Add("Invalid PNG signature");
                return;
            }
        }
    }

    private async Task ValidatePdfSpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[1024];
        var bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
        var content = Encoding.ASCII.GetString(buffer, 0, bytesRead);
        
        if (!content.Contains("%PDF-"))
        {
            result.ErrorMessages.Add("Invalid PDF format - missing PDF header");
            return;
        }
        
        // Check for PDF version
        var versionMatch = System.Text.RegularExpressions.Regex.Match(content, @"%PDF-(\d+\.\d+)");
        if (versionMatch.Success)
        {
            var version = versionMatch.Groups[1].Value;
            result.Warnings.Add($"PDF version: {version}");
            
            if (double.TryParse(version, out var versionNumber) && versionNumber > 2.0)
            {
                result.Warnings.Add("PDF version is very recent - ensure compatibility");
            }
        }
    }

    private async Task ValidateOfficeDocumentAsync(string filePath, ValidationResult result)
    {
        try
        {
            using var stream = File.OpenRead(filePath);
            var buffer = new byte[4];
            await stream.ReadAsync(buffer, 0, buffer.Length);
            
            // Office documents are ZIP files
            if (buffer[0] != 0x50 || buffer[1] != 0x4B)
            {
                result.ErrorMessages.Add("Invalid Office document format - not a valid ZIP archive");
                result.ErrorMessages.Add("• Try opening the file in Microsoft Office to verify it's valid");
                result.ErrorMessages.Add("• Check if the file extension is correct");
                result.ErrorMessages.Add("• Verify the file was not corrupted during transfer");
                return;
            }
            
            // For files that are likely test files or very small, just check the ZIP header is present
            if (stream.Length < 2048) // Files smaller than 2KB are likely test files or incomplete
            {
                result.Warnings.Add("Office document is very small and may be incomplete");
                result.Warnings.Add("• Try opening the file in Microsoft Office to verify it's valid");
                return;
            }
            
            // Try to read as ZIP to validate structure for larger files
            stream.Seek(0, SeekOrigin.Begin);
            try
            {
                using var archive = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Read);
                
                var hasContentTypes = archive.Entries.Any(e => e.FullName == "[Content_Types].xml");
                if (!hasContentTypes)
                {
                    result.Warnings.Add("Office document may be corrupted - missing content types");
                    result.Warnings.Add("• Try opening the file in Microsoft Office to verify it's valid");
                    result.Warnings.Add("• Consider re-saving the file to fix potential corruption");
                }
            }
            catch (Exception zipEx)
            {
                result.ErrorMessages.Add($"Office document validation failed: {zipEx.Message}");
                result.ErrorMessages.Add("• Try opening the file in Microsoft Office to verify it's valid");
                result.ErrorMessages.Add("• Check if the file was corrupted during transfer");
                result.ErrorMessages.Add("• Consider using a file repair tool");
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessages.Add($"Office document validation failed: {ex.Message}");
            result.ErrorMessages.Add("• Try opening the file in Microsoft Office to verify it's valid");
            result.ErrorMessages.Add("• Check if the file extension is correct");
            result.ErrorMessages.Add("• Verify the file was not corrupted during transfer");
        }
    }

    private async Task ValidateCsvSpecificAsync(string filePath, ValidationResult result)
    {
        try
        {
            var lines = await File.ReadAllLinesAsync(filePath);
            if (lines.Length == 0)
            {
                result.ErrorMessages.Add("CSV file is empty");
                return;
            }
            
            // Check for consistent column count
            var firstLineColumns = ParseCsvLine(lines[0]).Length;
            var inconsistentRows = 0;
            
            for (int i = 1; i < Math.Min(lines.Length, 100); i++) // Check first 100 rows
            {
                var columnCount = ParseCsvLine(lines[i]).Length;
                if (columnCount != firstLineColumns)
                {
                    inconsistentRows++;
                }
            }
            
            if (inconsistentRows > 0)
            {
                result.Warnings.Add($"CSV has inconsistent column counts in {inconsistentRows} rows");
            }
        }
        catch (Exception ex)
        {
            result.ErrorMessages.Add($"CSV validation failed: {ex.Message}");
        }
    }

    private async Task ValidateMp4SpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[12];
        await stream.ReadAsync(buffer, 0, buffer.Length);
        
        // MP4 files typically start with ftyp box
        var ftypSignature = Encoding.ASCII.GetString(buffer, 4, 4);
        if (ftypSignature != "ftyp")
        {
            result.Warnings.Add("MP4 file may not have standard structure");
        }
    }

    private async Task ValidateMp3SpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[10];
        await stream.ReadAsync(buffer, 0, buffer.Length);
        
        // Check for ID3 tag or MP3 frame sync
        var hasId3 = buffer[0] == 0x49 && buffer[1] == 0x44 && buffer[2] == 0x33; // "ID3"
        var hasMpegSync = (buffer[0] == 0xFF && (buffer[1] & 0xE0) == 0xE0);
        
        if (!hasId3 && !hasMpegSync)
        {
            result.Warnings.Add("MP3 file may not have standard format markers");
        }
    }

    private async Task ValidateWavSpecificAsync(string filePath, ValidationResult result)
    {
        using var stream = File.OpenRead(filePath);
        var buffer = new byte[12];
        await stream.ReadAsync(buffer, 0, buffer.Length);
        
        // WAV files should have RIFF header followed by WAVE identifier
        var riffHeader = Encoding.ASCII.GetString(buffer, 0, 4);
        var waveIdentifier = Encoding.ASCII.GetString(buffer, 8, 4);
        
        if (riffHeader != "RIFF")
        {
            result.ErrorMessages.Add("Invalid WAV file - missing RIFF header");
            result.ErrorMessages.Add("• The file may be corrupted or have an incorrect extension");
            result.ErrorMessages.Add("• Try opening the file in an audio player to verify it's valid");
            return;
        }
        
        if (waveIdentifier != "WAVE")
        {
            result.ErrorMessages.Add("Invalid WAV file - corrupted format identifier");
            result.ErrorMessages.Add("• The file may be corrupted or have an incorrect extension");
            result.ErrorMessages.Add("• Try opening the file in an audio player to verify it's valid");
            result.ErrorMessages.Add("• Consider re-downloading or obtaining a fresh copy of the file");
        }
    }
}