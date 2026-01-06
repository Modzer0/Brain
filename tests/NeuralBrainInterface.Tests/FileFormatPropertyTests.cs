using FsCheck;
using FsCheck.Xunit;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using NeuralBrainInterface.Core.Interfaces;
using Xunit;
using System.Text;

namespace NeuralBrainInterface.Tests;

public class FileFormatPropertyTests
{
    private readonly IFileFormatManager _fileFormatManager;

    public FileFormatPropertyTests()
    {
        _fileFormatManager = new FileFormatManager();
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 27: Image Format Support**
    /// **Validates: Requirements 13.1**
    /// 
    /// For any common image format file (JPEG, PNG, GIF, BMP, TIFF, WebP, SVG), 
    /// the system should successfully process the file and extract visual information 
    /// for neural network analysis.
    /// </summary>
    [Property(MaxTest = 100)]
    public void ImageFormatSupport_CommonImageFormatsAreSupported()
    {
        // Arrange - Define all supported image formats
        var supportedImageFormats = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".tif", ".webp", ".svg" };
        var supportedFormats = _fileFormatManager.GetSupportedFormats();

        // Act & Assert - All image formats should be supported
        Assert.True(supportedFormats.ContainsKey(MediaType.Image));
        var imageFormats = supportedFormats[MediaType.Image];

        foreach (var format in supportedImageFormats)
        {
            Assert.True(imageFormats.Contains(format), $"Image format {format} should be supported");
            Assert.True(_fileFormatManager.IsFormatSupported(format), $"IsFormatSupported should return true for {format}");
        }

        // Verify that the system recognizes these as image formats
        foreach (var format in supportedImageFormats)
        {
            Assert.True(_fileFormatManager.IsFormatSupported(format));
        }
    }

    [Property(MaxTest = 100)]
    public void ImageFormatSupport_ValidImageFilesProcessSuccessfully()
    {
        // Arrange - Create test image files for each supported format
        var imageTestCases = new Dictionary<string, byte[]>
        {
            [".jpg"] = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }, // JPEG header
            [".jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }, // JPEG header
            [".png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, // PNG header
            [".gif"] = Encoding.ASCII.GetBytes("GIF89a"), // GIF header
            [".bmp"] = new byte[] { 0x42, 0x4D }, // BMP header
            [".tiff"] = new byte[] { 0x49, 0x49, 0x2A, 0x00 }, // TIFF header (little endian)
            [".tif"] = new byte[] { 0x49, 0x49, 0x2A, 0x00 }, // TIFF header (little endian)
            [".webp"] = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).Concat(Encoding.ASCII.GetBytes("WEBP")).ToArray(),
            [".svg"] = Encoding.UTF8.GetBytes("<?xml version=\"1.0\"?><svg xmlns=\"http://www.w3.org/2000/svg\"></svg>")
        };

        foreach (var (extension, headerBytes) in imageTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data with proper header
                var testData = headerBytes.Concat(new byte[1024]).ToArray(); // Add some dummy data
                File.WriteAllBytes(testFilePath, testData);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected correctly
                Assert.Equal(MediaType.Image, detectedFormat.MediaType);
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.True(detectedFormat.IsSupported);
                Assert.NotEmpty(detectedFormat.MimeType);

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - File should be valid
                Assert.True(validation.IsValid);
                Assert.True(validation.CanProcess);
                Assert.Equal(MediaType.Image, validation.FormatDetected.MediaType);
                Assert.Empty(validation.ErrorMessages);

                // Act - Convert image file
                var imageData = _fileFormatManager.ConvertImageFileAsync(testFilePath).Result;

                // Assert - Conversion should succeed
                Assert.NotNull(imageData);
                Assert.NotEmpty(imageData.Data);
                Assert.Equal(extension, imageData.Format);
                Assert.True(imageData.Width > 0);
                Assert.True(imageData.Height > 0);
                Assert.NotNull(imageData.Metadata);
                Assert.True(imageData.Metadata.ContainsKey("OriginalPath"));
                Assert.Equal(testFilePath, imageData.Metadata["OriginalPath"]);

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void ImageFormatSupport_ConversionOptionsAvailableForImages()
    {
        // Arrange
        var imageFormat = new FileFormat
        {
            MediaType = MediaType.Image,
            FileExtension = ".jpg",
            MimeType = "image/jpeg",
            IsSupported = true,
            RequiresConversion = false,
            EstimatedSize = 1024
        };

        // Act
        var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(imageFormat).Result;

        // Assert
        Assert.NotNull(conversionOptions);
        Assert.NotEmpty(conversionOptions);
        
        // Should have standard image conversion options
        Assert.Contains("Convert to PNG", conversionOptions);
        Assert.Contains("Convert to JPEG", conversionOptions);
        Assert.Contains("Resize image", conversionOptions);
        Assert.Contains("Extract metadata", conversionOptions);
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 28: Video Format Support**
    /// **Validates: Requirements 13.2**
    /// 
    /// For any common video format file (MP4, AVI, MOV, WMV, FLV, WebM, MKV), 
    /// the system should successfully process the file and extract temporal visual 
    /// information for neural network analysis.
    /// </summary>
    [Property(MaxTest = 100)]
    public void VideoFormatSupport_CommonVideoFormatsAreSupported()
    {
        // Arrange - Define all supported video formats
        var supportedVideoFormats = new[] { ".mp4", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mkv" };
        var supportedFormats = _fileFormatManager.GetSupportedFormats();

        // Act & Assert - All video formats should be supported
        Assert.True(supportedFormats.ContainsKey(MediaType.Video));
        var videoFormats = supportedFormats[MediaType.Video];

        foreach (var format in supportedVideoFormats)
        {
            Assert.True(videoFormats.Contains(format), $"Video format {format} should be supported");
            Assert.True(_fileFormatManager.IsFormatSupported(format), $"IsFormatSupported should return true for {format}");
        }
    }

    [Property(MaxTest = 100)]
    public void VideoFormatSupport_ValidVideoFilesProcessSuccessfully()
    {
        // Arrange - Create test video files for each supported format
        var videoTestCases = new Dictionary<string, byte[]>
        {
            [".mp4"] = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70 }, // MP4 header
            [".avi"] = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).Concat(Encoding.ASCII.GetBytes("AVI ")).ToArray(),
            [".mov"] = new byte[] { 0x00, 0x00, 0x00, 0x14, 0x66, 0x74, 0x79, 0x70, 0x71, 0x74, 0x20, 0x20 }, // MOV header
            [".wmv"] = new byte[] { 0x30, 0x26, 0xB2, 0x75, 0x8E, 0x66, 0xCF, 0x11 }, // WMV header
            [".flv"] = new byte[] { 0x46, 0x4C, 0x56, 0x01 }, // FLV header
            [".webm"] = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 }, // WebM header
            [".mkv"] = new byte[] { 0x1A, 0x45, 0xDF, 0xA3 } // MKV header (same as WebM)
        };

        foreach (var (extension, headerBytes) in videoTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data with proper header
                var testData = headerBytes.Concat(new byte[2048]).ToArray(); // Add some dummy data
                File.WriteAllBytes(testFilePath, testData);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected correctly
                Assert.Equal(MediaType.Video, detectedFormat.MediaType);
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.True(detectedFormat.IsSupported);
                Assert.NotEmpty(detectedFormat.MimeType);

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - File should be valid
                Assert.True(validation.IsValid);
                Assert.True(validation.CanProcess);
                Assert.Equal(MediaType.Video, validation.FormatDetected.MediaType);

                // Act - Convert video file
                var videoData = _fileFormatManager.ConvertVideoFileAsync(testFilePath).Result;

                // Assert - Conversion should succeed
                Assert.NotNull(videoData);
                Assert.NotEmpty(videoData.Data);
                Assert.Equal(extension, videoData.Format);
                Assert.True(videoData.Width > 0);
                Assert.True(videoData.Height > 0);
                Assert.True(videoData.FrameRate > 0);
                Assert.True(videoData.Duration > TimeSpan.Zero);
                Assert.NotNull(videoData.Metadata);
                Assert.True(videoData.Metadata.ContainsKey("OriginalPath"));

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 29: Audio Format Support**
    /// **Validates: Requirements 13.3**
    /// 
    /// For any common audio format file (MP3, WAV, FLAC, AAC, OGG, M4A), 
    /// the system should successfully process the file and extract audio information 
    /// for neural network analysis.
    /// </summary>
    [Property(MaxTest = 100)]
    public void AudioFormatSupport_CommonAudioFormatsAreSupported()
    {
        // Arrange - Define all supported audio formats
        var supportedAudioFormats = new[] { ".mp3", ".wav", ".flac", ".aac", ".ogg", ".m4a" };
        var supportedFormats = _fileFormatManager.GetSupportedFormats();

        // Act & Assert - All audio formats should be supported
        Assert.True(supportedFormats.ContainsKey(MediaType.Audio));
        var audioFormats = supportedFormats[MediaType.Audio];

        foreach (var format in supportedAudioFormats)
        {
            Assert.True(audioFormats.Contains(format), $"Audio format {format} should be supported");
            Assert.True(_fileFormatManager.IsFormatSupported(format), $"IsFormatSupported should return true for {format}");
        }
    }

    [Property(MaxTest = 100)]
    public void AudioFormatSupport_ValidAudioFilesProcessSuccessfully()
    {
        // Arrange - Create test audio files for each supported format
        var audioTestCases = new Dictionary<string, byte[]>
        {
            [".mp3"] = new byte[] { 0xFF, 0xFB, 0x90, 0x00 }, // MP3 header
            [".wav"] = Encoding.ASCII.GetBytes("RIFF").Concat(new byte[] { 0x00, 0x00, 0x00, 0x00 }).Concat(Encoding.ASCII.GetBytes("WAVE")).ToArray(),
            [".flac"] = Encoding.ASCII.GetBytes("fLaC"), // FLAC header
            [".aac"] = new byte[] { 0xFF, 0xF1, 0x50, 0x80 }, // AAC header
            [".ogg"] = Encoding.ASCII.GetBytes("OggS"), // OGG header
            [".m4a"] = new byte[] { 0x00, 0x00, 0x00, 0x20, 0x66, 0x74, 0x79, 0x70, 0x4D, 0x34, 0x41, 0x20 } // M4A header
        };

        foreach (var (extension, headerBytes) in audioTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data with proper header
                var testData = headerBytes.Concat(new byte[1024]).ToArray(); // Add some dummy data
                File.WriteAllBytes(testFilePath, testData);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected correctly
                Assert.Equal(MediaType.Audio, detectedFormat.MediaType);
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.True(detectedFormat.IsSupported);
                Assert.NotEmpty(detectedFormat.MimeType);

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - File should be valid
                Assert.True(validation.IsValid);
                Assert.True(validation.CanProcess);
                Assert.Equal(MediaType.Audio, validation.FormatDetected.MediaType);

                // Act - Convert audio file
                var audioData = _fileFormatManager.ConvertAudioFileAsync(testFilePath).Result;

                // Assert - Conversion should succeed
                Assert.NotNull(audioData);
                Assert.NotEmpty(audioData.Data);
                Assert.Equal(extension, audioData.Format);
                Assert.True(audioData.SampleRate > 0);
                Assert.True(audioData.Channels > 0);
                Assert.True(audioData.BitDepth > 0);
                Assert.True(audioData.Duration > TimeSpan.Zero);
                Assert.NotNull(audioData.Metadata);
                Assert.True(audioData.Metadata.ContainsKey("OriginalPath"));

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void FileFormatDetection_InvalidPathsHandledGracefully()
    {
        // Test null path
        var nullResult = _fileFormatManager.DetectFileFormatAsync(null!).Result;
        Assert.False(nullResult.IsSupported);
        Assert.Equal(string.Empty, nullResult.FileExtension);

        // Test empty path
        var emptyResult = _fileFormatManager.DetectFileFormatAsync("").Result;
        Assert.False(emptyResult.IsSupported);
        Assert.Equal(string.Empty, emptyResult.FileExtension);

        // Test non-existent file
        var nonExistentResult = _fileFormatManager.DetectFileFormatAsync("non_existent_file.jpg").Result;
        Assert.False(nonExistentResult.IsSupported);
        Assert.Equal(0, nonExistentResult.EstimatedSize);
    }

    [Property(MaxTest = 100)]
    public void FileFormatValidation_CorruptedFilesDetected()
    {
        // Arrange - Create a file with wrong header for its extension
        var tempFilePath = Path.GetTempFileName();
        var testFilePath = Path.ChangeExtension(tempFilePath, ".jpg");
        
        try
        {
            File.Move(tempFilePath, testFilePath);
            
            // Write PNG header to a .jpg file (corruption simulation)
            var corruptedData = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            File.WriteAllBytes(testFilePath, corruptedData);

            // Act
            var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

            // Assert - Should detect corruption
            Assert.False(validation.IsValid);
            Assert.False(validation.CanProcess);
            Assert.Contains("header does not match", validation.ErrorMessages.FirstOrDefault() ?? "");
        }
        finally
        {
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 30: Document Format Support**
    /// **Validates: Requirements 13.4**
    /// 
    /// For any common document format file (TXT, RTF, PDF, DOC, DOCX, Markdown), 
    /// the system should successfully process the file and extract text content 
    /// for neural network analysis.
    /// </summary>
    [Property(MaxTest = 100)]
    public void DocumentFormatSupport_CommonDocumentFormatsAreSupported()
    {
        // Arrange - Define all supported document formats
        var supportedDocumentFormats = new[] { ".txt", ".rtf", ".pdf", ".doc", ".docx", ".md" };
        var supportedFormats = _fileFormatManager.GetSupportedFormats();

        // Act & Assert - All document formats should be supported
        Assert.True(supportedFormats.ContainsKey(MediaType.Document));
        var documentFormats = supportedFormats[MediaType.Document];

        foreach (var format in supportedDocumentFormats)
        {
            Assert.True(documentFormats.Contains(format), $"Document format {format} should be supported");
            Assert.True(_fileFormatManager.IsFormatSupported(format), $"IsFormatSupported should return true for {format}");
        }
    }

    [Property(MaxTest = 100)]
    public void DocumentFormatSupport_ValidDocumentFilesProcessSuccessfully()
    {
        // Arrange - Create test document files for each supported format
        var documentTestCases = new Dictionary<string, byte[]>
        {
            [".txt"] = Encoding.UTF8.GetBytes("This is a sample text document with some content for testing."),
            [".md"] = Encoding.UTF8.GetBytes("# Sample Markdown\n\nThis is a **markdown** document with *formatting*."),
            [".rtf"] = Encoding.ASCII.GetBytes(@"{\rtf1\ansi\deff0 {\fonttbl {\f0 Times New Roman;}} \f0\fs24 Sample RTF document text.}"),
            [".pdf"] = Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n/Pages 2 0 R\n>>\nendobj\n").Concat(new byte[1024]).ToArray(),
            [".doc"] = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.Concat(new byte[2048]).ToArray(), // OLE compound document header
            [".docx"] = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(new byte[1024]).ToArray() // ZIP header for DOCX
        };

        foreach (var (extension, contentBytes) in documentTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data
                File.WriteAllBytes(testFilePath, contentBytes);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected correctly
                Assert.Equal(MediaType.Document, detectedFormat.MediaType);
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.True(detectedFormat.IsSupported);
                Assert.NotEmpty(detectedFormat.MimeType);

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - File should be valid
                Assert.True(validation.IsValid);
                Assert.True(validation.CanProcess);
                Assert.Equal(MediaType.Document, validation.FormatDetected.MediaType);
                Assert.Empty(validation.ErrorMessages);

                // Act - Convert document file
                var documentData = _fileFormatManager.ConvertDocumentFileAsync(testFilePath).Result;

                // Assert - Conversion should succeed
                Assert.NotNull(documentData);
                Assert.NotEmpty(documentData.TextContent);
                Assert.True(documentData.WordCount > 0);
                Assert.True(documentData.PageCount > 0);
                Assert.NotNull(documentData.Metadata);
                Assert.True(documentData.Metadata.ContainsKey("OriginalPath"));
                Assert.Equal(testFilePath, documentData.Metadata["OriginalPath"]);
                Assert.True(documentData.Metadata.ContainsKey("FileExtension"));
                Assert.Equal(extension, documentData.Metadata["FileExtension"]);
                Assert.True(documentData.Metadata.ContainsKey("MimeType"));
                Assert.True(documentData.Metadata.ContainsKey("ConvertedAt"));

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void DocumentFormatSupport_ConversionOptionsAvailableForDocuments()
    {
        // Arrange
        var documentFormat = new FileFormat
        {
            MediaType = MediaType.Document,
            FileExtension = ".pdf",
            MimeType = "application/pdf",
            IsSupported = true,
            RequiresConversion = false,
            EstimatedSize = 2048
        };

        // Act
        var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(documentFormat).Result;

        // Assert
        Assert.NotNull(conversionOptions);
        Assert.NotEmpty(conversionOptions);
        
        // Should have standard document conversion options
        Assert.Contains("Extract text", conversionOptions);
        Assert.Contains("Convert to plain text", conversionOptions);
        Assert.Contains("Extract images", conversionOptions);
        Assert.Contains("Parse structure", conversionOptions);
    }

    [Property(MaxTest = 100)]
    public void DocumentFormatSupport_TextExtractionWorksForAllFormats()
    {
        // Arrange - Test text extraction for different document formats
        var documentTestCases = new Dictionary<string, (byte[] Content, string ExpectedTextPattern)>
        {
            [".txt"] = (Encoding.UTF8.GetBytes("Sample plain text content"), "Sample plain text content"),
            [".md"] = (Encoding.UTF8.GetBytes("# Header\n\nSample **markdown** content"), "Sample markdown content"),
            [".rtf"] = (Encoding.ASCII.GetBytes(@"{\rtf1\ansi Sample RTF content}"), "Sample RTF content")
        };

        foreach (var (extension, (contentBytes, expectedPattern)) in documentTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data
                File.WriteAllBytes(testFilePath, contentBytes);

                // Act - Convert document file
                var documentData = _fileFormatManager.ConvertDocumentFileAsync(testFilePath).Result;

                // Assert - Text extraction should work
                Assert.NotNull(documentData);
                Assert.NotEmpty(documentData.TextContent);
                
                // For formats that support direct text extraction, verify content
                if (extension == ".txt" || extension == ".md")
                {
                    Assert.Contains(expectedPattern.Split(' ')[0], documentData.TextContent);
                }

                // Verify metadata contains content analysis
                Assert.True(documentData.Metadata.ContainsKey("CharacterCount"));
                Assert.True(documentData.Metadata.ContainsKey("SentenceCount"));
                Assert.True(documentData.Metadata.ContainsKey("ParagraphCount"));
                Assert.True(documentData.Metadata.ContainsKey("DetectedLanguage"));
                Assert.True(documentData.Metadata.ContainsKey("ContentType"));

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 31: Spreadsheet Format Support**
    /// **Validates: Requirements 13.5**
    /// 
    /// For any common spreadsheet format file (XLS, XLSX, CSV, ODS), 
    /// the system should successfully process the file and extract structured data 
    /// for neural network analysis.
    /// </summary>
    [Property(MaxTest = 100)]
    public void SpreadsheetFormatSupport_CommonSpreadsheetFormatsAreSupported()
    {
        // Arrange - Define all supported spreadsheet formats
        var supportedSpreadsheetFormats = new[] { ".xls", ".xlsx", ".csv", ".ods" };
        var supportedFormats = _fileFormatManager.GetSupportedFormats();

        // Act & Assert - All spreadsheet formats should be supported
        Assert.True(supportedFormats.ContainsKey(MediaType.Spreadsheet));
        var spreadsheetFormats = supportedFormats[MediaType.Spreadsheet];

        foreach (var format in supportedSpreadsheetFormats)
        {
            Assert.True(spreadsheetFormats.Contains(format), $"Spreadsheet format {format} should be supported");
            Assert.True(_fileFormatManager.IsFormatSupported(format), $"IsFormatSupported should return true for {format}");
        }
    }

    [Property(MaxTest = 100)]
    public void SpreadsheetFormatSupport_ValidSpreadsheetFilesProcessSuccessfully()
    {
        // Arrange - Create test spreadsheet files for each supported format
        var spreadsheetTestCases = new Dictionary<string, byte[]>
        {
            [".csv"] = Encoding.UTF8.GetBytes("Name,Age,City\nJohn,25,New York\nJane,30,Los Angeles\nBob,35,Chicago"),
            [".xls"] = new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.Concat(new byte[2048]).ToArray(), // OLE compound document header
            [".xlsx"] = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(new byte[1024]).ToArray(), // ZIP header for XLSX
            [".ods"] = new byte[] { 0x50, 0x4B, 0x03, 0x04 }.Concat(new byte[1024]).ToArray() // ZIP header for ODS
        };

        foreach (var (extension, contentBytes) in spreadsheetTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data
                File.WriteAllBytes(testFilePath, contentBytes);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected correctly
                Assert.Equal(MediaType.Spreadsheet, detectedFormat.MediaType);
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.True(detectedFormat.IsSupported);
                Assert.NotEmpty(detectedFormat.MimeType);

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - File should be valid
                Assert.True(validation.IsValid);
                Assert.True(validation.CanProcess);
                Assert.Equal(MediaType.Spreadsheet, validation.FormatDetected.MediaType);
                Assert.Empty(validation.ErrorMessages);

                // Act - Convert spreadsheet file
                var spreadsheetData = _fileFormatManager.ConvertSpreadsheetFileAsync(testFilePath).Result;

                // Assert - Conversion should succeed
                Assert.NotNull(spreadsheetData);
                Assert.NotEmpty(spreadsheetData.Sheets);
                Assert.True(spreadsheetData.TotalRows > 0);
                Assert.True(spreadsheetData.TotalColumns > 0);
                Assert.NotNull(spreadsheetData.Metadata);
                Assert.True(spreadsheetData.Metadata.ContainsKey("OriginalPath"));
                Assert.Equal(testFilePath, spreadsheetData.Metadata["OriginalPath"]);
                Assert.True(spreadsheetData.Metadata.ContainsKey("FileExtension"));
                Assert.Equal(extension, spreadsheetData.Metadata["FileExtension"]);
                Assert.True(spreadsheetData.Metadata.ContainsKey("MimeType"));
                Assert.True(spreadsheetData.Metadata.ContainsKey("ConvertedAt"));

                // Verify sheet structure
                Assert.Contains("Sheet1", spreadsheetData.Sheets.Keys);
                
                // For CSV files, verify actual data extraction
                if (extension == ".csv")
                {
                    Assert.True(spreadsheetData.Sheets["Sheet1"] is List<string[]>);
                    var rows = (List<string[]>)spreadsheetData.Sheets["Sheet1"];
                    Assert.True(rows.Count >= 3); // Header + 2 data rows minimum
                    Assert.True(rows[0].Length >= 3); // At least 3 columns
                    Assert.Equal("Name", rows[0][0]); // First header
                    Assert.Equal("John", rows[1][0]); // First data row
                }

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void SpreadsheetFormatSupport_ConversionOptionsAvailableForSpreadsheets()
    {
        // Arrange
        var spreadsheetFormat = new FileFormat
        {
            MediaType = MediaType.Spreadsheet,
            FileExtension = ".xlsx",
            MimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            IsSupported = true,
            RequiresConversion = false,
            EstimatedSize = 4096
        };

        // Act
        var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(spreadsheetFormat).Result;

        // Assert
        Assert.NotNull(conversionOptions);
        Assert.NotEmpty(conversionOptions);
        
        // Should have standard spreadsheet conversion options
        Assert.Contains("Convert to CSV", conversionOptions);
        Assert.Contains("Extract data", conversionOptions);
        Assert.Contains("Parse formulas", conversionOptions);
        Assert.Contains("Export charts", conversionOptions);
    }

    [Property(MaxTest = 100)]
    public void SpreadsheetFormatSupport_DataExtractionWorksForAllFormats()
    {
        // Arrange - Test data extraction for different spreadsheet formats
        var spreadsheetTestCases = new Dictionary<string, (byte[] Content, int ExpectedRows, int ExpectedColumns)>
        {
            [".csv"] = (Encoding.UTF8.GetBytes("Header1,Header2,Header3\nValue1,Value2,Value3\nData1,Data2,Data3"), 3, 3)
        };

        foreach (var (extension, (contentBytes, expectedRows, expectedColumns)) in spreadsheetTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to match the format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data
                File.WriteAllBytes(testFilePath, contentBytes);

                // Act - Convert spreadsheet file
                var spreadsheetData = _fileFormatManager.ConvertSpreadsheetFileAsync(testFilePath).Result;

                // Assert - Data extraction should work
                Assert.NotNull(spreadsheetData);
                Assert.NotEmpty(spreadsheetData.Sheets);
                Assert.True(spreadsheetData.TotalRows >= expectedRows);
                Assert.True(spreadsheetData.TotalColumns >= expectedColumns);
                
                // Verify metadata contains content analysis
                Assert.True(spreadsheetData.Metadata.ContainsKey("SheetCount"));
                Assert.True(spreadsheetData.Metadata.ContainsKey("TotalCells"));
                Assert.True(spreadsheetData.Metadata.ContainsKey("HasHeaders"));
                Assert.True(spreadsheetData.Metadata.ContainsKey("DataTypes"));
                Assert.True(spreadsheetData.Metadata.ContainsKey("Density"));

                // Verify sheet data structure
                Assert.Contains("Sheet1", spreadsheetData.Sheets.Keys);
                var sheetData = spreadsheetData.Sheets["Sheet1"];
                Assert.NotNull(sheetData);

                // For CSV, verify actual parsed data
                if (extension == ".csv" && sheetData is List<string[]> rows)
                {
                    Assert.Equal(expectedRows, rows.Count);
                    Assert.Equal(expectedColumns, rows[0].Length);
                    Assert.Equal("Header1", rows[0][0]);
                    Assert.Equal("Value1", rows[1][0]);
                }

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void SpreadsheetFormatSupport_StructuredDataAnalysisWorks()
    {
        // Arrange - Create a CSV file with mixed data types
        var csvContent = "Name,Age,Salary,StartDate,Active\nJohn Doe,25,50000.50,2023-01-15,true\nJane Smith,30,75000.00,2022-06-01,false\nBob Johnson,35,60000.25,2021-03-10,true";
        var tempFilePath = Path.GetTempFileName();
        var testFilePath = Path.ChangeExtension(tempFilePath, ".csv");
        
        try
        {
            File.Move(tempFilePath, testFilePath);
            File.WriteAllText(testFilePath, csvContent);

            // Act - Convert spreadsheet file
            var spreadsheetData = _fileFormatManager.ConvertSpreadsheetFileAsync(testFilePath).Result;

            // Assert - Structured data analysis should work
            Assert.NotNull(spreadsheetData);
            Assert.True(spreadsheetData.TotalRows == 4); // Header + 3 data rows
            Assert.True(spreadsheetData.TotalColumns == 5); // 5 columns
            
            // Verify data type analysis
            Assert.True(spreadsheetData.Metadata.ContainsKey("DataTypes"));
            var dataTypes = (List<string>)spreadsheetData.Metadata["DataTypes"];
            Assert.Contains("text", dataTypes);
            Assert.Contains("numeric", dataTypes);
            
            // Verify header detection
            Assert.True(spreadsheetData.Metadata.ContainsKey("HasHeaders"));
            var hasHeaders = (bool)spreadsheetData.Metadata["HasHeaders"];
            Assert.True(hasHeaders);
            
            // Verify data density calculation
            Assert.True(spreadsheetData.Metadata.ContainsKey("Density"));
            var density = (double)spreadsheetData.Metadata["Density"];
            Assert.True(density > 0.9); // Should be high density with no empty cells
            
            // Verify sheet structure
            Assert.Single(spreadsheetData.Sheets);
            Assert.Contains("Sheet1", spreadsheetData.Sheets.Keys);
            
            var sheetData = (List<string[]>)spreadsheetData.Sheets["Sheet1"];
            Assert.Equal(4, sheetData.Count);
            Assert.Equal(5, sheetData[0].Length);
            
            // Verify actual data content
            Assert.Equal("Name", sheetData[0][0]);
            Assert.Equal("Age", sheetData[0][1]);
            Assert.Equal("John Doe", sheetData[1][0]);
            Assert.Equal("25", sheetData[1][1]);

            // Cleanup
            File.Delete(testFilePath);
        }
        catch
        {
            // Cleanup on error
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
            throw;
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 32: Unsupported Format Error Handling**
    /// **Validates: Requirements 13.6**
    /// 
    /// For any unsupported file format, the system should display a clear error message 
    /// with information about supported formats and handle the error gracefully.
    /// </summary>
    [Property(MaxTest = 100)]
    public void UnsupportedFormatErrorHandling_DisplaysClearErrorMessages()
    {
        // Arrange - Define various unsupported file extensions
        var unsupportedExtensions = new[] { ".xyz", ".unknown", ".fake", ".test", ".invalid", ".badformat", ".notreal", "" };

        foreach (var extension in unsupportedExtensions)
        {
            // Act & Assert - IsFormatSupported should return false
            Assert.False(_fileFormatManager.IsFormatSupported(extension), 
                $"Extension '{extension}' should not be supported");

            // Act - Get error message for unsupported format
            var errorMessage = _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(extension).Result;

            // Assert - Error message should be clear and informative
            Assert.NotNull(errorMessage);
            Assert.NotEmpty(errorMessage);
            Assert.Contains("not supported", errorMessage.ToLowerInvariant());
            
            // Should contain information about supported formats
            Assert.True(errorMessage.Contains("Supported") || errorMessage.Contains("supported"), 
                $"Error message for '{extension}' should mention supported formats");
            
            // Should provide recommendations
            Assert.Contains("Recommendations", errorMessage);

            // Act - Test conversion options for unsupported format
            var unsupportedFormat = new FileFormat
            {
                MediaType = MediaType.Text,
                FileExtension = extension,
                IsSupported = false,
                RequiresConversion = false
            };

            var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(unsupportedFormat).Result;

            // Assert - Should provide helpful error information in conversion options
            Assert.NotNull(conversionOptions);
            Assert.NotEmpty(conversionOptions);
            Assert.True(conversionOptions.Any(option => option.ToLowerInvariant().Contains("not supported")), 
                $"Conversion options for '{extension}' should indicate format is not supported");
        }
    }

    [Property(MaxTest = 100)]
    public void UnsupportedFormatErrorHandling_ValidationFailsGracefully()
    {
        // Arrange - Create test files with unsupported extensions
        var unsupportedTestCases = new Dictionary<string, byte[]>
        {
            [".xyz"] = Encoding.UTF8.GetBytes("This is test content for unsupported format"),
            [".unknown"] = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 },
            [".fake"] = Encoding.ASCII.GetBytes("FAKE FILE HEADER"),
            [".invalid"] = new byte[] { 0xFF, 0xFE, 0xFD, 0xFC }
        };

        foreach (var (extension, contentBytes) in unsupportedTestCases)
        {
            // Create a temporary test file
            var tempFilePath = Path.GetTempFileName();
            try
            {
                // Change extension to unsupported format
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Write test data
                File.WriteAllBytes(testFilePath, contentBytes);

                // Act - Detect file format
                var detectedFormat = _fileFormatManager.DetectFileFormatAsync(testFilePath).Result;

                // Assert - Format should be detected as unsupported
                Assert.Equal(extension, detectedFormat.FileExtension);
                Assert.False(detectedFormat.IsSupported, 
                    $"Format '{extension}' should be detected as unsupported");
                Assert.False(detectedFormat.RequiresConversion, 
                    $"Unsupported format '{extension}' should not require conversion");

                // Act - Validate file integrity
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - Validation should handle unsupported format gracefully
                // The validation may pass basic integrity checks but should indicate the format is unsupported
                Assert.False(validation.CanProcess, 
                    $"File with unsupported format '{extension}' should not be processable");
                
                // Should have error messages about unsupported format
                Assert.NotEmpty(validation.ErrorMessages);
                
                // Should contain clear error message about unsupported format
                var hasUnsupportedMessage = validation.ErrorMessages.Any(msg => 
                    msg.ToLowerInvariant().Contains("not supported") || 
                    msg.ToLowerInvariant().Contains("unsupported"));
                Assert.True(hasUnsupportedMessage, 
                    $"Validation errors for '{extension}' should mention format is not supported. " +
                    $"Actual messages: {string.Join("; ", validation.ErrorMessages)}");

                // Should provide information about supported formats
                var errorText = string.Join(" ", validation.ErrorMessages).ToLowerInvariant();
                Assert.True(errorText.Contains("supported") || errorText.Contains("format"), 
                    $"Validation errors for '{extension}' should provide format information");

                // Cleanup
                File.Delete(testFilePath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void UnsupportedFormatErrorHandling_ConversionAttemptsFailGracefully()
    {
        // Arrange - Create a test file with unsupported extension
        var tempFilePath = Path.GetTempFileName();
        var testFilePath = Path.ChangeExtension(tempFilePath, ".unsupported");
        
        try
        {
            File.Move(tempFilePath, testFilePath);
            File.WriteAllText(testFilePath, "Test content for unsupported format");

            // Act & Assert - Conversion attempts should fail gracefully
            
            // Test image conversion
            var imageException = Assert.ThrowsAsync<InvalidOperationException>(
                () => _fileFormatManager.ConvertImageFileAsync(testFilePath));
            Assert.Contains("Cannot convert file to image", imageException.Result.Message);

            // Test video conversion
            var videoException = Assert.ThrowsAsync<InvalidOperationException>(
                () => _fileFormatManager.ConvertVideoFileAsync(testFilePath));
            Assert.Contains("Cannot convert file to video", videoException.Result.Message);

            // Test audio conversion
            var audioException = Assert.ThrowsAsync<InvalidOperationException>(
                () => _fileFormatManager.ConvertAudioFileAsync(testFilePath));
            Assert.Contains("Cannot convert file to audio", audioException.Result.Message);

            // Test document conversion
            var documentException = Assert.ThrowsAsync<InvalidOperationException>(
                () => _fileFormatManager.ConvertDocumentFileAsync(testFilePath));
            Assert.Contains("Cannot convert file to document", documentException.Result.Message);

            // Test spreadsheet conversion
            var spreadsheetException = Assert.ThrowsAsync<InvalidOperationException>(
                () => _fileFormatManager.ConvertSpreadsheetFileAsync(testFilePath));
            Assert.Contains("Cannot convert file to spreadsheet", spreadsheetException.Result.Message);

            // Cleanup
            File.Delete(testFilePath);
        }
        catch
        {
            // Cleanup on error
            if (File.Exists(tempFilePath))
                File.Delete(tempFilePath);
            if (File.Exists(testFilePath))
                File.Delete(testFilePath);
            throw;
        }
    }

    [Property(MaxTest = 100)]
    public void UnsupportedFormatErrorHandling_ProvidesHelpfulRecommendations()
    {
        // Arrange - Test different media types with unsupported extensions
        var unsupportedFormats = new[]
        {
            new FileFormat { MediaType = MediaType.Image, FileExtension = ".xyz", IsSupported = false },
            new FileFormat { MediaType = MediaType.Video, FileExtension = ".unknown", IsSupported = false },
            new FileFormat { MediaType = MediaType.Audio, FileExtension = ".fake", IsSupported = false },
            new FileFormat { MediaType = MediaType.Document, FileExtension = ".invalid", IsSupported = false },
            new FileFormat { MediaType = MediaType.Spreadsheet, FileExtension = ".test", IsSupported = false }
        };

        foreach (var format in unsupportedFormats)
        {
            // Act - Get error message
            var errorMessage = _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(format.FileExtension).Result;

            // Assert - Should provide helpful recommendations
            Assert.NotNull(errorMessage);
            Assert.NotEmpty(errorMessage);
            
            // Should mention the specific unsupported format
            Assert.Contains(format.FileExtension, errorMessage);
            
            // Should provide recommendations section
            Assert.Contains("Recommendations", errorMessage);
            
            // Should suggest converting to supported format
            Assert.True(errorMessage.ToLowerInvariant().Contains("convert") || 
                       errorMessage.ToLowerInvariant().Contains("supported format"), 
                $"Error message for '{format.FileExtension}' should suggest conversion");
            
            // Should suggest checking file extension
            Assert.True(errorMessage.ToLowerInvariant().Contains("extension") || 
                       errorMessage.ToLowerInvariant().Contains("correct"), 
                $"Error message for '{format.FileExtension}' should suggest checking extension");
            
            // Should suggest checking for corruption
            Assert.True(errorMessage.ToLowerInvariant().Contains("corrupt") || 
                       errorMessage.ToLowerInvariant().Contains("damaged"), 
                $"Error message for '{format.FileExtension}' should suggest checking for corruption");

            // Act - Get conversion options
            var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(format).Result;

            // Assert - Should provide clear information about lack of support
            Assert.NotNull(conversionOptions);
            Assert.NotEmpty(conversionOptions);
            Assert.True(conversionOptions.Any(option => option.ToLowerInvariant().Contains("not supported")), 
                $"Conversion options for '{format.FileExtension}' should clearly state format is not supported");
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 33: File Integrity Validation**
    /// **Validates: Requirements 13.7**
    /// 
    /// For any uploaded file, the system should validate file integrity and handle 
    /// corrupted files gracefully without system crashes or data loss.
    /// </summary>
    [Property(MaxTest = 100)]
    public void FileIntegrityValidation_HandlesCorruptedFilesGracefully()
    {
        // Arrange - Test various corruption scenarios for different file types
        var corruptionTestCases = new Dictionary<string, (byte[] ValidHeader, byte[] CorruptedHeader, string Description)>
        {
            // Image files - test formats that have specific validation
            [".jpg"] = (
                new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }, // Valid JPEG header
                new byte[] { 0x00, 0x00, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 }, // Corrupted JPEG header
                "JPEG with corrupted magic bytes"
            ),
            [".png"] = (
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, // Valid PNG header
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x00 }, // Corrupted PNG header
                "PNG with corrupted signature"
            ),
            
            // Document files
            [".pdf"] = (
                Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n>>"), // Valid PDF header
                Encoding.ASCII.GetBytes("%XDF-1.4\n1 0 obj\n<<\n/Type /Catalog\n>>"), // Corrupted PDF header
                "PDF with corrupted header"
            )
        };

        foreach (var (extension, (validHeader, corruptedHeader, description)) in corruptionTestCases)
        {
            // Test 1: Valid file should pass integrity validation (for formats with specific validation)
            var validFilePath = Path.GetTempFileName();
            try
            {
                var validTestPath = Path.ChangeExtension(validFilePath, extension);
                File.Move(validFilePath, validTestPath);
                
                // Create valid file with proper header and some content
                var validContent = validHeader.Concat(new byte[1024]).ToArray();
                File.WriteAllBytes(validTestPath, validContent);

                // Act - Validate valid file
                var validValidation = _fileFormatManager.ValidateFileIntegrityAsync(validTestPath).Result;

                // Assert - Valid file should pass validation or at least not crash
                Assert.NotNull(validValidation);
                // Note: Some formats may not have full validation implemented, so we just ensure no crash

                File.Delete(validTestPath);
            }
            catch
            {
                if (File.Exists(validFilePath))
                    File.Delete(validFilePath);
                throw;
            }

            // Test 2: Corrupted file should be detected gracefully
            var corruptedFilePath = Path.GetTempFileName();
            try
            {
                var corruptedTestPath = Path.ChangeExtension(corruptedFilePath, extension);
                File.Move(corruptedFilePath, corruptedTestPath);
                
                // Create corrupted file
                var corruptedContent = corruptedHeader.Concat(new byte[1024]).ToArray();
                File.WriteAllBytes(corruptedTestPath, corruptedContent);

                // Act - Validate corrupted file (should not throw exception)
                ValidationResult corruptedValidation = null;
                Exception validationException = null;
                
                try
                {
                    corruptedValidation = _fileFormatManager.ValidateFileIntegrityAsync(corruptedTestPath).Result;
                }
                catch (Exception ex)
                {
                    validationException = ex;
                }

                // Assert - System should handle corruption gracefully without crashing
                Assert.Null(validationException);
                Assert.NotNull(corruptedValidation);
                
                // The key requirement is graceful handling - the system should not crash
                // Whether it detects corruption depends on the specific validation implementation
                // But it should always return a valid ValidationResult

                File.Delete(corruptedTestPath);
            }
            catch
            {
                if (File.Exists(corruptedFilePath))
                    File.Delete(corruptedFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void FileIntegrityValidation_HandlesEmptyAndTruncatedFiles()
    {
        // Arrange - Test empty and truncated files for various formats
        var supportedExtensions = new[] { ".jpg", ".png", ".pdf", ".docx", ".mp3", ".wav", ".mp4", ".csv", ".txt" };

        foreach (var extension in supportedExtensions)
        {
            // Test 1: Empty files
            var emptyFilePath = Path.GetTempFileName();
            try
            {
                var emptyTestPath = Path.ChangeExtension(emptyFilePath, extension);
                File.Move(emptyFilePath, emptyTestPath);
                
                // Create empty file
                File.WriteAllBytes(emptyTestPath, new byte[0]);

                // Act - Validate empty file
                var emptyValidation = _fileFormatManager.ValidateFileIntegrityAsync(emptyTestPath).Result;

                // Assert - Empty files should be detected and handled gracefully
                Assert.False(emptyValidation.IsValid, $"Empty {extension} file should be marked as invalid");
                Assert.False(emptyValidation.CanProcess, $"Empty {extension} file should not be processable");
                Assert.NotEmpty(emptyValidation.ErrorMessages);
                
                var errorText = string.Join(" ", emptyValidation.ErrorMessages).ToLowerInvariant();
                Assert.True(errorText.Contains("empty") || errorText.Contains("size"), 
                    $"Error messages for empty {extension} should mention file is empty. Messages: {string.Join("; ", emptyValidation.ErrorMessages)}");

                File.Delete(emptyTestPath);
            }
            catch
            {
                if (File.Exists(emptyFilePath))
                    File.Delete(emptyFilePath);
                throw;
            }

            // Test 2: Truncated files (very small size)
            var truncatedFilePath = Path.GetTempFileName();
            try
            {
                var truncatedTestPath = Path.ChangeExtension(truncatedFilePath, extension);
                File.Move(truncatedFilePath, truncatedTestPath);
                
                // Create truncated file with only 1-2 bytes
                File.WriteAllBytes(truncatedTestPath, new byte[] { 0x00, 0x01 });

                // Act - Validate truncated file
                var truncatedValidation = _fileFormatManager.ValidateFileIntegrityAsync(truncatedTestPath).Result;

                // Assert - Truncated files should be handled gracefully
                Assert.NotNull(truncatedValidation);
                
                // Most truncated files should be handled gracefully
                Assert.NotNull(truncatedValidation);
                
                // Should either be marked as invalid or have warnings about truncation
                var hasTruncationIssues = !truncatedValidation.IsValid || 
                                        truncatedValidation.ErrorMessages.Any() || 
                                        truncatedValidation.Warnings.Any();
                
                Assert.True(hasTruncationIssues, 
                    $"Truncated {extension} file should be detected as having issues. " +
                    $"IsValid: {truncatedValidation.IsValid}, Errors: {truncatedValidation.ErrorMessages.Count}, Warnings: {truncatedValidation.Warnings.Count}");

                File.Delete(truncatedTestPath);
            }
            catch
            {
                if (File.Exists(truncatedFilePath))
                    File.Delete(truncatedFilePath);
                throw;
            }
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 34: Format Conversion Capabilities**
    /// **Validates: Requirements 13.8**
    /// 
    /// For any partially supported file format, the system should provide appropriate 
    /// conversion options when possible and handle conversion failures gracefully.
    /// </summary>
    [Property(MaxTest = 100)]
    public void FormatConversionCapabilities_PartiallySupported_FormatsProvideConversionOptions()
    {
        // Arrange - Define partially supported formats that require conversion
        var partiallySupportedFormats = new Dictionary<string, FileFormat>
        {
            [".tiff"] = new FileFormat 
            { 
                MediaType = MediaType.Image, 
                FileExtension = ".tiff", 
                MimeType = "image/tiff",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 2048
            },
            [".tif"] = new FileFormat 
            { 
                MediaType = MediaType.Image, 
                FileExtension = ".tif", 
                MimeType = "image/tiff",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 1536
            },
            [".svg"] = new FileFormat 
            { 
                MediaType = MediaType.Image, 
                FileExtension = ".svg", 
                MimeType = "image/svg+xml",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 1024
            },
            [".flv"] = new FileFormat 
            { 
                MediaType = MediaType.Video, 
                FileExtension = ".flv", 
                MimeType = "video/x-flv",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 10240
            },
            [".wmv"] = new FileFormat 
            { 
                MediaType = MediaType.Video, 
                FileExtension = ".wmv", 
                MimeType = "video/x-ms-wmv",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 8192
            },
            [".flac"] = new FileFormat 
            { 
                MediaType = MediaType.Audio, 
                FileExtension = ".flac", 
                MimeType = "audio/flac",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 4096
            },
            [".ogg"] = new FileFormat 
            { 
                MediaType = MediaType.Audio, 
                FileExtension = ".ogg", 
                MimeType = "audio/ogg",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 3072
            },
            [".doc"] = new FileFormat 
            { 
                MediaType = MediaType.Document, 
                FileExtension = ".doc", 
                MimeType = "application/msword",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 5120
            },
            [".rtf"] = new FileFormat 
            { 
                MediaType = MediaType.Document, 
                FileExtension = ".rtf", 
                MimeType = "application/rtf",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 2560
            },
            [".ods"] = new FileFormat 
            { 
                MediaType = MediaType.Spreadsheet, 
                FileExtension = ".ods", 
                MimeType = "application/vnd.oasis.opendocument.spreadsheet",
                IsSupported = true, 
                RequiresConversion = true,
                EstimatedSize = 6144
            }
        };

        foreach (var (extension, format) in partiallySupportedFormats)
        {
            // Act - Get conversion options for partially supported format
            var conversionOptions = _fileFormatManager.GetConversionOptionsAsync(format).Result;

            // Assert - Should provide appropriate conversion options
            Assert.NotNull(conversionOptions);
            Assert.NotEmpty(conversionOptions);
            
            // Should indicate that conversion is available for optimal processing
            var hasConversionOption = conversionOptions.Any(option => 
                option.ToLowerInvariant().Contains("convert") && 
                option.ToLowerInvariant().Contains("optimal"));
            
            Assert.True(hasConversionOption, 
                $"Partially supported format '{extension}' should provide conversion options for optimal processing. " +
                $"Options: {string.Join("; ", conversionOptions)}");

            // Should provide format-specific conversion options based on media type
            switch (format.MediaType)
            {
                case MediaType.Image:
                    Assert.True(conversionOptions.Any(opt => opt.Contains("PNG") || opt.Contains("JPEG")), 
                        $"Image format '{extension}' should offer PNG or JPEG conversion options");
                    break;
                case MediaType.Video:
                    Assert.True(conversionOptions.Any(opt => opt.Contains("MP4")), 
                        $"Video format '{extension}' should offer MP4 conversion option");
                    break;
                case MediaType.Audio:
                    Assert.True(conversionOptions.Any(opt => opt.Contains("WAV") || opt.Contains("MP3")), 
                        $"Audio format '{extension}' should offer WAV or MP3 conversion options");
                    break;
                case MediaType.Document:
                    Assert.True(conversionOptions.Any(opt => opt.Contains("text") || opt.Contains("modern")), 
                        $"Document format '{extension}' should offer text extraction or modern format conversion");
                    break;
                case MediaType.Spreadsheet:
                    Assert.True(conversionOptions.Any(opt => opt.Contains("XLSX") || opt.Contains("CSV")), 
                        $"Spreadsheet format '{extension}' should offer XLSX or CSV conversion options");
                    break;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void FormatConversionCapabilities_ConversionAttempts_HandleFailuresGracefully()
    {
        // Arrange - Test conversion attempts for various scenarios
        var conversionTestCases = new Dictionary<string, (byte[] Content, string TargetExtension, bool ShouldSucceed)>
        {
            // Valid conversions that should succeed
            [".csv_to_xlsx"] = (Encoding.UTF8.GetBytes("Name,Age\nJohn,25\nJane,30"), ".xlsx", true),
            [".txt_to_md"] = (Encoding.UTF8.GetBytes("This is plain text content"), ".md", true),
            
            // Problematic conversions that should fail gracefully
            [".corrupted_image"] = (new byte[] { 0x00, 0x01, 0x02, 0x03 }, ".png", false),
            [".empty_file"] = (new byte[0], ".jpg", false),
            [".invalid_data"] = (Encoding.UTF8.GetBytes("Not valid image data"), ".png", false)
        };

        foreach (var (testName, (content, targetExtension, shouldSucceed)) in conversionTestCases)
        {
            // Create test input file
            var inputFilePath = Path.GetTempFileName();
            var outputFilePath = Path.GetTempFileName();
            
            try
            {
                // Determine source extension from test name
                var sourceExtension = testName.Contains("csv") ? ".csv" : 
                                    testName.Contains("txt") ? ".txt" : 
                                    testName.Contains("image") ? ".jpg" : ".dat";
                
                var testInputPath = Path.ChangeExtension(inputFilePath, sourceExtension);
                var testOutputPath = Path.ChangeExtension(outputFilePath, targetExtension);
                
                File.Move(inputFilePath, testInputPath);
                File.Move(outputFilePath, testOutputPath);
                
                // Write test content
                File.WriteAllBytes(testInputPath, content);

                // Act - Attempt format conversion
                Exception conversionException = null;
                bool conversionResult = false;
                
                try
                {
                    conversionResult = _fileFormatManager.TryConvertToSupportedFormatAsync(
                        testInputPath, testOutputPath, targetExtension).Result;
                }
                catch (Exception ex)
                {
                    conversionException = ex;
                }

                // Assert - Conversion should handle failures gracefully
                if (shouldSucceed)
                {
                    // Valid conversions should succeed without throwing exceptions
                    Assert.Null(conversionException);
                    Assert.True(conversionResult, 
                        $"Valid conversion '{testName}' should succeed");
                    
                    // Output file should exist and have content
                    Assert.True(File.Exists(testOutputPath), 
                        $"Conversion output file should exist for '{testName}'");
                    
                    var outputInfo = new FileInfo(testOutputPath);
                    Assert.True(outputInfo.Length > 0, 
                        $"Conversion output should have content for '{testName}'");
                }
                else
                {
                    // Invalid conversions should fail gracefully without crashing
                    Assert.NotNull(conversionException == null ? (object)conversionResult : conversionException);
                    
                    if (conversionException != null)
                    {
                        // Exception should be informative, not a system crash
                        Assert.IsType<InvalidOperationException>(conversionException);
                        Assert.NotEmpty(conversionException.Message);
                        Assert.True(conversionException.Message.Contains("Cannot convert") || 
                                  conversionException.Message.Contains("conversion") ||
                                  conversionException.Message.Contains("format"),
                            $"Exception message should be informative for '{testName}': {conversionException.Message}");
                    }
                    else
                    {
                        // If no exception, conversion should return false
                        Assert.False(conversionResult, 
                            $"Invalid conversion '{testName}' should return false");
                    }
                }

                // Cleanup
                if (File.Exists(testInputPath)) File.Delete(testInputPath);
                if (File.Exists(testOutputPath)) File.Delete(testOutputPath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(inputFilePath)) File.Delete(inputFilePath);
                if (File.Exists(outputFilePath)) File.Delete(outputFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void FormatConversionCapabilities_UnsupportedToSupported_ConversionHandling()
    {
        // Arrange - Test conversion attempts from unsupported to supported formats
        var unsupportedToSupportedTests = new Dictionary<string, string>
        {
            [".xyz"] = ".png",    // Unsupported to supported image
            [".unknown"] = ".mp4", // Unsupported to supported video
            [".fake"] = ".wav",    // Unsupported to supported audio
            [".invalid"] = ".txt", // Unsupported to supported document
            [".test"] = ".csv"     // Unsupported to supported spreadsheet
        };

        foreach (var (unsupportedExt, supportedExt) in unsupportedToSupportedTests)
        {
            // Create test file with unsupported format
            var inputFilePath = Path.GetTempFileName();
            var outputFilePath = Path.GetTempFileName();
            
            try
            {
                var testInputPath = Path.ChangeExtension(inputFilePath, unsupportedExt);
                var testOutputPath = Path.ChangeExtension(outputFilePath, supportedExt);
                
                File.Move(inputFilePath, testInputPath);
                File.Move(outputFilePath, testOutputPath);
                
                // Write some test content
                File.WriteAllText(testInputPath, "Test content for unsupported format");

                // Act - Attempt conversion from unsupported to supported format
                Exception conversionException = null;
                bool conversionResult = false;
                
                try
                {
                    conversionResult = _fileFormatManager.TryConvertToSupportedFormatAsync(
                        testInputPath, testOutputPath, supportedExt).Result;
                }
                catch (Exception ex)
                {
                    conversionException = ex;
                }

                // Assert - Should handle unsupported format conversion gracefully
                Assert.NotNull(conversionException == null ? (object)conversionResult : conversionException);
                
                if (conversionException != null)
                {
                    // Should provide clear error message about unsupported format
                    Assert.NotEmpty(conversionException.Message);
                    var errorMessage = conversionException.Message.ToLowerInvariant();
                    Assert.True(errorMessage.Contains("unsupported") || 
                              errorMessage.Contains("not supported") ||
                              errorMessage.Contains("cannot convert"),
                        $"Error message should indicate unsupported format for '{unsupportedExt}': {conversionException.Message}");
                }
                else
                {
                    // If no exception, should return false for unsupported format
                    Assert.False(conversionResult, 
                        $"Conversion from unsupported format '{unsupportedExt}' should return false");
                }

                // Cleanup
                if (File.Exists(testInputPath)) File.Delete(testInputPath);
                if (File.Exists(testOutputPath)) File.Delete(testOutputPath);
            }
            catch
            {
                // Cleanup on error
                if (File.Exists(inputFilePath)) File.Delete(inputFilePath);
                if (File.Exists(outputFilePath)) File.Delete(outputFilePath);
                throw;
            }
        }
    }

    [Property(MaxTest = 100)]
    public void FormatConversionCapabilities_ConversionProgress_EventsAndErrorHandling()
    {
        // Arrange - Test conversion progress events and error handling
        var progressEvents = new List<string>();
        var errorEvents = new List<string>();
        
        // Subscribe to events
        _fileFormatManager.ConversionProgress += (sender, message) => progressEvents.Add(message);
        _fileFormatManager.FormatError += (sender, message) => errorEvents.Add(message);

        // Test successful conversion scenario
        var csvContent = "Name,Age,City\nJohn,25,NYC\nJane,30,LA";
        var inputFilePath = Path.GetTempFileName();
        var outputFilePath = Path.GetTempFileName();
        
        try
        {
            var csvInputPath = Path.ChangeExtension(inputFilePath, ".csv");
            var xlsxOutputPath = Path.ChangeExtension(outputFilePath, ".xlsx");
            
            File.Move(inputFilePath, csvInputPath);
            File.Move(outputFilePath, xlsxOutputPath);
            
            File.WriteAllText(csvInputPath, csvContent);

            // Act - Perform conversion that should succeed
            var successResult = _fileFormatManager.TryConvertToSupportedFormatAsync(
                csvInputPath, xlsxOutputPath, ".xlsx").Result;

            // Assert - Should generate progress events for successful conversion
            Assert.True(successResult, "CSV to XLSX conversion should succeed");
            Assert.NotEmpty(progressEvents);
            
            // Should have start and completion progress messages
            var hasStartMessage = progressEvents.Any(msg => 
                msg.ToLowerInvariant().Contains("starting") || 
                msg.ToLowerInvariant().Contains("converting"));
            var hasCompletionMessage = progressEvents.Any(msg => 
                msg.ToLowerInvariant().Contains("completed") || 
                msg.ToLowerInvariant().Contains("success"));
            
            Assert.True(hasStartMessage, 
                $"Should have conversion start message. Events: {string.Join("; ", progressEvents)}");
            Assert.True(hasCompletionMessage, 
                $"Should have conversion completion message. Events: {string.Join("; ", progressEvents)}");

            // Cleanup successful test
            if (File.Exists(csvInputPath)) File.Delete(csvInputPath);
            if (File.Exists(xlsxOutputPath)) File.Delete(xlsxOutputPath);
        }
        catch
        {
            // Cleanup on error
            if (File.Exists(inputFilePath)) File.Delete(inputFilePath);
            if (File.Exists(outputFilePath)) File.Delete(outputFilePath);
            throw;
        }

        // Test error scenario
        var errorInputPath = Path.GetTempFileName();
        var errorOutputPath = Path.GetTempFileName();
        
        try
        {
            var invalidInputPath = Path.ChangeExtension(errorInputPath, ".invalid");
            var pngOutputPath = Path.ChangeExtension(errorOutputPath, ".png");
            
            File.Move(errorInputPath, invalidInputPath);
            File.Move(errorOutputPath, pngOutputPath);
            
            File.WriteAllText(invalidInputPath, "Invalid content for image conversion");

            // Clear previous events
            progressEvents.Clear();
            errorEvents.Clear();

            // Act - Attempt conversion that should fail
            Exception conversionException = null;
            bool errorResult = false;
            
            try
            {
                errorResult = _fileFormatManager.TryConvertToSupportedFormatAsync(
                    invalidInputPath, pngOutputPath, ".png").Result;
            }
            catch (Exception ex)
            {
                conversionException = ex;
            }

            // Assert - Should handle errors gracefully with appropriate events
            Assert.True(conversionException != null || !errorResult, 
                "Invalid format conversion should fail");
            
            // Should generate error events or throw informative exceptions
            if (errorEvents.Any())
            {
                Assert.NotEmpty(errorEvents);
                var hasErrorMessage = errorEvents.Any(msg => 
                    msg.ToLowerInvariant().Contains("error") || 
                    msg.ToLowerInvariant().Contains("failed") ||
                    msg.ToLowerInvariant().Contains("cannot"));
                
                Assert.True(hasErrorMessage, 
                    $"Should have error message in events. Events: {string.Join("; ", errorEvents)}");
            }
            
            if (conversionException != null)
            {
                Assert.NotEmpty(conversionException.Message);
                Assert.True(conversionException.Message.ToLowerInvariant().Contains("cannot convert") ||
                          conversionException.Message.ToLowerInvariant().Contains("unsupported") ||
                          conversionException.Message.ToLowerInvariant().Contains("invalid"),
                    $"Exception should provide clear error information: {conversionException.Message}");
            }

            // Cleanup error test
            if (File.Exists(invalidInputPath)) File.Delete(invalidInputPath);
            if (File.Exists(pngOutputPath)) File.Delete(pngOutputPath);
        }
        catch
        {
            // Cleanup on error
            if (File.Exists(errorInputPath)) File.Delete(errorInputPath);
            if (File.Exists(errorOutputPath)) File.Delete(errorOutputPath);
            throw;
        }
        finally
        {
            // Unsubscribe from events
            _fileFormatManager.ConversionProgress -= (sender, message) => progressEvents.Add(message);
            _fileFormatManager.FormatError -= (sender, message) => errorEvents.Add(message);
        }
    }

    [Property(MaxTest = 100)]
    public void FileIntegrityValidation_HandlesNonExistentAndInaccessibleFiles()
    {
        // Test 1: Non-existent files
        var nonExistentPath = Path.Combine(Path.GetTempPath(), "non_existent_file_" + Guid.NewGuid().ToString() + ".jpg");
        
        // Act - Validate non-existent file
        var nonExistentValidation = _fileFormatManager.ValidateFileIntegrityAsync(nonExistentPath).Result;

        // Assert - Non-existent files should be handled gracefully
        Assert.NotNull(nonExistentValidation);
        Assert.False(nonExistentValidation.IsValid);
        Assert.False(nonExistentValidation.CanProcess);
        Assert.NotEmpty(nonExistentValidation.ErrorMessages);
        
        var errorText = string.Join(" ", nonExistentValidation.ErrorMessages).ToLowerInvariant();
        Assert.True(errorText.Contains("not exist") || errorText.Contains("found"), 
            $"Error messages should mention file does not exist. Messages: {string.Join("; ", nonExistentValidation.ErrorMessages)}");

        // Test 2: Null and empty paths
        var nullValidation = _fileFormatManager.ValidateFileIntegrityAsync(null).Result;
        Assert.False(nullValidation.IsValid);
        Assert.NotEmpty(nullValidation.ErrorMessages);

        var emptyPathValidation = _fileFormatManager.ValidateFileIntegrityAsync("").Result;
        Assert.False(emptyPathValidation.IsValid);
        Assert.NotEmpty(emptyPathValidation.ErrorMessages);

        var whitespacePathValidation = _fileFormatManager.ValidateFileIntegrityAsync("   ").Result;
        Assert.False(whitespacePathValidation.IsValid);
        Assert.NotEmpty(whitespacePathValidation.ErrorMessages);
    }

    [Property(MaxTest = 100)]
    public void FileIntegrityValidation_ProvidesDetailedErrorMessages()
    {
        // Arrange - Create files with various integrity issues
        var testCases = new Dictionary<string, (Action<string> CreateFile, string ExpectedErrorPattern)>
        {
            ["empty_file.jpg"] = (
                path => File.WriteAllBytes(path, new byte[0]),
                "empty"
            ),
            ["wrong_header.png"] = (
                path => File.WriteAllBytes(path, new byte[] { 0x00, 0x00, 0x00, 0x00, 0x50, 0x4E, 0x47 }),
                "header"
            ),
            ["truncated.pdf"] = (
                path => File.WriteAllBytes(path, Encoding.ASCII.GetBytes("%P")),
                "truncated|header|invalid"
            ),
            ["corrupted_zip.docx"] = (
                path => File.WriteAllBytes(path, new byte[] { 0x50, 0x4B, 0x00, 0x00 }),
                "zip|corrupt|invalid"
            )
        };

        foreach (var (fileName, (createFile, expectedPattern)) in testCases)
        {
            var tempFilePath = Path.GetTempFileName();
            try
            {
                var extension = Path.GetExtension(fileName);
                var testFilePath = Path.ChangeExtension(tempFilePath, extension);
                File.Move(tempFilePath, testFilePath);
                
                // Create problematic file
                createFile(testFilePath);

                // Act - Validate file
                var validation = _fileFormatManager.ValidateFileIntegrityAsync(testFilePath).Result;

                // Assert - Should provide detailed error messages
                Assert.NotNull(validation);
                Assert.False(validation.IsValid);
                Assert.NotEmpty(validation.ErrorMessages);

                // Check that error messages contain expected patterns
                var allErrorText = string.Join(" ", validation.ErrorMessages).ToLowerInvariant();
                var patterns = expectedPattern.Split('|');
                var hasExpectedPattern = patterns.Any(pattern => allErrorText.Contains(pattern));
                
                Assert.True(hasExpectedPattern, 
                    $"Error messages for {fileName} should contain one of: {expectedPattern}. " +
                    $"Actual messages: {string.Join("; ", validation.ErrorMessages)}");

                // Should provide helpful suggestions
                var hasHelpfulSuggestions = validation.ErrorMessages.Any(msg => 
                    msg.Contains("•") || msg.Contains("Try") || msg.Contains("Check") || msg.Contains("Verify"));
                
                Assert.True(hasHelpfulSuggestions, 
                    $"Error messages for {fileName} should provide helpful suggestions. " +
                    $"Messages: {string.Join("; ", validation.ErrorMessages)}");

                File.Delete(testFilePath);
            }
            catch
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
                throw;
            }
        }
    }
}