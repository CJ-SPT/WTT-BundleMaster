using System.Collections.Concurrent;

namespace WTT_BundleMaster;

public class LogService
{
    private readonly SynchronizationContext _syncContext; 
    private readonly ConcurrentQueue<LogEntry> _logQueue = new(); 
    private const int MaxLogs = 1000; 
    private const int ThrottleDelay = 75;
    private bool _isThrottled; 

    public event Action? LogUpdated; 

    public LogService(SynchronizationContext syncContext)
    {
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
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
}

public class LogEntry
{
    public DateTime Timestamp { get; init; }
    public string Message { get; init; } = "";
    public LogLevel Level { get; set; } = LogLevel.Info; 
}

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Success
}