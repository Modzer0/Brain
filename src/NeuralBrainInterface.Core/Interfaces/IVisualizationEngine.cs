using NeuralBrainInterface.Core.Models;

namespace NeuralBrainInterface.Core.Interfaces;

public interface IVisualizationEngine
{
    Task<VisualFrame> RenderNeuralStateAsync(NeuralState state);
    Task UpdateDisplayAsync(VisualFrame frame);
    void SetVisualizationMode(VisualizationMode mode);
    List<VisualizationMode> GetSupportedModes();
    
    event EventHandler<VisualFrame>? FrameRendered;
    event EventHandler<VisualizationMode>? ModeChanged;
}