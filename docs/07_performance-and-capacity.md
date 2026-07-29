# Performance and Capacity Notes

## Summary
This section documents baseline import performance observations and the current storage ceiling observed with the SQLite-backed deployment. The results represent controlled tests on a single environment and are intended to guide expectations, not to serve as strict guarantees.

## Test scope
The measurements below reflect end-to-end CSV user sync and include:
- CSV validation and parsing
- Database comparison and conflict detection
- Persistence and audit logging

## Import throughput (CSV sync)
Observed results:
- 1,000 users: ~1.9 seconds
- 100,000 users: ~19 seconds

These timings were measured on the same test environment and should be treated as indicative. Factors such as hardware, concurrent activity, and CSV shape (conflict rate, number of updates vs. inserts, validation warnings) can materially affect total duration.

## Capacity ceiling (SQLite)
During scale testing, the current SQLite configuration exhibited instability beyond approximately 250,000 users. This indicates that the file-based database is a limiting factor for higher volumes in this deployment model.

Guidance:
- If projected volume exceeds ~250k users, plan a migration to a production-grade relational database.
- Re-run import and reconciliation tests under expected concurrency and data volumes after any database change.

## Operational implications
- Large imports are CPU and IO bound; schedule them during low-traffic windows.
- Retain CSV files and ImportHistory records for audit traceability.

## Signature verification cost

Signature verification recomputes an HMAC per `SignatureRecord` from its frozen snapshot and checks the per-signer hash chain. Measured via `SignatureVerificationPerformanceTests` (in-memory SQLite, single dev environment, records spread across 50 signers so each signer accumulates a long chain — a deliberate stress of the chain path). Numbers are indicative, not guarantees.

Full sweep (what the background job does — verify every record once):

| Records | Sweep time | Per record |
|---|---|---|
| 1,000 | ~0.07 s | ~0.07 ms |
| 5,000 | ~0.6 s | ~0.12 ms |
| 10,000 | ~2.7 s | ~0.27 ms |
| 50,000 | ~26 s | ~0.52 ms |

On-demand batch of 100 signatures for a signer with a long chain: ~0.5 ms/record at 1k total, rising to ~27 ms/record at 50k total. `SignatureVerificationPerformanceTests` runs the 1,000 and 5,000 cases automatically as part of the normal test suite (`dotnet test`); 10,000/50,000 are on-demand via `SIGNATURE_PERF_COUNT`. Run-to-run variance of a few tenths of a millisecond per record is expected (JIT warmup, machine load) — treat single-run numbers as indicative, not exact.

### Interpretation
- Per-record cost is **superlinear** as an individual signer's chain grows. Cause: `SignatureVerificationService` loads a signer's *entire* chain to locate the single immediately-preceding record (and the batch/sweep reloads that chain per page). A signer with N signatures makes each of their verifications cost O(N).
- The distribution above is a worst case: 50 signers holding all records. Real data skews the other way — most employees sign only their own few documents (short chains, cheap), while managers/admins accumulate long chains (the expensive case).
- **Background sweep**: even the stressed 50k case (~26 s, once per day) is acceptable for an off-peak background job. It is disabled by default (`SignatureVerificationSweep:Enabled`) precisely so this cost is a deliberate opt-in.
- **On-demand verification** is the sharper concern: verifying a long-chain signer's signatures synchronously inside a request (e.g. a manager validating 100 documents) can reach seconds. Watch this before relying on on-demand verification at scale for heavy signers.

### Recommended optimization (not yet applied)
The chain check only needs the signer's single most-recent signature *before* a given record's `SignedAt`, and the `IX_SignatureRecords_SignerUserId_SignedAt` index already supports that lookup. Replacing the full-chain load in `SignatureVerificationService.LoadSignerChainAsync`/`FindPreviousRecord` with a targeted "previous record" query would turn the per-record cost from O(chain length) into roughly O(1), removing the superlinear growth. Defer until on-demand verification or the sweep is actually enabled at scale.

## Benchmarking recommendations
- Capture hardware specs and workload concurrency to make results reproducible.
- Measure separate stages (validation, compare, persistence) to isolate bottlenecks.
- Track memory utilization and lock contention during large sync operations.
- For signature verification, re-run `SignatureVerificationPerformanceTests` with `SIGNATURE_PERF_COUNT` set to the target volume before enabling the periodic sweep in production.
