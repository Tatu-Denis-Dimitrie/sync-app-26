using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.API.Services
{
    /// <summary>One anomalous signature found during a sweep, for building admin alerts.</summary>
    public sealed record SweepAnomaly(Guid SignatureId, Guid SignerUserId, string Status);

    /// <summary>Outcome of one full sweep, for logging and for tests to assert on.</summary>
    public sealed record SweepSummary(int RecordsChecked, int AnomaliesFound, int BatchesFailed, IReadOnlyList<SweepAnomaly> Anomalies);

    /// <summary>
    /// Read-only safety-net sweep: walks every SignatureRecord in fixed-size pages, recomputes each
    /// one's verification status, and logs any that no longer verify (Invalid / ChainBroken). It
    /// never mutates anything, so it is safe to run repeatedly. Separated from the scheduling
    /// BackgroundService so the sweep itself is unit-testable without a running timer.
    /// </summary>
    public class SignatureVerificationSweeper
    {
        private readonly ApplicationDbContext _context;
        private readonly ISignatureVerificationService _verificationService;
        private readonly ILogger<SignatureVerificationSweeper> _logger;
        private readonly int _pageSize;

        public SignatureVerificationSweeper(
            ApplicationDbContext context,
            ISignatureVerificationService verificationService,
            ILogger<SignatureVerificationSweeper> logger,
            int pageSize = 500)
        {
            _context = context;
            _verificationService = verificationService;
            _logger = logger;
            _pageSize = pageSize;
        }

        private const int MaxAnomaliesReported = 20;

        public async Task<SweepSummary> RunAsync(CancellationToken cancellationToken)
        {
            var recordsChecked = 0;
            var anomaliesFound = 0;
            var batchesFailed = 0;
            var offset = 0;
            var anomalies = new List<SweepAnomaly>();

            while (!cancellationToken.IsCancellationRequested)
            {
                // Only ids are pulled into memory per page — never the whole table. Ordered by Id
                // (a stable total order SQLite can sort server-side, unlike DateTimeOffset) so paging
                // is deterministic across iterations.
                var pageIds = await _context.SignatureRecords
                    .OrderBy(r => r.Id)
                    .Skip(offset)
                    .Take(_pageSize)
                    .Select(r => r.Id)
                    .ToListAsync(cancellationToken);

                if (pageIds.Count == 0) break;

                // Per-batch isolation: a failure verifying one page must not abort the whole sweep —
                // log it, count it, and move on to the next page.
                try
                {
                    var statuses = await _verificationService.GetVerificationStatusBatchAsync(pageIds);
                    foreach (var status in statuses)
                    {
                        recordsChecked++;
                        if (status.Status is "Invalid" or "ChainBroken")
                        {
                            anomaliesFound++;
                            _logger.LogWarning(
                                "Signature sweep found {Status} signature {SignatureId} by signer {SignerUserId}.",
                                status.Status, status.SignatureId, status.SignerUserId);

                            // Capped so a mass-anomaly event can't blow up the admin alert email; the
                            // full count is still reflected in anomaliesFound and every one is logged above.
                            if (anomalies.Count < MaxAnomaliesReported)
                            {
                                anomalies.Add(new SweepAnomaly(status.SignatureId, status.SignerUserId, status.Status));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    batchesFailed++;
                    _logger.LogError(ex,
                        "Signature sweep batch at offset {Offset} failed; continuing with the next batch.", offset);
                }

                offset += pageIds.Count;
            }

            _logger.LogInformation(
                "Signature sweep complete: {RecordsChecked} checked, {AnomaliesFound} anomalies, {BatchesFailed} batches failed.",
                recordsChecked, anomaliesFound, batchesFailed);

            return new SweepSummary(recordsChecked, anomaliesFound, batchesFailed, anomalies);
        }
    }
}
