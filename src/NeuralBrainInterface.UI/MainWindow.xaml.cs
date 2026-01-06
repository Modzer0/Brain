using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.UI.Services;

namespace NeuralBrainInterface.UI;

public partial class MainWindow : Window
{
    private readonly UIManager _uiManager;

    public MainWindow(UIManager uiManager)
    {
        InitializeComponent();
        _uiManager = uiManager;
        
        // Wire up event handlers
        SendButton.Click += SendButton_Click;
        FileUploadButton.Click += FileUploadButton_Click;
        MicrophoneToggle.Click += MicrophoneToggle_Click;
        SpeakerToggle.Click += SpeakerToggle_Click;
        WebcamToggle.Click += WebcamToggle_Click;
        MicrophoneConfig.Click += MicrophoneConfig_Click;
        SpeakerConfig.Click += SpeakerConfig_Click;
        WebcamConfig.Click += WebcamConfig_Click;
        DevicePermissionsButton.Click += DevicePermissionsButton_Click;
        RefreshDevicesButton.Click += RefreshDevicesButton_Click;
        SleepButton.Click += SleepButton_Click;
        WakeButton.Click += WakeButton_Click;
        
        // Handle window size changes for responsive layout
        SizeChanged += MainWindow_SizeChanged;
        
        // Add placeholder text behavior
        TextInput.GotFocus += TextInput_GotFocus;
        TextInput.LostFocus += TextInput_LostFocus;
        TextInput.Text = "Type your message here...";
        TextInput.Foreground = System.Windows.Media.Brushes.Gray;
        
        // Initialize UI manager
        Loaded += async (s, e) => await _uiManager.InitializeWindowAsync();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e)
    {
        var text = TextInput.Text.Trim();
        if (!string.IsNullOrEmpty(text) && text != "Type your message here...")
        {
            // Disable input while processing
            TextInput.IsEnabled = false;
            SendButton.IsEnabled = false;
            
            try
            {
                await _uiManager.HandleTextInputAsync(text);
                TextInput.Clear();
                TextInput.Text = "Type your message here...";
                TextInput.Foreground = System.Windows.Media.Brushes.Gray;
            }
            finally
            {
                // Re-enable input
                TextInput.IsEnabled = true;
                SendButton.IsEnabled = true;
                TextInput.Focus();
            }
        }
    }

    private void TextInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(TextInput.Text) && TextInput.Text != "Type your message here...")
        {
            SendButton_Click(sender, new RoutedEventArgs());
        }
    }

    private void TextInput_GotFocus(object sender, RoutedEventArgs e)
    {
        if (TextInput.Text == "Type your message here...")
        {
            TextInput.Text = "";
            TextInput.Foreground = System.Windows.Media.Brushes.Black;
        }
    }

    private void TextInput_LostFocus(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(TextInput.Text))
        {
            TextInput.Text = "Type your message here...";
            TextInput.Foreground = System.Windows.Media.Brushes.Gray;
        }
    }

    private async void FileUploadButton_Click(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select file to upload",
            Filter = "All Supported Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.webp;*.svg;" +
                    "*.mp4;*.avi;*.mov;*.wmv;*.flv;*.webm;*.mkv;" +
                    "*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a;" +
                    "*.txt;*.rtf;*.pdf;*.doc;*.docx;*.md;" +
                    "*.xls;*.xlsx;*.csv;*.ods|" +
                    "Image Files|*.jpg;*.jpeg;*.png;*.gif;*.bmp;*.tiff;*.webp;*.svg|" +
                    "Video Files|*.mp4;*.avi;*.mov;*.wmv;*.flv;*.webm;*.mkv|" +
                    "Audio Files|*.mp3;*.wav;*.flac;*.aac;*.ogg;*.m4a|" +
                    "Document Files|*.txt;*.rtf;*.pdf;*.doc;*.docx;*.md|" +
                    "Spreadsheet Files|*.xls;*.xlsx;*.csv;*.ods|" +
                    "All Files|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            // Disable upload button while processing
            FileUploadButton.IsEnabled = false;
            
            try
            {
                var fileData = await File.ReadAllBytesAsync(openFileDialog.FileName);
                var fileName = Path.GetFileName(openFileDialog.FileName);
                await _uiManager.HandleFileUploadAsync(fileData, fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error reading file: {ex.Message}", "File Error", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                FileUploadButton.IsEnabled = true;
            }
        }
    }

    private async void MicrophoneToggle_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        
        var isCurrentlyOn = button.Content.ToString()?.Contains("ON") == true;
        button.IsEnabled = false;
        
        try
        {
            await _uiManager.ToggleMicrophoneAsync(!isCurrentlyOn);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void SpeakerToggle_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        
        var isCurrentlyOn = button.Content.ToString()?.Contains("ON") == true;
        button.IsEnabled = false;
        
        try
        {
            await _uiManager.ToggleSpeakerAsync(!isCurrentlyOn);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void WebcamToggle_Click(object sender, RoutedEventArgs e)
    {
        var button = sender as Button;
        if (button == null) return;
        
        var isCurrentlyOn = button.Content.ToString()?.Contains("ON") == true;
        button.IsEnabled = false;
        
        try
        {
            await _uiManager.ToggleWebcamAsync(!isCurrentlyOn);
        }
        finally
        {
            button.IsEnabled = true;
        }
    }

    private async void SleepButton_Click(object sender, RoutedEventArgs e)
    {
        SleepButton.IsEnabled = false;
        try
        {
            await _uiManager.ShowSleepMenuAsync();
        }
        finally
        {
            SleepButton.IsEnabled = true;
        }
    }

    private async void WakeButton_Click(object sender, RoutedEventArgs e)
    {
        WakeButton.IsEnabled = false;
        try
        {
            await _uiManager.ShowWakeMenuAsync();
        }
        finally
        {
            WakeButton.IsEnabled = true;
        }
    }

    private async void MicrophoneConfig_Click(object sender, RoutedEventArgs e)
    {
        await _uiManager.ShowDeviceConfigurationDialogAsync(Core.Models.DeviceType.Microphone);
    }

    private async void SpeakerConfig_Click(object sender, RoutedEventArgs e)
    {
        await _uiManager.ShowDeviceConfigurationDialogAsync(Core.Models.DeviceType.Speaker);
    }

    private async void WebcamConfig_Click(object sender, RoutedEventArgs e)
    {
        await _uiManager.ShowDeviceConfigurationDialogAsync(Core.Models.DeviceType.Webcam);
    }

    private async void DevicePermissionsButton_Click(object sender, RoutedEventArgs e)
    {
        DevicePermissionsButton.IsEnabled = false;
        try
        {
            await _uiManager.ShowDevicePermissionsDialogAsync();
        }
        finally
        {
            DevicePermissionsButton.IsEnabled = true;
        }
    }

    private async void RefreshDevicesButton_Click(object sender, RoutedEventArgs e)
    {
        RefreshDevicesButton.IsEnabled = false;
        RefreshDevicesButton.Content = "🔄 Refreshing...";
        
        try
        {
            // Refresh device status through UI manager
            await _uiManager.RefreshDeviceStatusAsync();
        }
        finally
        {
            RefreshDevicesButton.Content = "🔄 Refresh";
            RefreshDevicesButton.IsEnabled = true;
        }
    }

    private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Handle responsive layout changes
        if (e.NewSize.Width < 1000)
        {
            // Switch to compact layout for smaller screens
            if (DeviceControlsPanel != null)
            {
                DeviceControlsPanel.Orientation = Orientation.Vertical;
            }
            
            // Show overlay text instead of canvas content on very small screens
            if (e.NewSize.Width < 800 && MindDisplayOverlay != null)
            {
                MindDisplayOverlay.Visibility = Visibility.Visible;
            }
        }
        else
        {
            // Use normal layout for larger screens
            if (DeviceControlsPanel != null)
            {
                DeviceControlsPanel.Orientation = Orientation.Horizontal;
            }
            
            if (MindDisplayOverlay != null)
            {
                MindDisplayOverlay.Visibility = Visibility.Collapsed;
            }
        }
        
        // Update status text with window size info
        if (StatusText != null)
        {
            StatusText.Text = $"Neural Brain Interface - Ready ({e.NewSize.Width:F0}x{e.NewSize.Height:F0})";
        }
    }
}