namespace SyncApp26.API.Services.Logging
{
    /// <summary>
    /// Enforces the "at most N log files per day, deleted after M days" policy Serilog's own
    /// retainedFileCountLimit can't express (it counts across all days, not per day -- see
    /// LogFilePruner). Runs on a fixed interval and deletes whatever LogFilePruner flags, per
    /// configured directory.
    ///
    /// Configured under "LogRetention". No section, or an empty Directories list, disables the
    /// service entirely -- it does nothing rather than guessing at a default log location.
    /// </summary>
    public sealed class LogFileRetentionService : BackgroundService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<LogFileRetentionService> _logger;
        private readonly string _contentRootPath;

        public LogFileRetentionService(
            IConfiguration configuration, ILogger<LogFileRetentionService> logger, IHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;
            _contentRootPath = environment.ContentRootPath;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var intervalMinutes = _configuration.GetValue<int?>("LogRetention:SweepIntervalMinutes") ?? 60;
            if (intervalMinutes < 1) intervalMinutes = 60;
            var interval = TimeSpan.FromMinutes(intervalMinutes);

            _logger.LogInformation("Log file retention sweep starting; interval {IntervalMinutes}min.", intervalMinutes);

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

                try
                {
                    await Task.Delay(interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void Sweep()
        {
            var directories = _configuration.GetSection("LogRetention:Directories").GetChildren().ToList();
            if (directories.Count == 0)
            {
                return;
            }

            var retentionDays = _configuration.GetValue<int?>("LogRetention:RetentionDays") ?? 10;
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
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

                var toDelete = LogFilePruner.SelectFilesToDelete(fileNames, maxFilesPerDay, retentionDays, today);

                foreach (var name in toDelete)
                {
                    try
                    {
                        File.Delete(Path.Combine(fullPath, name));
                        deletedCount++;
                    }
                    catch (IOException ex)
                    {
                        // Most likely still open by the active file sink -- picked up on the next sweep.
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
