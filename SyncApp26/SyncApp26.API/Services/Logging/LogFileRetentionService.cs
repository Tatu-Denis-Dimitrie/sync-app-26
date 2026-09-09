namespace SyncApp26.API.Services.Logging
{
    /// <summary>
    /// Enforces the "at most N log files per day, deleted after M days" policy Serilog's own
    /// retainedFileCountLimit can't express (it counts across all days, not per day -- see
    /// LogFilePruner). Deletes whatever LogFilePruner flags, per configured directory
    ///
    /// Configured under "LogRetention". No section, or an empty Directories list, disables the
    /// service entirely -- it does nothing rather than guessing at a default log location.
    /// </summary>
    public sealed class LogFileRetentionService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LogFileRetentionService> _logger;
        private readonly TimeProvider _timeProvider;
        private readonly string _contentRootPath;

        public LogFileRetentionService(
            IConfiguration configuration,
            ILogger<LogFileRetentionService> logger,
            TimeProvider timeProvider,
            IHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;
            _timeProvider = timeProvider;
            _contentRootPath = environment.ContentRootPath;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Sweep() is synchronous directory/file IO. Yielding first lets IHostedService.
            // StartAsync return immediately instead of blocking host startup on however long the
            // first sweep takes -- previously this ran inline, before the loop's first await.
            await Task.Yield();

            var schedule = LogRetentionScheduler.ParseSchedule(_configuration.GetValue<string>("LogRetention:Schedule"));
            var dailyAtLocalTime = LogRetentionScheduler.ParseDailyAtLocalTime(_configuration.GetValue<string>("LogRetention:DailyAtLocalTime"));

            _logger.LogInformation(
                "Log file retention sweep starting; schedule {Schedule}{DailyAtLocalTime}.",
                schedule,
                schedule == LogRetentionSchedule.Daily ? $" at {dailyAtLocalTime}" : string.Empty);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    Sweep();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Log file retention sweep failed.");
                }

                if (schedule == LogRetentionSchedule.Startup)
                {
                    break;
                }

                var delay = schedule == LogRetentionSchedule.Daily
                    ? LogRetentionScheduler.GetDelayUntilNextDailyRun(_timeProvider.GetLocalNow(), dailyAtLocalTime)
                    : GetIntervalDelay();

                try
                {
                    await Task.Delay(delay, _timeProvider, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private TimeSpan GetIntervalDelay()
        {
            var intervalMinutes = _configuration.GetValue<int?>("LogRetention:SweepIntervalMinutes") ?? 60;
            if (intervalMinutes < 1) intervalMinutes = 60;
            return TimeSpan.FromMinutes(intervalMinutes);
        }

        private void Sweep()
        {
            var directories = _configuration.GetSection("LogRetention:Directories").GetChildren().ToList();
            if (directories.Count == 0)
            {
                return;
            }

            var retentionDays = _configuration.GetValue<int?>("LogRetention:RetentionDays") ?? 10;
            var applyToCurrentDay = _configuration.GetValue<bool?>("LogRetention:ApplyToCurrentDay") ?? false;

            // Local date, matching how Serilog itself names rolling files (Serilog.Sinks.File has
            // no UTC option -- it names files from DateTime.Now). Using UtcNow here previously
            // meant every file was up to a day early or late to expire, depending on the server's
            // UTC offset and time of day.
            var today = DateOnly.FromDateTime(_timeProvider.GetLocalNow().DateTime);
            var deletedCount = 0;

            foreach (var directoryConfig in directories)
            {
                var relativePath = directoryConfig.GetValue<string>("Path");
                if (string.IsNullOrWhiteSpace(relativePath))
                {
                    continue;
                }

                var maxFilesPerDay = directoryConfig.GetValue<int?>("MaxFilesPerDay") ?? 10;
                var fullPath = Path.IsPathRooted(relativePath)
                    ? relativePath
                    : Path.Combine(_contentRootPath, relativePath);

                if (!Directory.Exists(fullPath))
                {
                    continue;
                }

                var fileNames = Directory.EnumerateFiles(fullPath, "*.log")
                    .Select(Path.GetFileName)
                    .Where(name => name is not null)
                    .Select(name => name!)
                    .ToList();

                var toDelete = LogFilePruner.SelectFilesToDelete(fileNames, maxFilesPerDay, retentionDays, today, applyToCurrentDay);

                foreach (var name in toDelete)
                {
                    try
                    {
                        File.Delete(Path.Combine(fullPath, name));
                        deletedCount++;
                        _logger.LogDebug("Deleted log file {FileName}.", name);
                    }
                    catch (Exception ex)
                    {
                        // Most likely still open by the active file sink, or a permissions issue --
                        // either way, one bad file must not stop the rest of this directory (or the
                        // remaining directories) from being swept. Picked up again next sweep.
                        _logger.LogWarning(ex, "Could not delete log file {FileName}; will retry next sweep.", name);
                    }
                }
            }

            if (deletedCount > 0)
            {
                _logger.LogInformation("Log file retention sweep removed {Count} old log file(s).", deletedCount);
            }
        }
    }
}
