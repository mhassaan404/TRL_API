namespace TRL_API.Helpers
{
    // Writes log entries to one file per day (trl-yyyy-MM-dd.log, UTC dates) in a folder, and deletes files older than
    // keepDays. Used because the host keeps no console output. Never throws: a log that can't be written is dropped,
    // so logging can't break a request. Line breaks in messages are flattened so a value from a request (a username,
    // say) can't fake extra log lines; an exception's details follow on the next lines, indented.
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly string _directory;
        private readonly int _keepDays;
        private readonly LogLevel _minLevel;
        private readonly object _lock = new();
        private DateTime _cleanedOn = DateTime.MinValue;

        public FileLoggerProvider(string directory, int keepDays = 30, LogLevel minLevel = LogLevel.Warning)
        {
            _directory = directory;
            _keepDays = Math.Max(1, keepDays);
            _minLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

        public void Dispose() { }

        internal bool IsEnabled(LogLevel level) => level != LogLevel.None && level >= _minLevel;

        internal void Write(DateTime utcNow, LogLevel level, string category, string message, Exception? exception)
        {
            var line = $"{utcNow:yyyy-MM-dd HH:mm:ss}Z [{Short(level)}] {category}: {Flatten(message)}";
            if (exception != null)
                line += Environment.NewLine + "    " + exception.ToString().Replace("\n", "\n    ");
            try
            {
                lock (_lock)
                {
                    Directory.CreateDirectory(_directory);
                    File.AppendAllText(Path.Combine(_directory, $"trl-{utcNow:yyyy-MM-dd}.log"), line + Environment.NewLine);
                    if (_cleanedOn != utcNow.Date) { _cleanedOn = utcNow.Date; DeleteOldFiles(utcNow); }
                }
            }
            catch (Exception) { /* logging must never break the application */ }
        }

        private void DeleteOldFiles(DateTime utcNow)
        {
            foreach (var file in Directory.GetFiles(_directory, "trl-*.log"))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (DateTime.TryParseExact(name.Substring(4), "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var day)
                    && day < utcNow.Date.AddDays(-_keepDays))
                    File.Delete(file);
            }
        }

        internal static string Flatten(string message) => message.Replace("\r", " ").Replace("\n", " ");

        private static string Short(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRACE", LogLevel.Debug => "DEBUG", LogLevel.Information => "INFO",
            LogLevel.Warning => "WARN", LogLevel.Error => "ERROR", LogLevel.Critical => "CRIT", _ => level.ToString(),
        };

        private sealed class FileLogger : ILogger
        {
            private readonly FileLoggerProvider _provider;
            private readonly string _category;
            public FileLogger(FileLoggerProvider provider, string category) { _provider = provider; _category = category; }

            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel)) return;
                _provider.Write(DateTime.UtcNow, logLevel, _category, formatter(state, exception), exception);
            }
        }
    }
}
