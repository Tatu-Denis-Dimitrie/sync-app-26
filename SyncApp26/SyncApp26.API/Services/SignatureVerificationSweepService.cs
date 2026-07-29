namespace SyncApp26.API.Services
{
    /// <summary>
    /// Schedules the read-only SignatureVerificationSweeper on a configurable interval as a safety
    /// net for signature drift that on-demand/after-action verification never happens to touch.
    ///
    /// Opt-in: disabled unless SignatureVerificationSweep:Enabled is true, because a full sweep
    /// recomputes an HMAC per signature and its cost has not been load-validated yet. Turn it on
    /// only after running the performance tests. Interval comes from
    /// SignatureVerificationSweep:IntervalMinutes if set (handy for testing), otherwise
    /// SignatureVerificationSweep:IntervalHours (default 24).
    /// </summary>
    public class SignatureVerificationSweepService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SignatureVerificationSweepService> _logger;
        private readonly IConfiguration _configuration;

        public SignatureVerificationSweepService(
            IServiceProvider serviceProvider,
            ILogger<SignatureVerificationSweepService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue<bool>("SignatureVerificationSweep:Enabled"))
            {
                _logger.LogInformation(
                    "Signature Verification Sweep is disabled (SignatureVerificationSweep:Enabled is false).");
                return;
            }

            var intervalMinutes = _configuration.GetValue<int?>("SignatureVerificationSweep:IntervalMinutes");
            TimeSpan interval;
            if (intervalMinutes.HasValue && intervalMinutes.Value >= 1)
            {
                interval = TimeSpan.FromMinutes(intervalMinutes.Value);
                _logger.LogInformation("Signature Verification Sweep starting; interval {IntervalMinutes}min.", intervalMinutes.Value);
            }
            else
            {
                var intervalHours = _configuration.GetValue<int?>("SignatureVerificationSweep:IntervalHours") ?? 24;
                if (intervalHours < 1) intervalHours = 24;
                interval = TimeSpan.FromHours(intervalHours);
                _logger.LogInformation("Signature Verification Sweep starting; interval {IntervalHours}h.", intervalHours);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var sweeper = scope.ServiceProvider.GetRequiredService<SignatureVerificationSweeper>();
                    await sweeper.RunAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Signature Verification Sweep run failed.");
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
    }
}
