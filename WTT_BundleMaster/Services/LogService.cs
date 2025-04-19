using System.Collections.Concurrent;
using WTT_BundleMaster.Services;

namespace WTT_BundleMaster;

public class LogService
{
    private readonly SynchronizationContext _syncContext; 
    private readonly ConcurrentQueue<LogEntry> _logQueue = new(); 
    private const int MaxLogs = 1000; 
    private const int ThrottleDelay = 75;
    private bool _isThrottled; 

    private readonly ConfigurationService _config;
    private LogLevel CurrentLogLevel => _config.Config.LogLevel;
    
    public event Action? LogUpdated; 

    public LogService(
        SynchronizationContext syncContext,
        ConfigurationService config
        )
    {
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
        _config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public IReadOnlyCollection<LogEntry> Logs
    {
        get
        {
            lock (_logQueue)
            {
                return _logQueue.ToArray(); 
            }
        }
    }

    public void Log(string message, LogLevel level = LogLevel.Info)
    {
        if (!ShouldLog(level)) return;
        
        var entry = new LogEntry
        {
            Message = message,
            Level = level, 
            Timestamp = DateTime.Now
        };

        lock (_logQueue)
        {
            _logQueue.Enqueue(entry);
            if (_logQueue.Count > MaxLogs)
            {
                _logQueue.TryDequeue(out _); 
            }
        }

        if (!_isThrottled)
        {
            _isThrottled = true;
            _syncContext.Post(_ =>
            {
                LogUpdated?.Invoke();
                Task.Delay(ThrottleDelay).ContinueWith(_ => _isThrottled = false); 
            }, null);
        }
    }

    public void Clear()
    {
        lock (_logQueue)
        {
            _logQueue.Clear(); 
        }
        _syncContext.Post(_ => LogUpdated?.Invoke(), null);
    }

    private bool ShouldLog(LogLevel level)
    {
        if (CurrentLogLevel == LogLevel.Debug) return true;
        
        return (int)level <= (int)CurrentLogLevel;
    }
}

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = "";
    public LogLevel Level { get; set; } = LogLevel.Info; 
}

public enum LogLevel
{
    None = 0,
    Error,
    Success,
    Warning,
    Info,
    Debug
}