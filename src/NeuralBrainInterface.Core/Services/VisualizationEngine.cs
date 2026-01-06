using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;
using System.Runtime.InteropServices;

namespace NeuralBrainInterface.Core.Services;

/// <summary>
/// Visualization engine that renders neural network states into visual frames
/// Supports multiple rendering modes and real-time updates (Requirements 2.1, 2.2, 2.4)
/// </summary>
public class VisualizationEngine : IVisualizationEngine
{
    private VisualizationMode _currentMode = VisualizationMode.NetworkTopology;
    private readonly object _renderLock = new();
    private VisualFrame? _lastFrame;
    
    public event EventHandler<VisualFrame>? FrameRendered;
    public event EventHandler<VisualizationMode>? ModeChanged;

    /// <summary>
    /// Renders neural state into a visual frame based on current visualization mode
    /// </summary>
    public async Task<VisualFrame> RenderNeuralStateAsync(NeuralState state)
    {
        return await Task.Run(() =>
        {
            lock (_renderLock)
            {
                var frame = _currentMode switch
                {
                    VisualizationMode.NetworkTopology => RenderNetworkTopology(state),
                    VisualizationMode.ActivationPatterns => RenderActivationPatterns(state),
                    VisualizationMode.AttentionMaps => RenderAttentionMaps(state),
                    VisualizationMode.ProcessingFlow => RenderProcessingFlow(state),
                    _ => RenderNetworkTopology(state)
                };

                _lastFrame = frame;
                FrameRendered?.Invoke(this, frame);
                return frame;
            }
        });
    }

    /// <summary>
    /// Updates the display with the provided visual frame
    /// </summary>
    public async Task UpdateDisplayAsync(VisualFrame frame)
    {
        await Task.Run(() =>
        {
            // In a real implementation, this would update the actual display
            // For now, we simulate the display update
            lock (_renderLock)
            {
                _lastFrame = frame;
            }
        });
    }

    /// <summary>
    /// Sets the current visualization mode and triggers mode change event
    /// </summary>
    public void SetVisualizationMode(VisualizationMode mode)
    {
        if (_currentMode != mode)
        {
            _currentMode = mode;
            ModeChanged?.Invoke(this, mode);
        }
    }

    /// <summary>
    /// Returns all supported visualization modes
    /// </summary>
    public List<VisualizationMode> GetSupportedModes()
    {
        return Enum.GetValues<VisualizationMode>().ToList();
    }

    /// <summary>
    /// Gets the last rendered frame for synchronization testing
    /// </summary>
    public VisualFrame? GetLastFrame()
    {
        lock (_renderLock)
        {
            return _lastFrame;
        }
    }

    /// <summary>
    /// Determines whether to use System.Drawing for rendering (Windows-specific)
    /// Virtual method to allow override in tests for cross-platform compatibility
    /// </summary>
    protected virtual bool ShouldUseSystemDrawing()
    {
        return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    }

    /// <summary>
    /// Renders network topology visualization showing neural network structure
    /// </summary>
    private VisualFrame RenderNetworkTopology(NeuralState state)
    {
        const int width = 800;
        const int height = 600;
        
        // Cross-platform compatible rendering using byte array instead of System.Drawing
        if (ShouldUseSystemDrawing())
        {
            return RenderWithSystemDrawing(state, width, height, VisualizationMode.NetworkTopology);
        }
        else
        {
            return RenderWithCrossPlatformMethod(state, width, height, VisualizationMode.NetworkTopology);
        }
    }

    /// <summary>
    /// Renders activation patterns showing current neural activity levels
    /// </summary>
    private VisualFrame RenderActivationPatterns(NeuralState state)
    {
        const int width = 800;
        const int height = 600;
        
        if (ShouldUseSystemDrawing())
        {
            return RenderWithSystemDrawing(state, width, height, VisualizationMode.ActivationPatterns);
        }
        else
        {
            return RenderWithCrossPlatformMethod(state, width, height, VisualizationMode.ActivationPatterns);
        }
    }

    /// <summary>
    /// Renders attention maps showing where the network is focusing
    /// </summary>
    private VisualFrame RenderAttentionMaps(NeuralState state)
    {
        const int width = 800;
        const int height = 600;
        
        if (ShouldUseSystemDrawing())
        {
            return RenderWithSystemDrawing(state, width, height, VisualizationMode.AttentionMaps);
        }
        else
        {
            return RenderWithCrossPlatformMethod(state, width, height, VisualizationMode.AttentionMaps);
        }
    }

    /// <summary>
    /// Renders processing flow showing data movement through the network
    /// </summary>
    private VisualFrame RenderProcessingFlow(NeuralState state)
    {
        const int width = 800;
        const int height = 600;
        
        if (ShouldUseSystemDrawing())
        {
            return RenderWithSystemDrawing(state, width, height, VisualizationMode.ProcessingFlow);
        }
        else
        {
            return RenderWithCrossPlatformMethod(state, width, height, VisualizationMode.ProcessingFlow);
        }
    }

    /// <summary>
    /// Cross-platform rendering method that doesn't rely on System.Drawing
    /// </summary>
    private VisualFrame RenderWithCrossPlatformMethod(NeuralState state, int width, int height, VisualizationMode mode)
    {
        // Create a simple bitmap representation using byte array
        // This is a simplified implementation for cross-platform compatibility
        var bytesPerPixel = 4; // RGBA
        var frameData = new byte[width * height * bytesPerPixel];
        
        // Generate visualization data based on neural state and mode
        GenerateVisualizationData(state, frameData, width, height, mode);
        
        return new VisualFrame
        {
            FrameData = frameData,
            Width = width,
            Height = height,
            Mode = mode,
            Timestamp = SystemClock.Instance.GetCurrentInstant(),
            RenderingParameters = new Dictionary<string, object>
            {
                ["Mode"] = mode.ToString(),
                ["RenderTime"] = SystemClock.Instance.GetCurrentInstant(),
                ["Platform"] = "CrossPlatform"
            }
        };
    }

    /// <summary>
    /// Windows-specific rendering using System.Drawing
    /// </summary>
    private VisualFrame RenderWithSystemDrawing(NeuralState state, int width, int height, VisualizationMode mode)
    {
        try
        {
            // Only use System.Drawing on Windows
            using var bitmap = new System.Drawing.Bitmap(width, height);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            
            // Clear background based on mode
            var backgroundColor = mode switch
            {
                VisualizationMode.NetworkTopology => System.Drawing.Color.Black,
                VisualizationMode.ActivationPatterns => System.Drawing.Color.DarkBlue,
                VisualizationMode.AttentionMaps => System.Drawing.Color.DarkGreen,
                VisualizationMode.ProcessingFlow => System.Drawing.Color.DarkRed,
                _ => System.Drawing.Color.Black
            };
            graphics.Clear(backgroundColor);
            
            // Render based on mode
            switch (mode)
            {
                case VisualizationMode.NetworkTopology:
                    RenderNetworkTopologyWithDrawing(graphics, state, width, height);
                    break;
                case VisualizationMode.ActivationPatterns:
                    RenderActivationPatternsWithDrawing(graphics, state, width, height);
                    break;
                case VisualizationMode.AttentionMaps:
                    RenderAttentionMapsWithDrawing(graphics, state, width, height);
                    break;
                case VisualizationMode.ProcessingFlow:
                    RenderProcessingFlowWithDrawing(graphics, state, width, height);
                    break;
            }
            
            using var stream = new MemoryStream();
            bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
            
            return new VisualFrame
            {
                FrameData = stream.ToArray(),
                Width = bitmap.Width,
                Height = bitmap.Height,
                Mode = mode,
                Timestamp = SystemClock.Instance.GetCurrentInstant(),
                RenderingParameters = new Dictionary<string, object>
                {
                    ["Mode"] = mode.ToString(),
                    ["RenderTime"] = SystemClock.Instance.GetCurrentInstant(),
                    ["Platform"] = "Windows"
                }
            };
        }
        catch (PlatformNotSupportedException)
        {
            // Fallback to cross-platform method if System.Drawing fails
            return RenderWithCrossPlatformMethod(state, width, height, mode);
        }
    }

    /// <summary>
    /// Generates visualization data for cross-platform rendering
    /// </summary>
    private void GenerateVisualizationData(NeuralState state, byte[] frameData, int width, int height, VisualizationMode mode)
    {
        var random = new Random(state.ProcessingTimestamp.GetHashCode());
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var index = (y * width + x) * 4;
                
                // Generate colors based on mode and neural state
                var (r, g, b, a) = mode switch
                {
                    VisualizationMode.NetworkTopology => GenerateNetworkTopologyColor(state, x, y, width, height, random),
                    VisualizationMode.ActivationPatterns => GenerateActivationPatternColor(state, x, y, width, height, random),
                    VisualizationMode.AttentionMaps => GenerateAttentionMapColor(state, x, y, width, height, random),
                    VisualizationMode.ProcessingFlow => GenerateProcessingFlowColor(state, x, y, width, height, random),
                    _ => (0, 0, 0, 255)
                };
                
                frameData[index] = (byte)r;     // Red
                frameData[index + 1] = (byte)g; // Green
                frameData[index + 2] = (byte)b; // Blue
                frameData[index + 3] = (byte)a; // Alpha
            }
        }
    }

    private (byte r, byte g, byte b, byte a) GenerateNetworkTopologyColor(NeuralState state, int x, int y, int width, int height, Random random)
    {
        // Generate network topology visualization
        var centerX = width / 2;
        var centerY = height / 2;
        var distance = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
        var maxDistance = Math.Sqrt(centerX * centerX + centerY * centerY);
        
        var intensity = (byte)(255 * (1 - distance / maxDistance));
        return (intensity, intensity, 255, 255);
    }

    private (byte r, byte g, byte b, byte a) GenerateActivationPatternColor(NeuralState state, int x, int y, int width, int height, Random random)
    {
        // Generate activation pattern heatmap
        var cellX = x / (width / 20);
        var cellY = y / (height / 15);
        var activation = state.ActivationPatterns.Values.FirstOrDefault()?.ElementAtOrDefault(cellX + cellY) ?? 0f;
        var intensity = (byte)(255 * Math.Abs(activation));
        
        return activation >= 0 ? (intensity, 0, 0, 255) : (0, 0, intensity, 255);
    }

    private (byte r, byte g, byte b, byte a) GenerateAttentionMapColor(NeuralState state, int x, int y, int width, int height, Random random)
    {
        // Generate attention map visualization
        var centerX = width / 2;
        var centerY = height / 2;
        var distance = Math.Sqrt((x - centerX) * (x - centerX) + (y - centerY) * (y - centerY));
        var maxDistance = Math.Sqrt(centerX * centerX + centerY * centerY);
        
        var attention = state.AttentionWeights.Values.FirstOrDefault()?.FirstOrDefault() ?? 0f;
        var intensity = (byte)(255 * Math.Abs(attention) * (1 - distance / maxDistance));
        
        return (0, intensity, 0, 255);
    }

    private (byte r, byte g, byte b, byte a) GenerateProcessingFlowColor(NeuralState state, int x, int y, int width, int height, Random random)
    {
        // Generate processing flow visualization
        var flowIntensity = (float)(SystemClock.Instance.GetCurrentInstant() - state.ProcessingTimestamp).TotalSeconds;
        flowIntensity = Math.Max(0, 1 - flowIntensity / 10);
        
        var intensity = (byte)(255 * flowIntensity);
        return (intensity, intensity, intensity, 255);
    }

    // Windows-specific System.Drawing rendering methods
    private void RenderNetworkTopologyWithDrawing(System.Drawing.Graphics graphics, NeuralState state, int width, int height)
    {
        var nodePositions = CalculateNodePositions(state.ActivationPatterns, width, height);
        DrawNetworkNodes(graphics, nodePositions, state.ActivationPatterns);
        DrawNetworkConnections(graphics, nodePositions, state.AttentionWeights);
        DrawTemporalContext(graphics, state.TemporalContext, width, height);
    }

    private void RenderActivationPatternsWithDrawing(System.Drawing.Graphics graphics, NeuralState state, int width, int height)
    {
        DrawActivationHeatmap(graphics, state.ActivationPatterns, width, height);
        DrawConfidenceIndicators(graphics, state.ConfidenceScores, width, height);
    }

    private void RenderAttentionMapsWithDrawing(System.Drawing.Graphics graphics, NeuralState state, int width, int height)
    {
        DrawAttentionWeights(graphics, state.AttentionWeights, width, height);
        DrawDeviceContext(graphics, state.DeviceContext, width, height);
    }

    private void RenderProcessingFlowWithDrawing(System.Drawing.Graphics graphics, NeuralState state, int width, int height)
    {
        DrawProcessingFlow(graphics, state, width, height);
        DrawMemoryIndicators(graphics, state.MemoryContents, width, height);
    }

    private Dictionary<string, System.Drawing.Point> CalculateNodePositions(Dictionary<string, float[]> activationPatterns, int width, int height)
    {
        var positions = new Dictionary<string, System.Drawing.Point>();
        var nodeNames = activationPatterns.Keys.ToList();
        
        var centerX = width / 2;
        var centerY = height / 2;
        var radius = Math.Min(width, height) / 3;
        
        for (int i = 0; i < nodeNames.Count; i++)
        {
            var angle = 2 * Math.PI * i / nodeNames.Count;
            var x = centerX + (int)(radius * Math.Cos(angle));
            var y = centerY + (int)(radius * Math.Sin(angle));
            positions[nodeNames[i]] = new System.Drawing.Point(x, y);
        }
        
        return positions;
    }

    private void DrawNetworkNodes(System.Drawing.Graphics graphics, Dictionary<string, System.Drawing.Point> nodePositions, Dictionary<string, float[]> activationPatterns)
    {
        foreach (var (nodeName, position) in nodePositions)
        {
            if (activationPatterns.TryGetValue(nodeName, out var activations))
            {
                var avgActivation = activations.Length > 0 ? activations.Average() : 0f;
                var nodeSize = (int)(10 + Math.Abs(avgActivation) * 20);
                var intensity = (int)(255 * Math.Abs(avgActivation));
                var color = System.Drawing.Color.FromArgb(intensity, intensity, 255);
                
                using var brush = new System.Drawing.SolidBrush(color);
                graphics.FillEllipse(brush, position.X - nodeSize/2, position.Y - nodeSize/2, nodeSize, nodeSize);
                
                using var pen = new System.Drawing.Pen(System.Drawing.Color.White, 2);
                graphics.DrawEllipse(pen, position.X - nodeSize/2, position.Y - nodeSize/2, nodeSize, nodeSize);
            }
        }
    }

    private void DrawNetworkConnections(System.Drawing.Graphics graphics, Dictionary<string, System.Drawing.Point> nodePositions, Dictionary<string, float[]> attentionWeights)
    {
        var nodeNames = nodePositions.Keys.ToList();
        
        for (int i = 0; i < nodeNames.Count; i++)
        {
            for (int j = i + 1; j < nodeNames.Count; j++)
            {
                var node1 = nodeNames[i];
                var node2 = nodeNames[j];
                
                if (attentionWeights.TryGetValue(node1, out var weights1) && 
                    attentionWeights.TryGetValue(node2, out var weights2))
                {
                    var connectionStrength = CalculateConnectionStrength(weights1, weights2);
                    if (connectionStrength > 0.1f)
                    {
                        var alpha = (int)(255 * connectionStrength);
                        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(alpha, 255, 255, 0), 2);
                        graphics.DrawLine(pen, nodePositions[node1], nodePositions[node2]);
                    }
                }
            }
        }
    }

    private float CalculateConnectionStrength(float[] weights1, float[] weights2)
    {
        if (weights1.Length == 0 || weights2.Length == 0) return 0f;
        
        var minLength = Math.Min(weights1.Length, weights2.Length);
        var correlation = 0f;
        
        for (int i = 0; i < minLength; i++)
        {
            correlation += weights1[i] * weights2[i];
        }
        
        return Math.Abs(correlation) / minLength;
    }

    private void DrawTemporalContext(System.Drawing.Graphics graphics, TimeInfo timeInfo, int width, int height)
    {
        var timeText = $"Session: {timeInfo.TimeSinceWake.TotalMinutes:F1}m";
        using var font = new System.Drawing.Font("Arial", 12);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.White);
        graphics.DrawString(timeText, font, brush, 10, height - 30);
    }

    private void DrawActivationHeatmap(System.Drawing.Graphics graphics, Dictionary<string, float[]> activationPatterns, int width, int height)
    {
        var cellWidth = width / 20;
        var cellHeight = height / 15;
        
        int row = 0, col = 0;
        foreach (var (nodeName, activations) in activationPatterns)
        {
            for (int i = 0; i < Math.Min(activations.Length, 10); i++)
            {
                var intensity = (int)(255 * Math.Abs(activations[i]));
                var color = activations[i] >= 0 
                    ? System.Drawing.Color.FromArgb(intensity, 0, 0) 
                    : System.Drawing.Color.FromArgb(0, 0, intensity);
                
                using var brush = new System.Drawing.SolidBrush(color);
                graphics.FillRectangle(brush, col * cellWidth, row * cellHeight, cellWidth - 1, cellHeight - 1);
                
                col++;
                if (col >= 20) { col = 0; row++; }
                if (row >= 15) break;
            }
            if (row >= 15) break;
        }
    }

    private void DrawConfidenceIndicators(System.Drawing.Graphics graphics, Dictionary<string, float> confidenceScores, int width, int height)
    {
        int y = 10;
        using var font = new System.Drawing.Font("Arial", 10);
        
        foreach (var (key, confidence) in confidenceScores.Take(5))
        {
            var color = confidence > 0.7f ? System.Drawing.Color.Green : confidence > 0.4f ? System.Drawing.Color.Yellow : System.Drawing.Color.Red;
            using var brush = new System.Drawing.SolidBrush(color);
            var text = $"{key}: {confidence:F2}";
            graphics.DrawString(text, font, brush, width - 150, y);
            y += 20;
        }
    }

    private void DrawAttentionWeights(System.Drawing.Graphics graphics, Dictionary<string, float[]> attentionWeights, int width, int height)
    {
        var centerX = width / 2;
        var centerY = height / 2;
        var maxRadius = Math.Min(width, height) / 3;
        
        foreach (var (nodeName, weights) in attentionWeights)
        {
            if (weights.Length == 0) continue;
            
            var avgWeight = weights.Average();
            var radius = (int)(maxRadius * Math.Abs(avgWeight));
            var alpha = (int)(255 * Math.Abs(avgWeight));
            
            using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(alpha, 0, 255, 0));
            graphics.FillEllipse(brush, centerX - radius, centerY - radius, radius * 2, radius * 2);
        }
    }

    private void DrawDeviceContext(System.Drawing.Graphics graphics, Dictionary<DeviceType, bool> deviceContext, int width, int height)
    {
        int x = 10;
        using var font = new System.Drawing.Font("Arial", 10);
        
        foreach (var (device, isEnabled) in deviceContext)
        {
            var color = isEnabled ? System.Drawing.Color.Green : System.Drawing.Color.Gray;
            using var brush = new System.Drawing.SolidBrush(color);
            graphics.DrawString(device.ToString(), font, brush, x, 10);
            x += 80;
        }
    }

    private void DrawProcessingFlow(System.Drawing.Graphics graphics, NeuralState state, int width, int height)
    {
        var flowIntensity = (float)(SystemClock.Instance.GetCurrentInstant() - state.ProcessingTimestamp).TotalSeconds;
        flowIntensity = Math.Max(0, 1 - flowIntensity / 10);
        
        var alpha = (int)(255 * flowIntensity);
        using var pen = new System.Drawing.Pen(System.Drawing.Color.FromArgb(alpha, 255, 255, 255), 3);
        
        for (int i = 0; i < 5; i++)
        {
            var y = height / 6 * (i + 1);
            graphics.DrawLine(pen, 50, y, width - 50, y);
            
            graphics.DrawLine(pen, width - 70, y - 10, width - 50, y);
            graphics.DrawLine(pen, width - 70, y + 10, width - 50, y);
        }
    }

    private void DrawMemoryIndicators(System.Drawing.Graphics graphics, Dictionary<string, object> memoryContents, int width, int height)
    {
        using var font = new System.Drawing.Font("Arial", 10);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.Cyan);
        
        var memoryText = $"Memory Items: {memoryContents.Count}";
        graphics.DrawString(memoryText, font, brush, 10, height - 50);
    }
}