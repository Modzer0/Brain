using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using NodaTime;

namespace NeuralBrainInterface.UI.Services;

public class UIManager : IUIManager
{
    private MainWindow? _mainWindow;
    private readonly INeuralCore _neuralCore;
    private readonly IHardwareController _hardwareController;
    private readonly IStateManager _stateManager;
    private readonly ITimeContextManager _timeContextManager;
    private readonly IFileFormatManager _fileFormatManager;
    private readonly List<string> _conversationHistory = new();
    private readonly object _conversationLock = new();

    public event EventHandler<string>? TextInputReceived;
    public event EventHandler<(byte[] Data, string FileName)>? FileUploaded;
    public event EventHandler<DeviceType>? DeviceToggleRequested;
    public event EventHandler? SleepRequested;
    public event EventHandler? WakeRequested;

    public UIManager(
        INeuralCore neuralCore,
        IHardwareController hardwareController,
        IStateManager stateManager,
        ITimeContextManager timeContextManager,
        IFileFormatManager fileFormatManager)
    {
        _neuralCore = neuralCore;
        _hardwareController = hardwareController;
        _stateManager = stateManager;
        _timeContextManager = timeContextManager;
        _fileFormatManager = fileFormatManager;
        
        // Subscribe to hardware controller events
        _hardwareController.DeviceStatusChanged += OnDeviceStatusChanged;
        _hardwareController.DeviceError += OnDeviceError;
    }

    public Task InitializeWindowAsync()
    {
        return Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _mainWindow = Application.Current.MainWindow as MainWindow;
            if (_mainWindow != null)
            {
                SetupWindowLayout();
                UpdateDeviceStatusDisplay();
                UpdateTimeDisplay();
                
                // Start time updates
                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(1)
                };
                timer.Tick += (s, e) => UpdateTimeDisplay();
                timer.Start();
            }
        }).Task;
    }

    public async Task HandleTextInputAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input) || _mainWindow == null)
            return;

        try
        {
            // Add user input to conversation history
            lock (_conversationLock)
            {
                _conversationHistory.Add($"User: {input}");
            }

            // Update conversation display
            await UpdateConversationDisplayAsync();

            // Process through neural core
            var result = await _neuralCore.ProcessTextAsync(input);
            
            if (result.Success && !string.IsNullOrEmpty(result.GeneratedOutput))
            {
                // Add AI response to conversation history
                lock (_conversationLock)
                {
                    _conversationHistory.Add($"AI: {result.GeneratedOutput}");
                }

                // Update conversation display
                await UpdateConversationDisplayAsync();

                // Update mind display with new neural state
                if (result.UpdatedState != null)
                {
                    var visualFrame = await GenerateVisualFrameAsync(result.UpdatedState);
                    await UpdateMindDisplayAsync(visualFrame);
                }
            }

            // Raise event for other components
            TextInputReceived?.Invoke(this, input);
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error processing text input: {ex.Message}");
        }
    }

    public async Task HandleFileUploadAsync(byte[] fileData, string fileName)
    {
        if (fileData.Length == 0 || string.IsNullOrEmpty(fileName))
            return;

        try
        {
            // Write file data to temporary file for processing
            var tempFilePath = Path.Combine(Path.GetTempPath(), fileName);
            await File.WriteAllBytesAsync(tempFilePath, fileData);

            try
            {
                // Validate file format
                var format = await _fileFormatManager.DetectFileFormatAsync(tempFilePath);
                if (!format.IsSupported)
                {
                    var errorMessage = await _fileFormatManager.GetUnsupportedFormatErrorMessageAsync(format.FileExtension);
                    await ShowErrorMessageAsync(errorMessage);
                    return;
                }

                // Add file upload to conversation history
                lock (_conversationLock)
                {
                    _conversationHistory.Add($"User uploaded: {fileName} ({format.MediaType})");
                }
                await UpdateConversationDisplayAsync();

                // Process file based on type
                ProcessingResult? result = null;
                switch (format.MediaType)
                {
                    case MediaType.Image:
                        var imageData = await _fileFormatManager.ConvertImageFileAsync(tempFilePath);
                        result = await _neuralCore.ProcessImageAsync(imageData);
                        break;
                    case MediaType.Video:
                        var videoData = await _fileFormatManager.ConvertVideoFileAsync(tempFilePath);
                        result = await _neuralCore.ProcessVideoAsync(videoData);
                        break;
                    case MediaType.Audio:
                        var audioData = await _fileFormatManager.ConvertAudioFileAsync(tempFilePath);
                        result = await _neuralCore.ProcessAudioAsync(audioData);
                        break;
                    case MediaType.Document:
                        var documentData = await _fileFormatManager.ConvertDocumentFileAsync(tempFilePath);
                        result = await _neuralCore.ProcessDocumentAsync(documentData);
                        break;
                    case MediaType.Spreadsheet:
                        var spreadsheetData = await _fileFormatManager.ConvertSpreadsheetFileAsync(tempFilePath);
                        result = await _neuralCore.ProcessSpreadsheetAsync(spreadsheetData);
                        break;
                }

                if (result?.Success == true)
                {
                    if (!string.IsNullOrEmpty(result.GeneratedOutput))
                    {
                        lock (_conversationLock)
                        {
                            _conversationHistory.Add($"AI: {result.GeneratedOutput}");
                        }
                        await UpdateConversationDisplayAsync();
                    }

                    // Update mind display
                    if (result.UpdatedState != null)
                    {
                        var visualFrame = await GenerateVisualFrameAsync(result.UpdatedState);
                        await UpdateMindDisplayAsync(visualFrame);
                    }
                }

                // Raise event for other components
                FileUploaded?.Invoke(this, (fileData, fileName));
            }
            finally
            {
                // Clean up temporary file
                if (File.Exists(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error processing file upload: {ex.Message}");
        }
    }

    public async Task UpdateMindDisplayAsync(VisualFrame visualFrame)
    {
        if (_mainWindow == null || visualFrame.FrameData.Length == 0)
            return;

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            try
            {
                var canvas = _mainWindow.FindName("MindDisplayCanvas") as Canvas;
                if (canvas == null) return;

                canvas.Children.Clear();

                // Create bitmap from frame data
                var bitmap = CreateBitmapFromFrameData(visualFrame);
                if (bitmap != null)
                {
                    var image = new Image
                    {
                        Source = bitmap,
                        Width = canvas.ActualWidth > 0 ? canvas.ActualWidth : 800,
                        Height = canvas.ActualHeight > 0 ? canvas.ActualHeight : 600,
                        Stretch = Stretch.Uniform
                    };
                    
                    Canvas.SetLeft(image, 0);
                    Canvas.SetTop(image, 0);
                    canvas.Children.Add(image);
                }
                else
                {
                    // Fallback to text representation
                    var textBlock = new TextBlock
                    {
                        Text = $"Neural Activity - Mode: {visualFrame.Mode}\nTimestamp: {visualFrame.Timestamp}",
                        Foreground = Brushes.White,
                        FontSize = 16,
                        TextAlignment = TextAlignment.Center
                    };
                    
                    Canvas.SetLeft(textBlock, 50);
                    Canvas.SetTop(textBlock, 50);
                    canvas.Children.Add(textBlock);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't crash UI
                Console.WriteLine($"Error updating mind display: {ex.Message}");
            }
        });
    }

    public async Task DisplayResponseAsync(string response)
    {
        if (string.IsNullOrEmpty(response))
            return;

        lock (_conversationLock)
        {
            _conversationHistory.Add($"AI: {response}");
        }

        await UpdateConversationDisplayAsync();
    }

    public async Task ToggleMicrophoneAsync(bool enabled)
    {
        try
        {
            var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Microphone, enabled);
            if (success)
            {
                await UpdateDeviceButtonAsync(DeviceType.Microphone, enabled);
                DeviceToggleRequested?.Invoke(this, DeviceType.Microphone);
            }
            else
            {
                await ShowErrorMessageAsync("Failed to toggle microphone. Check permissions and device availability.");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error toggling microphone: {ex.Message}");
        }
    }

    public async Task ToggleSpeakerAsync(bool enabled)
    {
        try
        {
            var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Speaker, enabled);
            if (success)
            {
                await UpdateDeviceButtonAsync(DeviceType.Speaker, enabled);
                DeviceToggleRequested?.Invoke(this, DeviceType.Speaker);
            }
            else
            {
                await ShowErrorMessageAsync("Failed to toggle speaker. Check permissions and device availability.");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error toggling speaker: {ex.Message}");
        }
    }

    public async Task ToggleWebcamAsync(bool enabled)
    {
        try
        {
            var success = await _hardwareController.ToggleDeviceAsync(DeviceType.Webcam, enabled);
            if (success)
            {
                await UpdateDeviceButtonAsync(DeviceType.Webcam, enabled);
                DeviceToggleRequested?.Invoke(this, DeviceType.Webcam);
            }
            else
            {
                await ShowErrorMessageAsync("Failed to toggle webcam. Check permissions and device availability.");
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error toggling webcam: {ex.Message}");
        }
    }

    public async Task UpdateDeviceStatusAsync(DeviceStatus status)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateDeviceButtonStatus(status);
        });
    }

    public async Task ShowSleepMenuAsync()
    {
        try
        {
            var result = MessageBox.Show(
                "Put the neural network to sleep? This will save the current state and reduce resource usage.",
                "Sleep Neural Network",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _stateManager.InitiateSleepAsync();
                SleepRequested?.Invoke(this, EventArgs.Empty);
                
                lock (_conversationLock)
                {
                    _conversationHistory.Add("System: Neural network is now sleeping...");
                }
                await UpdateConversationDisplayAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error initiating sleep: {ex.Message}");
        }
    }

    public async Task ShowWakeMenuAsync()
    {
        try
        {
            var result = MessageBox.Show(
                "Wake up the neural network? This will restore the previous state and resume full processing.",
                "Wake Neural Network",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await _stateManager.InitiateWakeAsync();
                WakeRequested?.Invoke(this, EventArgs.Empty);
                
                lock (_conversationLock)
                {
                    _conversationHistory.Add("System: Neural network is now awake and ready!");
                }
                await UpdateConversationDisplayAsync();
            }
        }
        catch (Exception ex)
        {
            await ShowErrorMessageAsync($"Error initiating wake: {ex.Message}");
        }
    }

    public async Task DisplayTimeContextAsync(TimeInfo timeInfo)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_mainWindow?.Title != null)
            {
                var localTime = timeInfo.CurrentDateTime.InZone(timeInfo.TimeZone).ToDateTimeUnspecified();
                _mainWindow.Title = $"Neural Brain Interface - {localTime:yyyy-MM-dd HH:mm:ss}";
            }
        });
    }

    private void SetupWindowLayout()
    {
        if (_mainWindow == null) return;

        // Ensure responsive layout
        _mainWindow.SizeChanged += (s, e) => AdjustLayoutForSize(e.NewSize);
        
        // Set minimum size
        _mainWindow.MinWidth = 800;
        _mainWindow.MinHeight = 600;
        
        // Initial layout adjustment
        AdjustLayoutForSize(new Size(_mainWindow.Width, _mainWindow.Height));
    }

    private void AdjustLayoutForSize(Size newSize)
    {
        if (_mainWindow == null) return;

        // Adjust mind display canvas size
        var canvas = _mainWindow.FindName("MindDisplayCanvas") as Canvas;
        if (canvas != null)
        {
            // Ensure canvas takes up most of the available space
            var availableHeight = newSize.Height - 150; // Account for controls and text input
            canvas.Height = Math.Max(400, availableHeight);
        }

        // Adjust device controls layout for smaller screens
        var devicePanel = _mainWindow.FindName("DeviceControlsPanel") as StackPanel;
        if (devicePanel != null && newSize.Width < 1000)
        {
            // Stack vertically on smaller screens
            devicePanel.Orientation = Orientation.Vertical;
        }
        else if (devicePanel != null)
        {
            // Horizontal layout on larger screens
            devicePanel.Orientation = Orientation.Horizontal;
        }
    }

    private async Task UpdateConversationDisplayAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_mainWindow == null) return;

            var conversationDisplay = _mainWindow.FindName("ConversationDisplay") as TextBlock;
            var scrollViewer = _mainWindow.FindName("ConversationScrollViewer") as ScrollViewer;
            
            if (conversationDisplay != null)
            {
                lock (_conversationLock)
                {
                    if (_conversationHistory.Count > 0)
                    {
                        // Show last 20 messages to avoid overwhelming the display
                        var recentMessages = _conversationHistory.TakeLast(20);
                        conversationDisplay.Text = string.Join("\n\n", recentMessages);
                    }
                }
                
                // Auto-scroll to bottom
                if (scrollViewer != null)
                {
                    scrollViewer.ScrollToEnd();
                }
            }
        });
    }

    private void UpdateDeviceStatusDisplay()
    {
        if (_mainWindow == null) return;

        // Update all device button states
        var micStatus = _hardwareController.GetDeviceStatus(DeviceType.Microphone);
        var speakerStatus = _hardwareController.GetDeviceStatus(DeviceType.Speaker);
        var webcamStatus = _hardwareController.GetDeviceStatus(DeviceType.Webcam);

        UpdateDeviceButtonStatus(micStatus);
        UpdateDeviceButtonStatus(speakerStatus);
        UpdateDeviceButtonStatus(webcamStatus);
    }

    private void UpdateDeviceButtonStatus(DeviceStatus status)
    {
        if (_mainWindow == null) return;

        Button? button = status.DeviceType switch
        {
            DeviceType.Microphone => _mainWindow.FindName("MicrophoneToggle") as Button,
            DeviceType.Speaker => _mainWindow.FindName("SpeakerToggle") as Button,
            DeviceType.Webcam => _mainWindow.FindName("WebcamToggle") as Button,
            _ => null
        };

        if (button != null)
        {
            var icon = status.DeviceType switch
            {
                DeviceType.Microphone => status.IsEnabled ? "🎤" : "🔇",
                DeviceType.Speaker => status.IsEnabled ? "🔊" : "🔇",
                DeviceType.Webcam => status.IsEnabled ? "📹" : "📷",
                _ => ""
            };

            var statusText = status.IsEnabled ? "ON" : "OFF";
            var deviceName = status.DeviceType.ToString();
            
            button.Content = $"{icon} {deviceName} {statusText}";
            button.Background = status.IsEnabled ? Brushes.LightGreen : Brushes.LightGray;
            button.IsEnabled = status.IsAvailable;

            if (!string.IsNullOrEmpty(status.ErrorMessage))
            {
                button.ToolTip = status.ErrorMessage;
                button.Background = Brushes.LightCoral;
            }
        }
    }

    private async Task UpdateDeviceButtonAsync(DeviceType deviceType, bool enabled)
    {
        var status = _hardwareController.GetDeviceStatus(deviceType);
        await UpdateDeviceStatusAsync(status);
    }

    private void UpdateTimeDisplay()
    {
        if (_mainWindow == null) return;

        try
        {
            var timeInfo = _timeContextManager.GetTimeInfo();
            _ = DisplayTimeContextAsync(timeInfo);
        }
        catch
        {
            // Ignore time update errors
        }
    }

    private async Task<VisualFrame> GenerateVisualFrameAsync(NeuralState state)
    {
        // This is a simplified implementation - in a real system this would
        // interface with the visualization engine
        return new VisualFrame
        {
            FrameData = new byte[0], // Empty for now
            Width = 800,
            Height = 600,
            Mode = VisualizationMode.ActivationPatterns,
            Timestamp = SystemClock.Instance.GetCurrentInstant(),
            RenderingParameters = new Dictionary<string, object>
            {
                ["activation_count"] = state.ActivationPatterns.Count,
                ["attention_count"] = state.AttentionWeights.Count,
                ["confidence_avg"] = state.ConfidenceScores.Values.DefaultIfEmpty(0).Average()
            }
        };
    }

    private BitmapSource? CreateBitmapFromFrameData(VisualFrame frame)
    {
        // This would normally convert the frame data to a bitmap
        // For now, return null to use text fallback
        return null;
    }

    private async Task ShowErrorMessageAsync(string message)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        });
    }

    private async void OnDeviceStatusChanged(object? sender, DeviceStatus status)
    {
        await UpdateDeviceStatusAsync(status);
    }

    private async void OnDeviceError(object? sender, string error)
    {
        await ShowErrorMessageAsync($"Device Error: {error}");
        
        // Update status text to show error
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_mainWindow != null)
            {
                var statusText = _mainWindow.FindName("StatusText") as TextBlock;
                if (statusText != null)
                {
                    statusText.Text = $"Neural Brain Interface - Device Error: {error}";
                    statusText.Foreground = Brushes.Red;
                    
                    // Reset status text after 5 seconds
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(5)
                    };
                    timer.Tick += (s, e) =>
                    {
                        statusText.Text = "Neural Brain Interface - Ready";
                        statusText.Foreground = Brushes.Black;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        });
    }

    public async Task ShowDeviceConfigurationDialogAsync(DeviceType deviceType)
    {
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var configWindow = new DeviceConfigurationWindow(deviceType, _hardwareController);
            configWindow.Owner = _mainWindow;
            configWindow.ShowDialog();
        });
    }

    public async Task ShowDevicePermissionsDialogAsync()
    {
        await Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            var result = MessageBox.Show(
                "The application needs permission to access your microphone and webcam for full functionality. " +
                "Would you like to grant these permissions now?",
                "Device Permissions Required",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                var permissionsGranted = await _hardwareController.RequestDevicePermissionsAsync();
                
                if (permissionsGranted)
                {
                    MessageBox.Show(
                        "All device permissions have been granted successfully!",
                        "Permissions Granted",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
                else
                {
                    MessageBox.Show(
                        "Some device permissions were denied. You can enable devices manually using the toggle buttons, " +
                        "but some features may not work properly.",
                        "Permissions Partially Denied",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                
                // Refresh device status display
                UpdateDeviceStatusDisplay();
            }
        });
    }

    public async Task RefreshDeviceStatusAsync()
    {
        await Task.Run(() =>
        {
            // Synchronize hardware controller with actual device states
            if (_hardwareController is HardwareController hardwareController)
            {
                hardwareController.SynchronizeDeviceStatus();
            }
        });
        
        // Update the UI display
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            UpdateDeviceStatusDisplay();
        });
        
        // Update status text to show refresh completed
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_mainWindow != null)
            {
                var statusText = _mainWindow.FindName("StatusText") as TextBlock;
                if (statusText != null)
                {
                    var originalText = statusText.Text;
                    statusText.Text = "Neural Brain Interface - Device status refreshed";
                    statusText.Foreground = Brushes.Green;
                    
                    // Reset status text after 3 seconds
                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(3)
                    };
                    timer.Tick += (s, e) =>
                    {
                        statusText.Text = "Neural Brain Interface - Ready";
                        statusText.Foreground = Brushes.Black;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        });
    }
}