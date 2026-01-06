using NeuralBrainInterface.Core.Interfaces;
using NeuralBrainInterface.Core.Models;
using NodaTime;

namespace NeuralBrainInterface.Core.Services;

public class TimeContextManager : ITimeContextManager
{
    private readonly IClock _clock;
    private readonly DateTimeZone _timeZone;
    private Timer? _updateTimer;
    private Duration _updateInterval;
    private Instant _sessionStartTime;
    private Instant? _lastWakeTime;
    private readonly object _timeLock = new();
    private INeuralCore? _neuralCore; // Lazy injection to avoid circular dependency

    public event EventHandler<TimeInfo>? TimeContextUpdated;

    public TimeContextManager()
    {
        _clock = SystemClock.Instance;
        _timeZone = DateTimeZoneProviders.Tzdb.GetSystemDefault();
        _updateInterval = Duration.FromMinutes(1); // Default update every minute
        _sessionStartTime = _clock.GetCurrentInstant();
        _lastWakeTime = _sessionStartTime;
    }

    public void SetNeuralCore(INeuralCore neuralCore)
    {
        _neuralCore = neuralCore;
    }

    public Instant GetCurrentTime()
    {
        return _clock.GetCurrentInstant();
    }

    public Duration GetSessionDuration()
    {
        var currentTime = _clock.GetCurrentInstant();
        return currentTime - _sessionStartTime;
    }

    public Duration GetTimeSinceWake()
    {
        if (_lastWakeTime.HasValue)
        {
            var currentTime = _clock.GetCurrentInstant();
            return currentTime - _lastWakeTime.Value;
        }
        return Duration.Zero;
    }

    public void StartTimeUpdates()
    {
        lock (_timeLock)
        {
            if (_updateTimer != null)
            {
                return; // Already started
            }

            var intervalMs = (int)_updateInterval.TotalMilliseconds;
            _updateTimer = new Timer(async _ => await UpdateNeuralTimeContextAsync(), 
                                   null, 0, intervalMs);
        }
    }

    public void StopTimeUpdates()
    {
        lock (_timeLock)
        {
            _updateTimer?.Dispose();
            _updateTimer = null;
        }
    }

    public async Task UpdateNeuralTimeContextAsync()
    {
        try
        {
            var timeInfo = GetTimeInfo();
            
            // Update neural core with current time context if available
            if (_neuralCore != null)
            {
                var currentState = _neuralCore.GetCurrentState();
                currentState.TemporalContext = timeInfo;
                currentState.ProcessingTimestamp = timeInfo.CurrentDateTime;
            }
            
            // Notify subscribers
            TimeContextUpdated?.Invoke(this, timeInfo);
        }
        catch (Exception ex)
        {
            // Log error but don't throw to avoid breaking the timer
            Console.WriteLine($"Error updating neural time context: {ex.Message}");
        }
    }

    public void ConfigureUpdateInterval(Duration interval)
    {
        lock (_timeLock)
        {
            _updateInterval = interval;
            
            // Restart timer with new interval if it's running
            if (_updateTimer != null)
            {
                StopTimeUpdates();
                StartTimeUpdates();
            }
        }
    }

    public TimeInfo GetTimeInfo()
    {
        var currentInstant = _clock.GetCurrentInstant();
        var zonedDateTime = currentInstant.InZone(_timeZone);
        
        return new TimeInfo
        {
            CurrentDateTime = currentInstant,
            SessionStartTime = _sessionStartTime,
            TimeSinceWake = GetTimeSinceWake(),
            TimeZone = _timeZone,
            IsDaylightSaving = zonedDateTime.IsDaylightSavingTime()
        };
    }

    public void NotifyWakeEvent()
    {
        lock (_timeLock)
        {
            _lastWakeTime = _clock.GetCurrentInstant();
        }
    }

    public void NotifySessionStart()
    {
        lock (_timeLock)
        {
            _sessionStartTime = _clock.GetCurrentInstant();
            _lastWakeTime = _sessionStartTime;
        }
    }

    public void Dispose()
    {
        StopTimeUpdates();
    }
}