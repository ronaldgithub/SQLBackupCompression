# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Why this tool exists

SQL Server 2025 gives you new backup compression algorithms that can dramatically reduce backup size and I/O — often without increasing CPU load.

The "best" algorithm isn't universal. It depends on your data, your hardware, and your restore-time requirements. The only correct approach is to test:

- How each algorithm affects backup size
- How it affects backup throughput
- How it affects restore speed
- How it impacts CPU usage during both backup and restore

SQL Server 2025 supports:

- **MS_XPRESS** — classic, fast, low CPU
- **QAT** — hardware-accelerated (requires Intel QAT hardware)
- **ZSTD** — modern, higher compression, tunable levels (LOW / MEDIUM / HIGH)

Compression level affects restore time. Higher compression = smaller files = faster I/O = often faster restores, even though decompression uses more CPU. The only way to know the right choice for your environment is to benchmark it.

## Commands

```bash
dotnet build                  # build
dotnet run                    # run the app
dotnet build -c Release       # release build
```

No tests are present. There is no lint step.

## Architecture

Avalonia 11 desktop app targeting `net8.0-windows`. Two tabs: **Backup** and **Restore**. All UI lives in `MainWindow` — no additional windows or dialogs beyond the folder picker.

**Scenarios — 11 fixed combinations** (`BackupScenario.AllScenarios()`):

| Scenario | SQL WITH clause |
| --- | --- |
| NO_COMPRESSION | `NO_COMPRESSION` |
| DEFAULT | `COMPRESSION` |
| MS_XPRESS_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = MS_XPRESS, LEVEL = X)` |
| QAT_DEFLATE_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = QAT_DEFLATE, LEVEL = X)` |
| ZSTD_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = ZSTD, LEVEL = X)` |

`DEFAULT` uses plain `COMPRESSION` with no parameters (inherits server default). `LEVEL` is only valid when an `ALGORITHM` is specified — `COMPRESSION (LEVEL = X)` alone is invalid SQL Server syntax.

QAT_DEFLATE scenarios fail with a SQL Server error when Intel QAT hardware is absent; this is expected and displayed as `Error` status.

---

## Backup tab

**Data flow:**

1. `BackupScenario.AllScenarios()` produces all 11 scenarios at startup.
2. `MainWindowViewModel` wraps each in a `BackupScenarioItemViewModel` and populates `Scenarios`.
3. On **Run**, the VM checks `IsParallelRun`:
   - *Parallel*: all checked scenarios launch at once via `Task.WhenAll`, each with its own `SqlConnection`.
   - *Serial*: scenarios run one at a time in list order; status bar shows which is active.
4. Results post back to the UI thread via `Dispatcher.UIThread.Post(() => vm.ApplyResult(result))`.
5. After all tasks finish, `ComputeRatios()` calculates SIZE% and DUR% relative to the best (smallest = 100%).

**SQL generation** lives entirely in `BackupScenario.GenerateSql` / `BuildWithClause`. The exact SQL executed is stored in `BackupResult.SqlStatement` and displayed in the per-row SQL flyout button.

**Backup file naming:** `{database}_{ScenarioName}_{yyyyMMdd_HHmmss}.bak` — used by both the backup tab (to write) and the restore tab (to discover files).

---

## Restore tab

**Purpose:** benchmark restore speed across the same 11 scenarios. Smaller backup files (higher compression) mean faster I/O reads during restore, which often outweighs the extra CPU for decompression.

**Data flow:**

1. `RestoreTabViewModel` is a property on `MainWindowViewModel` (`RestoreTab`). It owns its own `SqlRestoreService` and its own copy of the 11 scenarios as `RestoreScenarioItemViewModel`.
2. On startup (and whenever `BackupPath` changes), `ScanForFiles()` enumerates all `.bak` files and distributes them to each scenario VM via `SetFiles()`. Matching uses in-memory `Contains($"_{ScenarioName}_")` — not Windows glob — to avoid multi-wildcard quirks.
3. Each scenario row in the UI shows its own **file combobox** (populated from `AvailableFiles`) and a derived **restore target database** name (filename stripped of the `_yyyyMMdd_HHmmss` timestamp).
4. Run is always **serial** — one restore at a time into the same target database.
5. Before each restore, `SqlRestoreService.GetFileListAsync` runs `RESTORE FILELISTONLY` to get logical file names, then generates `RESTORE DATABASE ... WITH MOVE ..., STATS = 10`.
6. `ComputeRatios()` calculates DUR% after all restores finish.

**Config controls:**

- `BACKUP PATH` — folder containing `.bak` files (220 px fixed width).
- `FILTER BY DATABASE` — combobox of SQL Server databases; filters which `.bak` files appear per scenario (prefix match on filename).
- `Overwrite existing database` — checkbox (default: on). When checked, adds `WITH REPLACE` to the restore SQL so an existing database is silently overwritten. When unchecked, the restore will fail if the target already exists.

**Restore SQL shape:**

```sql
RESTORE DATABASE [{targetDb}]
FROM DISK = N'{backupFilePath}'
WITH REPLACE,           -- omitted when Overwrite is unchecked
     MOVE N'{dataLogical}' TO N'{defaultDataPath}\{targetDb}.mdf',
     MOVE N'{logLogical}'  TO N'{defaultLogPath}\{targetDb}_log.ldf',
     STATS = 10;
```

Multiple data/log files get indexed names (`_2.ndf`, `_log2.ldf`, etc.).

**Derived restore target:** `{filename without timestamp}` — e.g. `StackOverflow2010_NO_COMPRESSION`. Each scenario restores into its own database so runs don't interfere. The SQL flyout button per row shows the full statement actually executed.

**Run button** is disabled until at least one checked scenario has a matching backup file. This is maintained by subscribing to each `RestoreScenarioItemViewModel.PropertyChanged` (for `IsChecked` and `FileFound`) and calling `RunCommand.NotifyCanExecuteChanged()`.

---

## Shared patterns

**Number formatting:** `nl-NL` culture is set globally in `Program.Main` before the Avalonia app starts. This gives European formatting everywhere: `10.633,1` (period thousands, comma decimal).

**Compiled bindings** are on by default (`AvaloniaUseCompiledBindingsByDefault=true`). All DataTemplates must declare `x:DataType`. Properties bound to `Foreground` must return `IBrush`, not `string` — see `StatusColor`, `FileSizeRatioColor`, `DurationRatioColor` in both item view models. Two-way `SelectedItem` binding through nested paths (e.g. `RestoreTab.SelectedScenario`) does not reliably fire the setter in compiled bindings — avoid it; bind directly to properties on the item's own VM instead.

**MVVM conventions (CommunityToolkit.Mvvm):**

- `[ObservableProperty]` on `_camelCase` fields — generated public property is `PascalCase`.
- `[RelayCommand]` on `private [async] Task/void` methods — generates `XxxCommand`.
- `partial void OnXxxChanged(T value)` hooks update derived state.
- Computed properties (`StatusText`, `StatusColor`, ratio colors) need explicit `OnPropertyChanged(nameof(...))` calls in `ApplyResult`, `SetRunning`, `Reset`, and `SetRatios`.

**Connection:** Always `Server=localhost;Integrated Security=true;TrustServerCertificate=true`. `CommandTimeout = 0` for backup/restore commands. `STATS = 10` produces progress messages every 10% via `SqlConnection.InfoMessage`.

**Adding a new scenario:** Add an entry in `BackupScenario.AllScenarios()`. Both the backup and restore tabs pick it up automatically — the checklist, runners, ratio calculators, and results grids require no other changes.
