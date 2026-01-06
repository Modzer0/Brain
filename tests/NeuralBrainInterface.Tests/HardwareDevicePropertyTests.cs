using FsCheck;
using FsCheck.Xunit;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using NeuralBrainInterface.Core.Interfaces;
using Moq;
using NodaTime;
using Xunit;
using SystemRandom = System.Random;

namespace NeuralBrainInterface.Tests;

public class HardwareDevicePropertyTests
{
    private (HardwareController controller, Mock<IAudioInputManager> audioIn, Mock<IAudioOutputManager> audioOut, Mock<IVideoInputManager> videoIn, Mock<INeuralCore> neuralCore) CreateHardwareController()
    {
        var mockAudioInputManager = new Mock<IAudioInputManager>();
        var mockAudioOutputManager = new Mock<IAudioOutputManager>();
        var mockVideoInputManager = new Mock<IVideoInputManager>();
        var mockNeuralCore = new Mock<INeuralCore>();

        // Setup basic mock behaviors
        mockAudioInputManager.Setup(m => m.EnableMicrophoneAsync()).ReturnsAsync(true);
        mockAudioInputManager.Setup(m => m.DisableMicrophoneAsync()).Returns(Task.CompletedTask);
        mockAudioInputManager.SetupGet(m => m.IsEnabled).Returns(false);

        mockAudioOutputManager.Setup(m => m.EnableSpeakerAsync()).ReturnsAsync(true);
        mockAudioOutputManager.Setup(m => m.DisableSpeakerAsync()).Returns(Task.CompletedTask);
        mockAudioOutputManager.SetupGet(m => m.IsEnabled).Returns(false);

        mockVideoInputManager.Setup(m => m.EnableWebcamAsync()).ReturnsAsync(true);
        mockVideoInputManager.Setup(m => m.DisableWebcamAsync()).Returns(Task.CompletedTask);
        mockVideoInputManager.SetupGet(m => m.IsEnabled).Returns(false);

        mockNeuralCore.Setup(n => n.UpdateDeviceContext(It.IsAny<Dictionary<DeviceType, bool>>()));

        var controller = new HardwareController(
            mockAudioInputManager.Object,
            mockAudioOutputManager.Object,
            mockVideoInputManager.Object,
            mockNeuralCore.Object,
            Path.Combine(Path.GetTempPath(), $"test_prefs_{Guid.NewGuid()}.json"));

        return (controller, mockAudioInputManager, mockAudioOutputManager, mockVideoInputManager, mockNeuralCore);
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 12: Independent Device Control**
    /// **Validates: Requirements 7.6**
    /// 
    /// For any device toggle operation, changing the state of one device (microphone, speaker, webcam) 
    /// should not affect the state of other devices.
    /// </summary>
    [Property(MaxTest = 20)]
    public void IndependentDeviceControl_TogglingOneDeviceDoesNotAffectOthers()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        // Generate random device type and state
        var deviceTypes = new[] { DeviceType.Microphone, DeviceType.Speaker, DeviceType.Webcam };
        var random = new SystemRandom();
        var targetDevice = deviceTypes[random.Next(deviceTypes.Length)];
        var targetState = random.Next(2) == 1;

        // Get initial states of all devices
        var initialMicStatus = controller.GetDeviceStatus(DeviceType.Microphone);
        var initialSpeakerStatus = controller.GetDeviceStatus(DeviceType.Speaker);
        var initialWebcamStatus = controller.GetDeviceStatus(DeviceType.Webcam);

        // Act - Toggle the target device
        var result = controller.ToggleDeviceAsync(targetDevice, targetState).Result;

        // Assert - The toggle should succeed
        Assert.True(result);

        // Get final states of all devices
        var finalMicStatus = controller.GetDeviceStatus(DeviceType.Microphone);
        var finalSpeakerStatus = controller.GetDeviceStatus(DeviceType.Speaker);
        var finalWebcamStatus = controller.GetDeviceStatus(DeviceType.Webcam);

        // Verify that only the target device state changed
        switch (targetDevice)
        {
            case DeviceType.Microphone:
                Assert.Equal(targetState, finalMicStatus.IsEnabled);
                Assert.Equal(initialSpeakerStatus.IsEnabled, finalSpeakerStatus.IsEnabled);
                Assert.Equal(initialWebcamStatus.IsEnabled, finalWebcamStatus.IsEnabled);
                break;
            case DeviceType.Speaker:
                Assert.Equal(initialMicStatus.IsEnabled, finalMicStatus.IsEnabled);
                Assert.Equal(targetState, finalSpeakerStatus.IsEnabled);
                Assert.Equal(initialWebcamStatus.IsEnabled, finalWebcamStatus.IsEnabled);
                break;
            case DeviceType.Webcam:
                Assert.Equal(initialMicStatus.IsEnabled, finalMicStatus.IsEnabled);
                Assert.Equal(initialSpeakerStatus.IsEnabled, finalSpeakerStatus.IsEnabled);
                Assert.Equal(targetState, finalWebcamStatus.IsEnabled);
                break;
        }
    }

    [Property(MaxTest = 20)]
    public void IndependentDeviceControl_MultipleTogglesPreserveIndependence()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        // Generate random sequence of device toggles
        var deviceTypes = new[] { DeviceType.Microphone, DeviceType.Speaker, DeviceType.Webcam };
        var random = new SystemRandom();
        var toggleSequence = Enumerable.Range(0, 10)
            .Select(_ => (Device: deviceTypes[random.Next(deviceTypes.Length)], State: random.Next(2) == 1))
            .ToList();

        // Track expected states
        var expectedStates = new Dictionary<DeviceType, bool>
        {
            [DeviceType.Microphone] = false,
            [DeviceType.Speaker] = false,
            [DeviceType.Webcam] = false
        };

        // Act - Execute the toggle sequence
        foreach (var (device, state) in toggleSequence)
        {
            var result = controller.ToggleDeviceAsync(device, state).Result;
            Assert.True(result);
            expectedStates[device] = state;
        }

        // Assert - Final states should match expected states
        var allStatus = controller.GetAllDeviceStatus();
        foreach (var (deviceType, expectedState) in expectedStates)
        {
            Assert.Equal(expectedState, allStatus[deviceType].IsEnabled);
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 11: Audio Output Conversion**
    /// **Validates: Requirements 7.5**
    /// 
    /// For any text response when speaker output is enabled, the system should convert 
    /// the text to speech and play it through the speakers.
    /// </summary>
    [Property(MaxTest = 20)]
    public void AudioOutputConversion_TextIsConvertedToSpeech(NonEmptyString textInput)
    {
        // Arrange
        var audioOutputManager = new AudioOutputManager();
        var text = textInput.Get;

        // Act - Convert text to speech
        var audioData = audioOutputManager.ConvertTextToSpeechAsync(text).Result;

        // Assert
        Assert.NotNull(audioData);
        Assert.True(audioData.Data.Length > 0, "Audio data should not be empty");
        Assert.True(audioData.SampleRate > 0, "Sample rate should be positive");
        Assert.True(audioData.Channels > 0, "Channels should be positive");
        Assert.True(audioData.BitDepth > 0, "Bit depth should be positive");
        Assert.Equal("PCM", audioData.Format);
        
        // Verify metadata contains source text
        Assert.True(audioData.Metadata.ContainsKey("SourceText"));
        Assert.Equal(text, audioData.Metadata["SourceText"]);
    }

    [Property(MaxTest = 20)]
    public void AudioOutputConversion_DeterministicForSameInput(NonEmptyString textInput)
    {
        // Arrange
        var audioOutputManager1 = new AudioOutputManager();
        var audioOutputManager2 = new AudioOutputManager();
        var text = textInput.Get;

        // Act - Convert same text twice
        var audioData1 = audioOutputManager1.ConvertTextToSpeechAsync(text).Result;
        var audioData2 = audioOutputManager2.ConvertTextToSpeechAsync(text).Result;

        // Assert - Results should be identical for same input
        Assert.Equal(audioData1.Data.Length, audioData2.Data.Length);
        Assert.Equal(audioData1.SampleRate, audioData2.SampleRate);
        Assert.Equal(audioData1.Channels, audioData2.Channels);
        Assert.Equal(audioData1.BitDepth, audioData2.BitDepth);
        
        // Data should be identical (deterministic)
        Assert.True(audioData1.Data.SequenceEqual(audioData2.Data));
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 14: Device State Management**
    /// **Validates: Requirements 8.5, 8.6, 9.2**
    /// 
    /// For any device enable/disable operation, the hardware controller should immediately 
    /// start or stop the device operation and accurately reflect the device status.
    /// </summary>
    [Property(MaxTest = 20)]
    public void DeviceStateManagement_StatusReflectsActualState()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        // Generate random device operations
        var deviceTypes = new[] { DeviceType.Microphone, DeviceType.Speaker, DeviceType.Webcam };
        var random = new SystemRandom();

        foreach (var deviceType in deviceTypes)
        {
            var targetState = random.Next(2) == 1;

            // Act - Toggle device
            var result = controller.ToggleDeviceAsync(deviceType, targetState).Result;

            // Assert - Status should reflect the operation
            Assert.True(result);
            var status = controller.GetDeviceStatus(deviceType);
            Assert.Equal(targetState, status.IsEnabled);
            Assert.Equal(deviceType, status.DeviceType);
        }
    }

    [Property(MaxTest = 20)]
    public void DeviceStateManagement_DisableStopsOperation()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();

        // Enable all devices first
        controller.ToggleDeviceAsync(DeviceType.Microphone, true).Wait();
        controller.ToggleDeviceAsync(DeviceType.Speaker, true).Wait();
        controller.ToggleDeviceAsync(DeviceType.Webcam, true).Wait();

        // Act - Disable all devices
        controller.ToggleDeviceAsync(DeviceType.Microphone, false).Wait();
        controller.ToggleDeviceAsync(DeviceType.Speaker, false).Wait();
        controller.ToggleDeviceAsync(DeviceType.Webcam, false).Wait();

        // Assert - All devices should be disabled
        var allStatus = controller.GetAllDeviceStatus();
        Assert.False(allStatus[DeviceType.Microphone].IsEnabled);
        Assert.False(allStatus[DeviceType.Speaker].IsEnabled);
        Assert.False(allStatus[DeviceType.Webcam].IsEnabled);

        // Verify disable methods were called
        audioIn.Verify(m => m.DisableMicrophoneAsync(), Times.Once);
        audioOut.Verify(m => m.DisableSpeakerAsync(), Times.Once);
        videoIn.Verify(m => m.DisableWebcamAsync(), Times.Once);
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 15: Device Status Accuracy**
    /// **Validates: Requirements 9.3**
    /// 
    /// For any device state query, the hardware controller should return accurate 
    /// status information that matches the actual device state.
    /// </summary>
    [Property(MaxTest = 20)]
    public void DeviceStatusAccuracy_QueryReturnsAccurateStatus()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        // Generate random device states
        var random = new SystemRandom();
        var micState = random.Next(2) == 1;
        var speakerState = random.Next(2) == 1;
        var webcamState = random.Next(2) == 1;

        // Act - Set device states
        controller.ToggleDeviceAsync(DeviceType.Microphone, micState).Wait();
        controller.ToggleDeviceAsync(DeviceType.Speaker, speakerState).Wait();
        controller.ToggleDeviceAsync(DeviceType.Webcam, webcamState).Wait();

        // Assert - Query should return accurate status
        var micStatus = controller.GetDeviceStatus(DeviceType.Microphone);
        var speakerStatus = controller.GetDeviceStatus(DeviceType.Speaker);
        var webcamStatus = controller.GetDeviceStatus(DeviceType.Webcam);

        Assert.Equal(micState, micStatus.IsEnabled);
        Assert.Equal(speakerState, speakerStatus.IsEnabled);
        Assert.Equal(webcamState, webcamStatus.IsEnabled);

        // Verify GetAllDeviceStatus returns same information
        var allStatus = controller.GetAllDeviceStatus();
        Assert.Equal(micState, allStatus[DeviceType.Microphone].IsEnabled);
        Assert.Equal(speakerState, allStatus[DeviceType.Speaker].IsEnabled);
        Assert.Equal(webcamState, allStatus[DeviceType.Webcam].IsEnabled);
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 18: Device State Notification**
    /// **Validates: Requirements 9.6, 12.4**
    /// 
    /// For any device state change, the hardware controller should notify the neural network 
    /// of the current device availability, and the neural network should update its context accordingly.
    /// </summary>
    [Property(MaxTest = 20)]
    public void DeviceStateNotification_NeuralNetworkIsNotified()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        Dictionary<DeviceType, bool>? capturedDeviceContext = null;
        neuralCore.Setup(n => n.UpdateDeviceContext(It.IsAny<Dictionary<DeviceType, bool>>()))
            .Callback<Dictionary<DeviceType, bool>>(ctx => capturedDeviceContext = ctx);

        // Generate random device type and state
        var deviceTypes = new[] { DeviceType.Microphone, DeviceType.Speaker, DeviceType.Webcam };
        var random = new SystemRandom();
        var targetDevice = deviceTypes[random.Next(deviceTypes.Length)];
        var targetState = random.Next(2) == 1;

        // Act - Toggle device
        var result = controller.ToggleDeviceAsync(targetDevice, targetState).Result;

        // Assert
        Assert.True(result);
        
        // Verify neural network was notified
        neuralCore.Verify(n => n.UpdateDeviceContext(It.IsAny<Dictionary<DeviceType, bool>>()), Times.AtLeastOnce);
        
        // Verify the captured context contains the correct state
        Assert.NotNull(capturedDeviceContext);
        Assert.True(capturedDeviceContext.ContainsKey(targetDevice));
        Assert.Equal(targetState, capturedDeviceContext[targetDevice]);
    }

    [Property(MaxTest = 20)]
    public void DeviceStateNotification_AllDeviceStatesIncluded()
    {
        // Arrange
        var (controller, audioIn, audioOut, videoIn, neuralCore) = CreateHardwareController();
        
        Dictionary<DeviceType, bool>? capturedDeviceContext = null;
        neuralCore.Setup(n => n.UpdateDeviceContext(It.IsAny<Dictionary<DeviceType, bool>>()))
            .Callback<Dictionary<DeviceType, bool>>(ctx => capturedDeviceContext = ctx);

        // Set up initial states
        controller.ToggleDeviceAsync(DeviceType.Microphone, true).Wait();
        controller.ToggleDeviceAsync(DeviceType.Speaker, false).Wait();
        controller.ToggleDeviceAsync(DeviceType.Webcam, true).Wait();

        // Act - Toggle one more device to trigger notification
        controller.ToggleDeviceAsync(DeviceType.Speaker, true).Wait();

        // Assert - All device states should be included in notification
        Assert.NotNull(capturedDeviceContext);
        Assert.True(capturedDeviceContext.ContainsKey(DeviceType.Microphone));
        Assert.True(capturedDeviceContext.ContainsKey(DeviceType.Speaker));
        Assert.True(capturedDeviceContext.ContainsKey(DeviceType.Webcam));
        
        // Verify correct states
        Assert.True(capturedDeviceContext[DeviceType.Microphone]);
        Assert.True(capturedDeviceContext[DeviceType.Speaker]);
        Assert.True(capturedDeviceContext[DeviceType.Webcam]);
    }
}


public class VideoInputPropertyTests
{
    /// <summary>
    /// **Feature: neural-brain-interface, Property 13: Real-time Video Processing**
    /// **Validates: Requirements 8.2, 8.3**
    /// 
    /// For any video input when webcam is enabled, the system should capture video frames 
    /// and process them through the neural network, updating the cognitive state.
    /// </summary>
    [Property(MaxTest = 20)]
    public void RealtimeVideoProcessing_FramesAreCapturedWhenEnabled()
    {
        // Arrange
        var videoInputManager = new VideoInputManager();
        var framesCaptured = new List<ImageData>();
        
        videoInputManager.FrameCaptured += (sender, frame) =>
        {
            framesCaptured.Add(frame);
        };

        // Act - Enable webcam and wait for frames
        var enableResult = videoInputManager.EnableWebcamAsync().Result;
        Assert.True(enableResult);
        
        // Wait for some frames to be captured
        Thread.Sleep(500); // Wait 500ms for frames
        
        // Disable webcam
        videoInputManager.DisableWebcamAsync().Wait();

        // Assert - Frames should have been captured
        Assert.True(framesCaptured.Count > 0, "At least one frame should be captured");
        
        // Verify frame data is valid
        foreach (var frame in framesCaptured)
        {
            Assert.True(frame.Data.Length > 0, "Frame data should not be empty");
            Assert.True(frame.Width > 0, "Frame width should be positive");
            Assert.True(frame.Height > 0, "Frame height should be positive");
            Assert.NotNull(frame.Format);
        }
    }

    [Property(MaxTest = 20)]
    public void RealtimeVideoProcessing_NoFramesWhenDisabled()
    {
        // Arrange
        var videoInputManager = new VideoInputManager();
        var framesCaptured = new List<ImageData>();
        
        videoInputManager.FrameCaptured += (sender, frame) =>
        {
            framesCaptured.Add(frame);
        };

        // Act - Don't enable webcam, just wait
        Thread.Sleep(200);

        // Assert - No frames should be captured when disabled
        Assert.Empty(framesCaptured);
        Assert.False(videoInputManager.IsEnabled);
    }

    [Property(MaxTest = 20)]
    public void RealtimeVideoProcessing_SettingsAffectCapture()
    {
        // Arrange
        var videoInputManager = new VideoInputManager();
        
        var customSettings = new VideoSettings
        {
            Resolution = (320, 240),
            FrameRate = 15,
            ColorFormat = "RGB24",
            CompressionQuality = 0.5f
        };

        // Act - Configure settings
        videoInputManager.ConfigureVideoSettingsAsync(customSettings).Wait();
        var currentSettings = videoInputManager.GetCurrentSettings();

        // Assert - Settings should be applied
        Assert.Equal(customSettings.Resolution, currentSettings.Resolution);
        Assert.Equal(customSettings.FrameRate, currentSettings.FrameRate);
        Assert.Equal(customSettings.ColorFormat, currentSettings.ColorFormat);
        Assert.Equal(customSettings.CompressionQuality, currentSettings.CompressionQuality);
    }

    [Property(MaxTest = 20)]
    public void RealtimeVideoProcessing_DeviceSelectionWorks()
    {
        // Arrange
        var videoInputManager = new VideoInputManager();

        // Act - Get available cameras
        var cameras = videoInputManager.GetAvailableCamerasAsync().Result;

        // Assert - Should have at least one camera
        Assert.NotEmpty(cameras);

        // Act - Set a camera device
        var firstCamera = cameras.First();
        var setResult = videoInputManager.SetCameraDeviceAsync(firstCamera).Result;

        // Assert
        Assert.True(setResult);
        Assert.Equal(firstCamera, videoInputManager.GetCurrentDeviceName());
    }
}

public class AudioInputPropertyTests
{
    /// <summary>
    /// **Feature: neural-brain-interface, Property 10: Real-time Audio Processing**
    /// **Validates: Requirements 7.2, 7.3**
    /// 
    /// For any audio input when microphone is enabled, the system should capture the audio data 
    /// and process it through the neural network, generating appropriate responses.
    /// </summary>
    [Property(MaxTest = 20)]
    public void RealtimeAudioProcessing_AudioIsCapturedWhenEnabled()
    {
        // Arrange
        var audioInputManager = new AudioInputManager();
        var audioCaptured = new List<AudioData>();
        
        audioInputManager.AudioCaptured += (sender, audio) =>
        {
            audioCaptured.Add(audio);
        };

        // Act - Enable microphone and wait for audio
        var enableResult = audioInputManager.EnableMicrophoneAsync().Result;
        Assert.True(enableResult);
        
        // Wait for some audio to be captured
        Thread.Sleep(500); // Wait 500ms for audio
        
        // Disable microphone
        audioInputManager.DisableMicrophoneAsync().Wait();

        // Assert - Audio should have been captured
        Assert.True(audioCaptured.Count > 0, "At least one audio sample should be captured");
        
        // Verify audio data is valid
        foreach (var audio in audioCaptured)
        {
            Assert.True(audio.Data.Length > 0, "Audio data should not be empty");
            Assert.True(audio.SampleRate > 0, "Sample rate should be positive");
            Assert.True(audio.Channels > 0, "Channels should be positive");
            Assert.True(audio.BitDepth > 0, "Bit depth should be positive");
        }
    }

    [Property(MaxTest = 20)]
    public void RealtimeAudioProcessing_NoAudioWhenDisabled()
    {
        // Arrange
        var audioInputManager = new AudioInputManager();
        var audioCaptured = new List<AudioData>();
        
        audioInputManager.AudioCaptured += (sender, audio) =>
        {
            audioCaptured.Add(audio);
        };

        // Act - Don't enable microphone, just wait
        Thread.Sleep(200);

        // Assert - No audio should be captured when disabled
        Assert.Empty(audioCaptured);
        Assert.False(audioInputManager.IsEnabled);
    }

    [Property(MaxTest = 20)]
    public void RealtimeAudioProcessing_SettingsAffectCapture()
    {
        // Arrange
        var audioInputManager = new AudioInputManager();
        
        var customSettings = new AudioSettings
        {
            SampleRate = 48000,
            BitDepth = 24,
            Channels = 2,
            BufferSize = 2048,
            NoiseReduction = false
        };

        // Act - Configure settings
        audioInputManager.ConfigureAudioSettingsAsync(customSettings).Wait();
        var currentSettings = audioInputManager.GetCurrentSettings();

        // Assert - Settings should be applied
        Assert.Equal(customSettings.SampleRate, currentSettings.SampleRate);
        Assert.Equal(customSettings.BitDepth, currentSettings.BitDepth);
        Assert.Equal(customSettings.Channels, currentSettings.Channels);
        Assert.Equal(customSettings.BufferSize, currentSettings.BufferSize);
        Assert.Equal(customSettings.NoiseReduction, currentSettings.NoiseReduction);
    }

    [Property(MaxTest = 20)]
    public void RealtimeAudioProcessing_DeviceSelectionWorks()
    {
        // Arrange
        var audioInputManager = new AudioInputManager();

        // Act - Get available devices
        var devices = audioInputManager.GetAvailableDevicesAsync().Result;

        // Assert - Should have at least one device
        Assert.NotEmpty(devices);

        // Act - Set a device
        var firstDevice = devices.First();
        var setResult = audioInputManager.SetInputDeviceAsync(firstDevice).Result;

        // Assert
        Assert.True(setResult);
        Assert.Equal(firstDevice, audioInputManager.GetCurrentDeviceName());
    }
}
