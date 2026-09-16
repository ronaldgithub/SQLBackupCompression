# SQL Server 2025 Backup Compression Benchmark

A small Windows desktop tool for benchmarking SQL Server 2025's backup compression algorithms — **MS_XPRESS**, **QAT**, and **ZSTD** — so you can pick the right one for your data, hardware, and restore-time requirements instead of guessing.

## Why

SQL Server 2025 adds new backup compression algorithms that can dramatically reduce backup size and I/O, often without increasing CPU load. But the "best" algorithm isn't universal — it depends on your data, your hardware, and how fast you need restores to be. The only correct approach is to test:

- How each algorithm affects backup size
- How it affects backup throughput
- How it affects restore speed
- How it impacts CPU usage during backup and restore

This tool runs all of that for you, side by side, against a real database.

## Scenarios

The app runs 11 fixed backup scenarios:

| Scenario | SQL `WITH` clause |
| --- | --- |
| NO_COMPRESSION | `NO_COMPRESSION` |
| DEFAULT | `COMPRESSION` |
| MS_XPRESS_LOW / MEDIUM / HIGH | `COMPRESSION (ALGORITHM = MS_XPRESS, LEVEL = ...)` |
| QAT_DEFLATE_LOW / MEDIUM / HIGH | `COMPRESSION (ALGORITHM = QAT_DEFLATE, LEVEL = ...)` |
| ZSTD_LOW / MEDIUM / HIGH | `COMPRESSION (ALGORITHM = ZSTD, LEVEL = ...)` |

`QAT_DEFLATE` requires Intel QuickAssist Technology hardware. The app detects your CPU vendor at startup — on a non-Intel machine, the three `QAT_DEFLATE_*` scenarios are shown struck through and disabled with an "Only Intel CPU" note, so they're skipped automatically instead of running and failing.

Every backup statement also includes `WITH COPY_ONLY`, so benchmark runs never disturb a database's real differential base or backup chain.

## Features

- **Backup tab** — pick a database and backup folder, choose which scenarios to run, and run them in parallel or serially. Results (shown side by side with the scenario picker) include duration, file size, MB/sec, and two ratios: SIZE% (against the best result in the run) and RATIO (space saved vs. the original database size). A live TOTAL DURATION readout above the results shows the summed time for whichever scenarios you just ran.
- **Striped backups** — an Off/2x/4x/8x switch next to the Serial/Parallel toggle splits each backup across N files (SQL Server's striped `BACKUP DATABASE ... TO DISK = 'f1', DISK = 'f2', ...`) for faster I/O. The Restore tab automatically groups a scenario's stripe files back into one restorable set.
- **Restore tab** — benchmark restore speed for the `.bak` files produced by the Backup tab, one scenario at a time, into its own scratch database so runs don't interfere with each other.
- **Ask for Feedback** — a header button that runs every backup and restore scenario back to back, saves a full text report (durations, sizes, throughput, ratios, SQL, errors), and opens an email draft reminding you to attach it.
- **Analyse** — a button next to the database picker runs a table-level report (size, storage compression, and counts of LOB/GUID/Unicode/fixed-width/float columns per table) so you can see *why* a database will or won't compress well before running a single backup.
- **Connection dialog** — point the app at any SQL Server instance (not just `localhost`), with Windows or SQL Login authentication, and a "Test Connection" check before applying.
- **Info dialog** — a built-in reference explaining how MS_XPRESS, QAT_DEFLATE, and ZSTD actually work, and how column data types (Unicode text, GUIDs, floats, already-compressed blobs, etc.) and storage features (ROW/PAGE compression, columnstore, TDE) affect what backup compression can save.
- Every row has a SQL flyout showing the exact statement that was executed, for both backup and restore.

## Requirements

- Windows
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A SQL Server 2025 instance you can back up and restore against
- Intel QAT hardware, only if you want to benchmark the `QAT_DEFLATE` scenarios

## Getting started

```bash
dotnet build              # build
dotnet run                 # run the app
dotnet build -c Release    # release build
```

On first launch, use the connection button in the header (defaults to `localhost` with Windows Authentication) to point the app at your SQL Server instance.

## Tech stack

[Avalonia](https://avaloniaui.net/) 11 desktop UI (MVVM via [CommunityToolkit.Mvvm](https://learn.microsoft.com/dotnet/communitytoolkit/mvvm/)) targeting `net8.0-windows`, talking to SQL Server via `Microsoft.Data.SqlClient`.

## License

[MIT](LICENSE)
