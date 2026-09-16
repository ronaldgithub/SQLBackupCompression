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

Avalonia 11 desktop app targeting `net8.0-windows`. Two tabs: **Backup** and **Restore**, plus supporting dialogs opened from `MainWindow`: the folder picker, `ConnectionDialog`, `InfoWindow` (compression algorithm reference), `AnalysisWindow` (table-level compression readiness report), and `AboutWindow`.

**Scenarios — 11 fixed combinations** (`BackupScenario.AllScenarios()`):

| Scenario | SQL WITH clause |
| --- | --- |
| NO_COMPRESSION | `NO_COMPRESSION` |
| DEFAULT | `COMPRESSION` |
| MS_XPRESS_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = MS_XPRESS, LEVEL = X)` |
| QAT_DEFLATE_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = QAT_DEFLATE, LEVEL = X)` |
| ZSTD_LOW/MEDIUM/HIGH | `COMPRESSION (ALGORITHM = ZSTD, LEVEL = X)` |

`DEFAULT` uses plain `COMPRESSION` with no parameters (inherits server default). `LEVEL` is only valid when an `ALGORITHM` is specified — `COMPRESSION (LEVEL = X)` alone is invalid SQL Server syntax.

Every generated backup statement also includes `COPY_ONLY`, so benchmark runs never disturb a database's real differential base or backup chain.

`Services/CpuInfo.cs` detects the CPU vendor from the `PROCESSOR_IDENTIFIER` environment variable at startup. When it isn't Intel, the three `QAT_DEFLATE_*` scenarios (`BackupScenarioItemViewModel.IsQatUnsupported`) start unchecked and disabled, render struck through with an "Only Intel CPU" note beside the name, and are filtered out of `SelectAll()` and `Run()` — they're never actually executed, so they never show a stale hardware error.

---

## Backup tab

**Layout:** config card (server / database / CPU / backup path) at the top, a Run button row, then the SCENARIOS checklist and RESULTS grid side by side in a `260px | *` grid filling the rest of the window — both scroll independently. There is no SQL preview panel; the per-row SQL flyout is the only way to see generated SQL before or after a run.

**Data flow:**

1. `BackupScenario.AllScenarios()` produces all 11 scenarios at startup.
2. `MainWindowViewModel` wraps each in a `BackupScenarioItemViewModel` and populates `Scenarios`.
3. On **Run**, the VM checks `IsParallelRun` (default: `false`, i.e. Serial):
   - *Parallel*: all checked scenarios launch at once via `Task.WhenAll`, each with its own `SqlConnection`.
   - *Serial*: scenarios run one at a time in list order; status bar shows which is active.
4. Results post back to the UI thread via `Dispatcher.UIThread.Post(() => vm.ApplyResult(result, databaseSizeMb))`.
5. After all tasks finish, `ComputeRatios()` calculates SIZE% and DUR% relative to the best (smallest = 100%), and `TotalDurationText` is set to the summed duration of every scenario just run (shown top-right of the RESULTS header, reset to `-` when a new run starts).

**Striped backups:** a second segmented control (`Off`/`2x`/`4x`/`8x` — "Off" binds to `IsStripe1`, i.e. `StripeCount == 1`; `MainWindowViewModel.StripeCount` with derived `IsStripe1/2/4/8` radio-bound bools) sits next to the Serial/Parallel toggle. It's a run-level setting, independent of scenario/compression choice: `BackupScenario.GenerateSql`/`GetBackupFilePaths` split one `BACKUP DATABASE` statement across N files (`TO DISK = N'f1', DISK = N'f2', ...`) when stripe count > 1, naming them `..._stripe{i}of{N}.bak`; at `Off` (`StripeCount == 1`) the filename is unchanged (`{database}_{ScenarioName}_{yyyyMMdd_HHmmss}.bak`) so existing backups keep working. `BackupResult.BackupFilePaths` holds all stripe paths; size/throughput are summed across them.

**SQL generation** lives entirely in `BackupScenario.GenerateSql` / `BuildWithClause`. The exact SQL executed is stored in `BackupResult.SqlStatement` and displayed in the per-row SQL flyout button.

**Backup file naming:** `{database}_{ScenarioName}_{yyyyMMdd_HHmmss}.bak` when striping is Off, or `{database}_{ScenarioName}_{yyyyMMdd_HHmmss}_stripe{i}of{N}.bak` per file when striped — used by both the backup tab (to write) and the restore tab (to discover and group files).

**RATIO column:** `BackupScenarioItemViewModel.CompressionRatioText`, computed in `ApplyResult` as `100% × (1 − backup size ÷ database size)` — space saved versus the *source database's* size (from the selected `DatabaseInfo.SizeMB`), not versus the best result in the run (that's SIZE%).

**Analyse button:** next to the DATABASE picker, enabled once a database is selected. `MainWindowViewModel.RunTableAnalysisAsync` runs `SqlAnalysisService.GetTableAnalysisAsync` (a fixed query over `sys.tables`/`sys.dm_db_partition_stats`/`sys.columns`) against the selected database and opens `Views/AnalysisWindow.axaml` (backed by `AnalysisWindowViewModel`) showing the top 25 tables by size, storage compression, and LOB/GUID/Unicode/fixed-width/float column counts, with a bold TOTAL row summed across the listed tables. Failures surface through `StatusMessage`, same pattern as `LoadDatabasesAsync` — no dialog opens on error.

---

## Restore tab

**Purpose:** benchmark restore speed across the same 11 scenarios. Smaller backup files (higher compression) mean faster I/O reads during restore, which often outweighs the extra CPU for decompression.

**Data flow:**

1. `RestoreTabViewModel` is a property on `MainWindowViewModel` (`RestoreTab`). It owns its own `SqlRestoreService` and its own copy of the 11 scenarios as `RestoreScenarioItemViewModel`.
2. On startup (and whenever `BackupPath` changes), `ScanForFiles()` enumerates all `.bak` files, filters by the `_{ScenarioName}_` marker, then groups them via `RestoreFileGroup.GroupFrom` — a striped backup's `_stripe{i}of{N}.bak` files collapse back into one group/one combobox entry (`RestoreScenarioItemViewModel.AvailableFiles` is `ObservableCollection<RestoreFileGroup>`), so restoring a striped backup still means picking one item and running one `RESTORE` statement with N `DISK =` clauses.
3. Each scenario row in the UI shows its own **file combobox** (populated from `AvailableFiles`) and a derived **restore target database** name (`RestoreFileGroup.RestoreDbName`, the group's filename stripped of the `_yyyyMMdd_HHmmss` timestamp and any stripe suffix).
4. Run is always **serial** — one restore at a time into the same target database.
5. Before each restore, `SqlRestoreService.GetFileListAsync` runs `RESTORE FILELISTONLY` to get logical file names, then generates `RESTORE DATABASE ... WITH MOVE ..., STATS = 10`.
6. `ComputeRatios()` calculates DUR% after all restores finish, and `TotalDurationText` is set to the summed duration of the scenarios just restored (same pattern as the Backup tab).

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

Multiple data/log files get indexed names (`_2.ndf`, `_log2.ldf`, etc.). `FROM` becomes multiple comma-separated `DISK = N'...'` clauses when restoring a striped backup set (see `RestoreFileGroup` above).

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

**Connection:** Configured via `Models/ConnectionSettings.cs` (Server, Windows/SQL auth, TrustServerCertificate), defaulting to `Server=localhost;Integrated Security=true;TrustServerCertificate=true`. `MainWindowViewModel` owns one shared `ConnectionSettings` instance, constructs `SqlBackupService` with it, and passes it into `RestoreTabViewModel`'s constructor, which uses it to build its own `SqlRestoreService` — both services read `connectionSettings.BuildConnectionString()` per call, so a change is picked up on the next connection without re-registering anything. `CommandTimeout = 0` for backup/restore commands. `STATS = 10` produces progress messages every 10% via `SqlConnection.InfoMessage`.

**Connection dialog:** The header button (bound to `ConnectionSummary`, e.g. "localhost (Windows Auth)") opens `Views/ConnectionDialog.axaml` via `MainWindow.OnConnectionClick`. The dialog edits a scratch copy (`ConnectionDialogViewModel`, constructed from the current `ConnectionSettings`) so Cancel discards changes; it has its own "Test Connection" button that opens a throwaway `SqlConnection` to report success/failure without touching the app's live services. OK calls `MainWindowViewModel.ApplyConnectionSettingsAsync`, which copies the new settings into the shared `ConnectionSettings` instance (via `CopyFrom`, so both services see it), then reloads the database list on both tabs.

**Info dialog** (`Views/InfoWindow.axaml`, opened via the header "Info" button) is static reference content — no ViewModel, no live data — covering how MS_XPRESS/QAT_DEFLATE/ZSTD work and how column data types and storage features (ROW/PAGE compression, columnstore, TDE) affect backup compression. Purely documentation; update it by hand if the scenario table or algorithm behavior changes.

**About dialog** (`Views/AboutWindow.axaml`) shows app name/version (from the `Version` MSBuild property in the `.csproj`, read via `AssemblyInformationalVersionAttribute`), a "Copy diagnostics" button, a "Help / contact" button (mailto with diagnostics pre-filled), and a CONTACT card with plain link buttons (`Classes="link"`, `Tag` holds the URI, all routed through one `OnLinkClick` handler) for mail/GitHub/Home/Posts. The contact address itself lives in `Services/AppContact.cs` (`AppContact.Email`), shared with the "Ask for Feedback" button below.

**Ask for Feedback button** (header, next to Info/About): `MainWindowViewModel.RunFeedbackReportAsync` force-checks and runs all backup scenarios (`SelectAllCommand` + `RunCommand.ExecuteAsync`, bypassing the command's `CanExecute` gate), rescans the restore tab against the same database/path, force-checks and runs all restore scenarios the same way, then restores every checkbox/filter/path the user had before it started (all in a `finally` block). `Services/FeedbackReportService.Build` turns both tabs' results (`ScenarioReportRow`, one per scenario: status/duration/size/MB-per-sec/ratio/SQL/error) plus environment info (`CpuInfo`, OS, runtime, app version) into a single plain-text report, saved as `{database}_FeedbackReport_{yyyyMMdd_HHmmss}.txt` in the backup folder — the app's only text-file output; everything else is `.bak` files written by SQL Server itself. `MainWindow.OnAskForFeedbackClick` then opens a mailto draft (via `AppContact.Email`, same pattern as `AboutWindow.OnContactClick`) telling the user to attach the file — mailto can't attach automatically — and opens the file's folder, both through Avalonia's `ILauncher`. Enabled via `CanAskForFeedback` (a database is selected and neither tab, nor the feedback run itself, is already running).

**Adding a new scenario:** Add an entry in `BackupScenario.AllScenarios()`. Both the backup and restore tabs pick it up automatically — the checklist, runners, ratio calculators, and results grids require no other changes.
