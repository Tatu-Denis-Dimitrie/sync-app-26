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

## Benchmarking recommendations
- Capture hardware specs and workload concurrency to make results reproducible.
- Measure separate stages (validation, compare, persistence) to isolate bottlenecks.
- Track memory utilization and lock contention during large sync operations.
