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

`QAT_DEFLATE` scenarios require Intel QAT hardware — without it they fail with a SQL Server error, which the app reports as `Error` (this is expected).

## Features

- **Backup tab** — pick a database and backup folder, choose which of the 11 scenarios to run, and run them in parallel or serially. Results show duration, file size, MB/sec, and a ratio (%) against the best result in the run, so you can see at a glance which algorithm won.
- **Restore tab** — benchmark restore speed for the `.bak` files produced by the Backup tab, one scenario at a time, into its own scratch database so runs don't interfere with each other.
- **Connection dialog** — point the app at any SQL Server instance (not just `localhost`), with Windows or SQL Login authentication, and a "Test Connection" check before applying.
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
