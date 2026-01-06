using NeuralBrainInterface.Core.Models;
using NodaTime;

namespace NeuralBrainInterface.Core.Interfaces;

public interface ITimeContextManager
{
    Instant GetCurrentTime();
    Duration GetSessionDuration();
    Duration GetTimeSinceWake();
    
    void StartTimeUpdates();
    void StopTimeUpdates();
    Task UpdateNeuralTimeContextAsync();
    void ConfigureUpdateInterval(Duration interval);
    
    TimeInfo GetTimeInfo();
    
    event EventHandler<TimeInfo>? TimeContextUpdated;
}