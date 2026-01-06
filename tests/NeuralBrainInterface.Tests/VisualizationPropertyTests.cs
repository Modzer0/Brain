using FsCheck;
using FsCheck.Xunit;
using NeuralBrainInterface.Core.Models;
using NeuralBrainInterface.Core.Services;
using NeuralBrainInterface.Core.Interfaces;
using Moq;
using NodaTime;
using Xunit;
using System.Runtime.InteropServices;

namespace NeuralBrainInterface.Tests;

public class VisualizationPropertyTests
{
    /// <summary>
    /// Test-specific visualization engine that forces cross-platform rendering
    /// to avoid System.Drawing.Common dependency issues in tests
    /// </summary>
    private class TestVisualizationEngine : VisualizationEngine
    {
        protected override bool ShouldUseSystemDrawing()
        {
            // Always use cross-platform rendering in tests to avoid platform dependencies
            return false;
        }
    }

    private VisualizationEngine CreateVisualizationEngine()
    {
        return new TestVisualizationEngine();
    }

    private NeuralState CreateTestNeuralState(Dictionary<string, float[]> activationPatterns, 
        Dictionary<string, float[]> attentionWeights)
    {
        return new NeuralState
        {
            ActivationPatterns = activationPatterns,
            AttentionWeights = attentionWeights,
            MemoryContents = new Dictionary<string, object>
            {
                ["test_memory"] = "test_content"
            },
            ProcessingTimestamp = SystemClock.Instance.GetCurrentInstant(),
            ConfidenceScores = new Dictionary<string, float>
            {
                ["confidence"] = 0.85f
            },
            DeviceContext = new Dictionary<DeviceType, bool>
            {
                [DeviceType.Microphone] = true,
                [DeviceType.Speaker] = false,
                [DeviceType.Webcam] = true
            },
            TemporalContext = new TimeInfo
            {
                CurrentDateTime = SystemClock.Instance.GetCurrentInstant(),
                SessionStartTime = SystemClock.Instance.GetCurrentInstant(),
                TimeSinceWake = Duration.FromMinutes(5),
                TimeZone = DateTimeZone.Utc,
                IsDaylightSaving = false
            },
            SleepStatus = new SleepStatus
            {
                IsSleeping = false,
                LastWakeTime = SystemClock.Instance.GetCurrentInstant(),
                AutoSleepEnabled = true,
                StateSaveLocation = "test_location"
            }
        };
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 2: Real-time Display Synchronization**
    /// **Validates: Requirements 2.1, 2.2, 2.4**
    /// 
    /// For any neural state change, the mind display should update immediately to reflect 
    /// the new state, and the displayed state should match the actual neural network state.
    /// </summary>
    [Property(MaxTest = 100)]
    public void RealTimeDisplaySynchronization_StateChangesReflectedInDisplay(
        NonEmptyArray<NonZeroInt> activationValues,
        NonEmptyArray<NonZeroInt> attentionValues)
    {
        // Arrange
        var visualizationEngine = CreateVisualizationEngine();
        
        // Create activation patterns from the generated values
        var activationPatterns = new Dictionary<string, float[]>();
        var nodeNames = new[] { "input_layer", "hidden_layer_1", "hidden_layer_2", "output_layer" };
        
        for (int i = 0; i < Math.Min(nodeNames.Length, activationValues.Get.Length); i++)
        {
            var values = new float[Math.Min(10, activationValues.Get.Length - i)];
            for (int j = 0; j < values.Length; j++)
            {
                values[j] = (float)activationValues.Get[i + j].Get / 100.0f; // Normalize to reasonable range
            }
            activationPatterns[nodeNames[i]] = values;
        }
        
        // Create attention weights from the generated values
        var attentionWeights = new Dictionary<string, float[]>();
        for (int i = 0; i < Math.Min(nodeNames.Length, attentionValues.Get.Length); i++)
        {
            var values = new float[Math.Min(10, attentionValues.Get.Length - i)];
            for (int j = 0; j < values.Length; j++)
            {
                values[j] = Math.Abs((float)attentionValues.Get[i + j].Get / 100.0f); // Normalize to positive range
            }
            attentionWeights[nodeNames[i]] = values;
        }

        var neuralState = CreateTestNeuralState(activationPatterns, attentionWeights);
        
        // Track frame rendering events
        VisualFrame? renderedFrame = null;
        visualizationEngine.FrameRendered += (sender, frame) => renderedFrame = frame;

        // Act - Render the neural state
        var visualFrame = visualizationEngine.RenderNeuralStateAsync(neuralState).Result;

        // Assert - Frame should be rendered successfully
        Assert.NotNull(visualFrame);
        Assert.True(visualFrame.Width > 0);
        Assert.True(visualFrame.Height > 0);
        Assert.True(visualFrame.FrameData.Length > 0);
        
        // Assert - Frame should reflect the current state timestamp
        var timeDifference = Math.Abs((visualFrame.Timestamp - neuralState.ProcessingTimestamp).TotalSeconds);
        Assert.True(timeDifference < 1.0, "Visual frame timestamp should be close to neural state timestamp");
        
        // Assert - Frame should contain the correct visualization mode
        Assert.True(Enum.IsDefined(typeof(VisualizationMode), visualFrame.Mode));
        
        // Assert - Frame rendered event should have been triggered
        Assert.NotNull(renderedFrame);
        Assert.Equal(visualFrame.Timestamp, renderedFrame.Timestamp);
        Assert.Equal(visualFrame.Mode, renderedFrame.Mode);
        
        // Act - Update display with the frame
        var updateTask = visualizationEngine.UpdateDisplayAsync(visualFrame);
        updateTask.Wait();
        
        // Assert - Update should complete successfully
        Assert.True(updateTask.IsCompletedSuccessfully);
        
        // Assert - Last frame should match the rendered frame
        var lastFrame = visualizationEngine.GetLastFrame();
        Assert.NotNull(lastFrame);
        Assert.Equal(visualFrame.Timestamp, lastFrame.Timestamp);
        Assert.Equal(visualFrame.Mode, lastFrame.Mode);
        Assert.Equal(visualFrame.Width, lastFrame.Width);
        Assert.Equal(visualFrame.Height, lastFrame.Height);
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 2: Real-time Display Synchronization - Mode Switching**
    /// **Validates: Requirements 2.1, 2.2, 2.4**
    /// 
    /// For any visualization mode change, the display should immediately switch to the new mode
    /// and subsequent renders should use the new mode.
    /// </summary>
    [Property(MaxTest = 100)]
    public void RealTimeDisplaySynchronization_ModeChangesReflectedImmediately(
        NonEmptyArray<NonZeroInt> activationValues)
    {
        // Arrange
        var visualizationEngine = CreateVisualizationEngine();
        var supportedModes = visualizationEngine.GetSupportedModes();
        
        // Skip if no modes are supported
        if (supportedModes.Count == 0) return;
        
        // Create a simple neural state
        var activationPatterns = new Dictionary<string, float[]>
        {
            ["test_layer"] = activationValues.Get.Take(5).Select(v => (float)v.Get / 100.0f).ToArray()
        };
        var attentionWeights = new Dictionary<string, float[]>
        {
            ["test_layer"] = activationValues.Get.Take(5).Select(v => Math.Abs((float)v.Get / 100.0f)).ToArray()
        };
        
        var neuralState = CreateTestNeuralState(activationPatterns, attentionWeights);
        
        // Track mode change events
        VisualizationMode? changedMode = null;
        visualizationEngine.ModeChanged += (sender, mode) => changedMode = mode;

        // Act & Assert - Test each supported mode
        var isFirstMode = true;
        foreach (var mode in supportedModes)
        {
            // Reset the changed mode tracker
            changedMode = null;
            
            // Set the visualization mode
            visualizationEngine.SetVisualizationMode(mode);
            
            // Assert - Mode change event should be triggered only if this isn't the first mode
            // (since the engine starts with NetworkTopology by default)
            if (!isFirstMode || mode != VisualizationMode.NetworkTopology)
            {
                Assert.Equal(mode, changedMode);
            }
            isFirstMode = false;
            
            // Render with the new mode
            var visualFrame = visualizationEngine.RenderNeuralStateAsync(neuralState).Result;
            
            // Assert - Frame should use the new mode
            Assert.Equal(mode, visualFrame.Mode);
            
            // Assert - Frame should contain mode information in rendering parameters
            Assert.True(visualFrame.RenderingParameters.ContainsKey("Mode"));
            Assert.Equal(mode.ToString(), visualFrame.RenderingParameters["Mode"]);
        }
    }

    /// <summary>
    /// **Feature: neural-brain-interface, Property 2: Real-time Display Synchronization - Concurrent Access**
    /// **Validates: Requirements 2.1, 2.2, 2.4**
    /// 
    /// For any concurrent rendering operations, the visualization engine should handle them
    /// safely and produce consistent results.
    /// </summary>
    [Property(MaxTest = 50)]
    public void RealTimeDisplaySynchronization_ConcurrentRenderingIsSafe(
        NonEmptyArray<NonZeroInt> activationValues1,
        NonEmptyArray<NonZeroInt> activationValues2)
    {
        // Arrange
        var visualizationEngine = CreateVisualizationEngine();
        
        var activationPatterns1 = new Dictionary<string, float[]>
        {
            ["layer1"] = activationValues1.Get.Take(5).Select(v => (float)v.Get / 100.0f).ToArray()
        };
        var activationPatterns2 = new Dictionary<string, float[]>
        {
            ["layer2"] = activationValues2.Get.Take(5).Select(v => (float)v.Get / 100.0f).ToArray()
        };
        
        var neuralState1 = CreateTestNeuralState(activationPatterns1, new Dictionary<string, float[]>());
        var neuralState2 = CreateTestNeuralState(activationPatterns2, new Dictionary<string, float[]>());
        
        // Act - Render concurrently
        var task1 = visualizationEngine.RenderNeuralStateAsync(neuralState1);
        var task2 = visualizationEngine.RenderNeuralStateAsync(neuralState2);
        
        Task.WaitAll(task1, task2);
        
        var frame1 = task1.Result;
        var frame2 = task2.Result;
        
        // Assert - Both renders should complete successfully
        Assert.NotNull(frame1);
        Assert.NotNull(frame2);
        Assert.True(frame1.Width > 0);
        Assert.True(frame1.Height > 0);
        Assert.True(frame2.Width > 0);
        Assert.True(frame2.Height > 0);
        Assert.True(frame1.FrameData.Length > 0);
        Assert.True(frame2.FrameData.Length > 0);
        
        // Assert - Frames should have valid timestamps
        Assert.True(frame1.Timestamp <= SystemClock.Instance.GetCurrentInstant());
        Assert.True(frame2.Timestamp <= SystemClock.Instance.GetCurrentInstant());
        
        // Assert - Last frame should be one of the rendered frames
        var lastFrame = visualizationEngine.GetLastFrame();
        Assert.NotNull(lastFrame);
        Assert.True(lastFrame.Timestamp == frame1.Timestamp || lastFrame.Timestamp == frame2.Timestamp);
    }
}