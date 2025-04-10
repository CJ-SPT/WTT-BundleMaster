using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components;

namespace WTT_BundleMaster
{
    public class LogService
    {
        private readonly SynchronizationContext _syncContext;
        public ObservableCollection<LogEntry> Logs { get; } = new ObservableCollection<LogEntry>();

        public LogService(SynchronizationContext syncContext)
        {
            _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
        }

        public void Log(string message, string level = "info")
        {
            var entry = new LogEntry 
            { 
                Message = message, 
                Level = level, 
                Timestamp = DateTime.Now 
            };

            _syncContext.Post(_ =>
            {
                Logs.Add(entry);
                if (Logs.Count > 1000)
                {
                    Logs.RemoveAt(0);
                }
            }, null);
        }

        public void Clear()
        {
            _syncContext.Post(_ =>
            {
                Logs.Clear();
            }, null);
        }
    }
    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string Message { get; set; } = "";
        public string Level { get; set; } = "info";
    }
}