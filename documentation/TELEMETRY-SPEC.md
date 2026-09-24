# Chromatic Menu v1.0.4: Telemetry, Game Requests & Dashboard — Implementation Spec

This is a handoff document for an AI coding agent (Claude Code). It describes a feature expansion agreed with the project owner. Read `SPEC.md` first, then this file. Where this file conflicts with `SPEC.md`, **this file wins** (Phase 0 updates `SPEC.md` so the two agree).

Work phase by phase. At the end of each phase: build clean in Release, report what was verified vs. not verified (and why), list changed files, and **stop and wait for the owner**.

---

## 0. Background

### 0.1 The shop

- One PisoNet shop, currently **4 PCs**, all Windows 10 (22H2), running WinLock and Faronics Deep Freeze (usually **Boot Frozen**).
- Customers pay with coins into a **physical timer**. When the timer hits 0 it cuts power to the **monitor only**; the PC stays on. The launcher cannot know paid time directly.
- The owner wants to see remotely:
  1. Which PCs are on or off right now, and when they were on over time.
  2. Which program each PC is using (program name only).
  3. An **estimated revenue**: ₱1 per 9 minutes of active use.
  4. Game requests submitted by customers from the launcher.

### 0.2 Release and rollout

- The PCs run **v1.0.3**. This work ships as **v1.0.4** (bump the version in `Chromatic Menu.csproj`, the `.wxs` fallback, and wherever else it appears).
- Rollout is **manual**, per PC:
  1. Boot the PC **Thawed**.
  2. Run `ChromaticMenu-DataTool.bat` → option **0 (Backup)** (§5.4).
  3. Settings › About › Check for Updates → install v1.0.4. The existing updater runs the MSI with admin rights (UAC prompt). The MSI installs or updates the telemetry service.
  4. Run `ChromaticMenu-DataTool.bat` → option **1 (Restore)**. If the data survived the upgrade, the tool says so and restoring is optional.
  5. Check the programs, tabs and name are back, and that `Get-Service ChromaticTelemetry` is Running.
  6. Refreeze the PC.
  No auto-check at boot.
- **Why the backup step exists (verified in the code):** the installed v1.0.3 MSI uses the default `MajorUpgrade` schedule (`RemoveExistingProducts` at sequence 1401, before `InstallInitialize`). Its `FolderCleanupComponent` deletes the whole install folder on uninstall (`util:RemoveFolderEx` + `RemoveFile *.*` on `data\`, `assets\`, `logs\`). So upgrading *from* 1.0.3 fully uninstalls 1.0.3 first, which **deletes `config.json`, icons, logo, wallpaper and logs**. v1.0.4 can't change the cached 1.0.3 uninstall logic; it can only reorder the upgrade (§5.1). The batch tool is the guaranteed safety net for this one upgrade.
- **v1.0.4 and later must never delete user data during an upgrade** (§5.1). The config is only *extended* by migration (§4.1).

### 0.3 What already exists (read these before changing anything)

| Area | File(s) | Notes |
|---|---|---|
| App entry, single instance, global exception handlers | `src/Chromatic Menu/App.xaml.cs` | |
| Config model (camelCase JSON via `[JsonProperty]`) | `src/Chromatic Menu/Models/AppConfig.cs` | `version` = 1, `Migrate()` hook in `ConfigService` |
| Config load/save (atomic, `.bak`, LocalAppData fallback) | `src/Chromatic Menu/Services/ConfigService.cs` | Data dir = `<exe dir>\data` |
| Logger (1 MB x 3 rotation) | `src/Chromatic Menu/Services/LoggerService.cs` | Copy its rotation approach for the service log |
| Start with Windows | `src/Chromatic Menu/Services/StartupService.cs`, `MainViewModel.StartWithWindows` | Writes the **HKCU** Run key. At load, the toggle's state is read from the registry, not from `config.startWithWindows` |
| Technical tools list | `src/Chromatic Menu/Services/ToolLaunchService.cs`, `Models/TechnicalToolModel.cs` | 11 tools in a 3x4 `UniformGrid` in `MainWindow.xaml` → **exactly one free slot** |
| Everything UI | `src/Chromatic Menu/Views/MainWindow.xaml` (+ `.xaml.cs`), `ViewModels/MainViewModel.cs` | All modals are overlays switched by `CurrentDialog` (`Models/DialogState.cs`) |
| Settings modal | `MainWindow.xaml` "FULL SETTINGS MODAL" | Pages 0–5; `OpenSettings()` clamps index to `Math.Min(5, …)` — update when adding a page |
| Deep Freeze detection | `Services/DeepFreezeService.cs` | |
| Updater (manual) | `Services/UpdateService.cs` | Refuses to install while Frozen. Runs `msiexec /i … /passive` |
| MSI | `installer/ChromaticMenu.wxs` (WiX **v4** syntax) | Installs to `ProgramFiles64Folder\Chromatic Menu`, writes `HKLM\Software\HanazonoArchive\ChromaticMenu\InstallDir`, grants `Users` full control on `data\`, `data\assets\`, `data\logs\` |
| Build script | `build-msi.ps1` | Builds app, compiles `Uninstall.exe` with `csc.exe`, runs `wix build` |
| Lucide icon pipeline | `tools/svg/*.svg`, `tools/svg-to-xaml.ps1` / `tools/convert_svg_to_xaml.py` → `Resources/Icons/LucideIcons.xaml` | Add icons only through this pipeline and verify the name exists in the official Lucide repo |

The launcher runs as a **standard user** (`asInvoker`) and never elevates.

### 0.4 Default Supabase credentials (provided by the owner)

**Changed after implementation:** the app has **no built-in project**. Empty URL/key means "not set up": nothing is sent and Request a Game is hidden. The owner's project below is preset only in `tools/ChromaticMenu-DataTool.bat` (variables `DEFAULT_SUPABASE_URL` / `DEFAULT_SUPABASE_KEY`), whose option **[2] Set up Supabase** writes it into the `telemetry` block of `config.json` (launcher force-closed first, other fields verified unchanged, service restarted). The values:

```
DefaultSupabaseUrl     = "https://iqzssuggmcqburqrpvtr.supabase.co"
DefaultSupabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImlxenNzdWdnbWNxYnVycXJwdnRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTAyMjYyNzQsImV4cCI6MjEwNTgwMjI3NH0.WPFeMFK4tRpnSksSO3tal9QhY0dBs5UwGVgAUtGIEps"
```

Verified on 2026-09-24: the key decodes to `role: anon` for project ref `iqzssuggmcqburqrpvtr` (matches the URL), expires 2036, and is accepted by `/auth/v1/settings` (HTTP 200). At that time the project had **no tables yet** (`PGRST205` for `heartbeats` and `game_requests`). The owner must run `docs/supabase/schema.sql` before any data can be sent.

**Never** embed the `service_role` key. The anon key is designed to be public, and security depends entirely on the Row Level Security policies in §2.

### 0.5 Privacy contract (applies everywhere)

A heartbeat contains only: timestamp, PC name, shop name, program name, and the heartbeat interval.
A game request contains only: PC name, shop name, title, description.
**Never** send: IP, MAC, Windows username, file paths, window titles, command lines, or hardware IDs.

---

## 1. Phase 0 — Housekeeping

0. **Build the DataTool first** (`tools/ChromaticMenu-DataTool.bat`, §5.4). It has no dependency on the rest of this work, and the owner can test it on the current v1.0.3 PCs right away.
1. **Move documents out of `docs/`.** `docs/` becomes the GitHub Pages website (§6). Move `docs/DEPLOYMENT.md` → `documentation/DEPLOYMENT.md` (this spec already lives in `documentation/`). Update every reference (e.g. `SPEC.md` §14).
2. **Update `SPEC.md`:**
   - §15 Out of scope: remove "telemetry" and "internet features". Keep coin/timer integration, user accounts, auto-update on boot, multi-language and so on.
   - §5: after "no process tracking", add an exception: *"Exception: the telemetry reporter reads the foreground program's name every few seconds. It never tracks, hooks, or controls processes."*
   - §2 NuGet rule stays: only `Newtonsoft.Json` and `Costura.Fody`. `System.Net.Http` and `System.ServiceProcess` are framework assemblies and are allowed.
   - Add a section **"Telemetry & Online Features"** summarising this document and linking to `documentation/TELEMETRY-SPEC.md`.
   - §14 project structure: add `src/ChromaticTelemetry/`, `src/Shared/`, `docs/` (website), `documentation/`.

---

## 2. Phase 1 — Supabase schema (`docs/supabase/schema.sql`)

One **idempotent** SQL file the owner pastes into the Supabase SQL editor on a fresh project. Re-running it must not fail or duplicate anything. It lives inside `docs/` so the website's setup guide can display and copy it (§6.3). Do not execute it yourself.

Shop-local time zone: **`Asia/Manila`**. Define it in one clearly commented place.

### 2.1 Table `heartbeats`

| Column | Type | Source |
|---|---|---|
| `id` | `bigserial primary key` | server |
| `ts` | `timestamptz not null` | client (clock-corrected UTC, §3.4) |
| `pc_name` | `text not null` (1–64) | `Environment.MachineName` |
| `menu_name` | `text not null` (1–64) | `branding.shopName` from config (default `"PisoNet"`) |
| `program` | `text not null` (1–128) | §4.2 |
| `interval_seconds` | `int not null` (check 15–3600) | the interval in effect when the row was created |
| `received_at` | `timestamptz not null default now()` | server |

Indexes: `(ts desc)`, `(pc_name, ts desc)`.

**Time math rule (everywhere):** time covered by a row = `interval_seconds`. Minutes on = `sum(interval_seconds)/60`. Never assume 1 row = 1 minute.

**Program categories (used by all aggregations):**
- *idle*: `'Chromatic Menu'`, `'Unknown'`
- *active*: everything else, including `'Windows'`
- *top-program lists* exclude `'Chromatic Menu'`, `'Unknown'` and `'Windows'`

### 2.2 Table `game_requests`

| Column | Type |
|---|---|
| `id` | `bigserial primary key` |
| `created_at` | `timestamptz not null default now()` |
| `pc_name` | `text not null` (1–64) |
| `menu_name` | `text not null` (1–64) |
| `title` | `text not null` (1–100, trimmed non-empty) |
| `description` | `text not null default ''` (0–500) |
| `status` | `text not null default 'new'` check in (`'new'`,`'added'`,`'rejected'`) |

Enforce lengths with `check` constraints, not only in the app.

**Server-side cooldown:** a `before insert` trigger (function `security definer`, fixed `search_path`) that raises an exception whose message starts with `cooldown` if the same `pc_name` inserted a request in the last **5 minutes**.

### 2.3 Table `daily_summary`

| Column | Type |
|---|---|
| `day` | `date` (shop-local) |
| `pc_name` | `text` |
| `menu_name` | `text` |
| `minutes_on` | `numeric` — `sum(interval_seconds)/60` |
| `minutes_active` | `numeric` — same, active rows only |
| `top_program` | `text` — by summed time, per §2.1 exclusions (null if none) |
| primary key | `(day, pc_name)` |

### 2.4 Row Level Security & grants

- Enable RLS on all tables.
- `heartbeats`: `anon` → INSERT only. `authenticated` → SELECT only.
- `game_requests`: `anon` → INSERT only, `with check (status = 'new')`. `authenticated` → SELECT, and UPDATE of **only** `status` (revoke update, then `grant update (status) … to authenticated`).
- `daily_summary`: `authenticated` → SELECT only. `anon` → nothing.
- Clients insert with `Prefer: return=minimal` (`anon` has no SELECT, so `return=representation` would fail).

### 2.5 Dashboard RPC functions

Supabase's API caps responses at **1000 rows** by default, and one day of 4 PCs is about 5,760 heartbeats. The dashboard must **not** download raw rows. Provide SQL functions declared `security invoker` (RLS applies). Grant execute to `authenticated` only; revoke from `public` and `anon`.

- `get_pc_status()` → per PC: `pc_name`, `menu_name`, `last_seen`, `last_program`, `last_interval_seconds`, `is_online` (last heartbeat within `2 × interval_seconds + 60 s` of `now()`).
- `get_day_timeline(p_day date)` → per PC, compressed **segments** using gaps-and-islands: `pc_name`, `seg_start`, `seg_end`, `kind` (`'active'` / `'idle'`), `program`. Consecutive rows join a segment if the gap is ≤ `2 × interval_seconds + 30 s`. A larger gap means the PC was off.
- `get_usage(p_from date, p_to date)` → `program`, `pc_name`, `minutes`.
- `get_daily(p_from date, p_to date)` → `day`, `pc_name`, `minutes_on`, `minutes_active`, from `daily_summary` for past days plus a live computation for today.
- `get_hourly_heatmap(p_from date, p_to date)` → `weekday`, `hour`, `avg_active_pcs` (for the busy-hours heatmap).

### 2.6 Scheduled jobs (pg_cron)

- `create extension if not exists pg_cron;`
- Every hour: upsert `daily_summary` for **today and yesterday** (shop-local).
- Daily at 03:00 Manila: delete `heartbeats` older than **90 days**, after their summary exists.
- Idempotent: unschedule by name if present, then schedule.

---

## 3. Phase 2 — Windows service `ChromaticTelemetry`

### 3.1 Projects and shared code

- New project `src/ChromaticTelemetry/ChromaticTelemetry.csproj`: SDK-style, `net48`, `OutputType` = `Exe`, `Newtonsoft.Json` and `Costura.Fody` (same versions as the app), referencing `System.ServiceProcess` and `System.Net.Http`. No `ProjectInstaller`, because the MSI installs the service. Add it to `Chromatic Menu.sln`.
- New folder `src/Shared/`, with files **linked** into both projects (`<Compile Include="..\Shared\X.cs" Link="Shared\X.cs" />`):
  - `TelemetryDefaults.cs`: the Supabase URL and anon key (§0.4), pipe name `ChromaticTelemetry.Pipe`, default interval 60 s, allowed interval range 30–600 s, buffer cap 720.
  - `TelemetryPipeMessage.cs`: the pipe message DTO.
- The service has its **own minimal config DTO**, reading only `branding.shopName`, `startWithWindows` and the `telemetry` object.

### 3.2 Service behaviour

- `ServiceBase` named `ChromaticTelemetry`, running as LocalSystem with automatic start. No UI, console or tray icon. `OnStart` returns quickly; work runs on background threads and timers.
- **Locating config:** read `InstallDir` from `HKLM\Software\HanazonoArchive\ChromaticMenu` (`RegistryView.Registry64`), falling back to the service exe's folder. Path = `<InstallDir>\data\config.json`. Read with `FileShare.ReadWrite | FileShare.Delete` (the launcher replaces the file atomically) and retry once on an IO error.
- **Missing or bad config:** use defaults (send nothing until a valid config says otherwise). Log once, then at most once per 5 minutes.
- **Sending condition:** send heartbeats only when **`startWithWindows == true` AND `telemetry.enabled == true`**. Otherwise keep the pipe server alive (so a reload can re-enable it), send nothing, and clear the buffer.
- **Heartbeat tick** (every `telemetry.heartbeatIntervalSeconds`, clamped 30–600, default 60; non-reentrant `System.Threading.Timer`; first tick right after start):
  1. Build a row `{ ts, pc_name, menu_name, program, interval_seconds }`, with `program` = latest pipe value if received within `max(3 min, 2 × interval)`, otherwise `"Unknown"`.
  2. Payload = buffered rows (oldest first) + the new row, as one JSON array.
  3. `POST {url}/rest/v1/heartbeats` with headers `apikey`, `Authorization: Bearer {key}`, `Content-Type: application/json`, `Prefer: return=minimal`. **This POST is the "is Supabase online" check.** There is no separate ping. One attempt per tick, 15 s timeout.
  4. On 2xx: clear the buffer and log `flush OK, N rows`. On failure: append the new row to the buffer and log the status or exception.
  5. Cap the buffer at 720 rows by dropping the oldest, and log how many were dropped.
- The buffer is **in memory only**. Never write it to disk. It's lost on stop or reboot, which is expected on Frozen PCs.
- A single `HttpClient`, with TLS 1.2 enabled via `ServicePointManager.SecurityProtocol`.

### 3.3 Named pipe server

- Name `ChromaticTelemetry.Pipe`, launcher (client) → service (server), `PipeDirection.In`.
- **ACL trap:** a pipe created by LocalSystem is **not writable by standard users** by default. Create it with a `PipeSecurity` granting `AuthenticatedUserSid` `ReadWrite` and LocalSystem `FullControl` (the `net48` `NamedPipeServerStream` constructor that accepts `PipeSecurity`).
- A background loop: wait for a connection, read newline-delimited JSON until disconnect, repeat. It must survive client crashes, logoff and malformed lines (log and skip). It must never block the heartbeat timer.
- Messages:
  ```json
  {"type":"program","value":"Google Chrome","ts":"2026-09-24T06:32:01Z"}
  {"type":"reload"}
  ```
  `reload` re-reads config.json and applies it immediately: the sending condition, URL, key, interval (reschedule the timer) and shop name.
- `OnStop`: stop the timer, cancel the pipe loop, and dispose everything within a few seconds.

### 3.4 Clock correction

On **every** HTTP response (any status), read the `Date` header and set `clockOffset = serverDate − DateTime.UtcNow`. Row `ts = DateTime.UtcNow + clockOffset`. The offset is 0 until the first response.

### 3.5 Logging

`<InstallDir>\data\logs\telemetry.log`, 1 MB x 3 rotation (same scheme as `LoggerService`). Log failures with reasons, flushes with the row count, the buffer depth when non-zero, config problems (rate-limited), and start/stop. **Never log the anon key.**

---

## 4. Phase 3 — Launcher changes

### 4.1 Config (extend, never overwrite)

Add to `AppConfig` (camelCase like the existing fields):

```json
"telemetry": {
  "enabled": true,
  "supabaseUrl": null,
  "supabaseAnonKey": null,
  "heartbeatIntervalSeconds": 60
}
```

- `null` URL or key means "use the built-in default".
- Bump `version` to 2. `ConfigService.Migrate()` adds the `telemetry` block **only if it's missing** and leaves every existing field (tabs, items, branding, appearance, password hash) untouched. Test this with a real v1.0.3 `config.json`.
- **Keep `startWithWindows` in sync:** at launcher start, after reading the real registry state (`StartupService.IsRunAtStartup()`), write it into `config.startWithWindows` if it differs and send `reload`. The service can't read the user's HKCU and relies on the config value.
- Export and import already include `config.json`. That's fine, because the anon key is public.

### 4.2 `TelemetryReporterService` (runs in the launcher)

- A background loop polls the foreground window **every 5 seconds**, never on the UI thread.
- Resolve the name:
  1. `GetForegroundWindow` → `GetWindowThreadProcessId`. No window or PID 0 → `"Chromatic Menu"` (nothing in front means the customer is effectively idle).
  2. PID = launcher's own process → `"Chromatic Menu"`.
  3. The desktop or taskbar is in front (window class `Progman`, `WorkerW` or `Shell_TrayWnd`) → `"Chromatic Menu"`.
  4. Get the exe path with `OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION)` + `QueryFullProcessImageName`. If that fails → `"Unknown"`.
  5. The exe is under `%SystemRoot%` (e.g. `C:\Windows\explorer.exe`, `SystemSettings.exe`, `Taskmgr.exe`, `mmc.exe`) → `"Windows"`.
  6. The path matches a menu item's resolved target (expand env vars; resolve `.lnk` targets once and cache them) → that **item's display name**.
  7. Otherwise use `FileVersionInfo.FileDescription`, then `ProductName`, then the exe file name without its extension. Trim to 128 characters.
  8. Never send window titles or paths.
- Send `{"type":"program",…}` when the name changes, and at least once per interval.
- Pipe client: connect with a ≈500 ms timeout. On failure, drop the message and retry on the next send. Never throw into the UI, and log only on state changes.
- Send `{"type":"reload"}` after any save that touches `telemetry`, `branding.shopName` or `startWithWindows`.
- Add the P/Invoke declarations to `Native/NativeMethods.cs`. Start after the main window loads; stop in `StopMonitoring()` or on close.

### 4.3 Settings › new "Telemetry" tab

A new nav entry in the Settings modal (e.g. before About; update the `OpenSettings()` clamp):

- **Enable telemetry** toggle.
- A note: *"Heartbeats are only sent when Start with Windows (General tab) is also enabled."*, plus a warning line if Start with Windows is off.
- **Supabase URL** and **anon key** text boxes. Empty means the built-in default (shown as a placeholder hint). The key box is masked, with a "show" toggle. A **Reset to default** button.
- **Heartbeat interval** (seconds), 30–600, validated, default 60.
- **Test connection** button: async `GET {url}/auth/v1/settings` with the `apikey` header → OK or a short error.
- **Service status** line: `ServiceController("ChromaticTelemetry").Status` → Running / Stopped / Not installed (refreshed when the page opens). Standard users can query this.
- A short privacy note listing exactly what is sent.
- Saving auto-saves config and sends `reload`. No admin rights are needed.

### 4.4 "Request a Game" (technical tools, 12th slot)

- Add a 12th tool. `TechnicalToolModel` currently only launches a process, so add a way to mark an in-app action (e.g. an `Action` enum). `LaunchToolCommand` then opens the dialog instead of calling `Process.Start`.
- Icon: `message-square-plus` if it exists in the official Lucide repo (add it through the tools pipeline), otherwise `message-square` (already present).
- **No password.** It's for customers. Hide the tool if telemetry is disabled (it would have nowhere to send).
- New `DialogType.RequestGame`, shown in the compact modal container:
  - **Game title** (required, max 100) and **Description** (optional, max 500, multiline) with character counters.
  - **Send** and **Cancel**. Enter submits only from the title box. Esc cancels.
  - **Hide the Deep Freeze banner** for this dialog.
  - Wire it into `CurrentDialog`'s `OnPropertyChanged` list, `CloseModal()` cleanup, and the Enter/Esc handling in `MainWindow_PreviewKeyDown`.
- Sending: an async `POST {url}/rest/v1/game_requests` directly from the launcher, with `Prefer: return=minimal` and body `{pc_name, menu_name, title, description}`. Disable Send while it's in progress.
  - Success: close the dialog and show the toast "Request sent. Thank you!"
  - Server `cooldown` error: "You can send another request in a few minutes."
  - Network or other error: a friendly message in the dialog, keeping what was typed. No offline queue.
- Client-side cooldown: **5 minutes** (in memory), mirroring the server trigger.

---

## 5. Phase 4 — Installer and build

### 5.1 `installer/ChromaticMenu.wxs` (WiX **v4**; verify syntax against v4 docs, no v3 idioms)

- A new component in `INSTALLFOLDER` with `ChromaticTelemetry.exe` as **KeyPath** (plus its `.exe.config`), containing:
  - `<ServiceInstall Id="ChromaticTelemetryService" Name="ChromaticTelemetry" DisplayName="Chromatic Telemetry" Description="Sends anonymous PC on/off and program-name heartbeats for Chromatic Menu." Start="auto" Type="ownProcess" ErrorControl="normal" Account="LocalSystem" Vital="yes" />`
  - `<ServiceControl Id="ControlChromaticTelemetry" Name="ChromaticTelemetry" Start="install" Stop="both" Remove="uninstall" Wait="yes" />`
  - Recovery: restart on failure (the v4 service-config element).
- A fresh install, or an upgrade from v1.0.3 (where the service doesn't exist), installs the service. An upgrade where it already exists stops it, replaces the binary and starts it again. MSI does this through `ServiceInstall`/`ServiceControl`, so no custom existence check is needed.
- **No Deep Freeze check inside the MSI.** The in-app updater already refuses while Frozen.
- **UPGRADE DATA-LOSS FIX (verified problem, see §0.2):** the existing `FolderCleanupComponent` (GUID `874219AC-…`) uses `util:RemoveFolderEx Property="INSTALLFOLDER"` and `RemoveFile *.*` on `data\`, `assets\`, `logs\` with `On="uninstall"`. The built v1.0.3 MSI has `RemoveExistingProducts` at 1401 (right after `InstallValidate`), so an upgrade fully uninstalls the old version first and wipes the data. Required in v1.0.4:
  1. `<MajorUpgrade … Schedule="afterInstallExecute" />`. The new version installs first, then the old one is removed.
  2. **Keep every existing component GUID unchanged**, especially `FolderCleanupComponent`, `DataFoldersComponent` and `MainExecutable`. Components shared by both versions stay installed when the old one is removed, so the old version's delete rules for them don't run. This is the only lever over the cached 1.0.3 uninstall.
  3. Make the data wipe happen **only on a real uninstall, never during an upgrade**, for all future versions. Preferred: remove `util:RemoveFolderEx` and the `RemoveFile *.*` rules for `data\`, `assets\`, `logs\` from the MSI, and let `Uninstall.exe` (which already deletes the install folder after `msiexec /x`) do the full wipe. If you keep the wipe in the MSI instead, it must be conditioned so it cannot run when `UPGRADINGPRODUCTCODE` is set. Verify the WiX v4 syntax for that condition exists before relying on it.
  4. **Verify on a VM or spare PC:** (a) install the v1.0.3 MSI (`bin/Release/ChromaticMenu-Setup.msi` from git tag or commit `42244f7`), configure a few items, a logo and a wallpaper; (b) upgrade to v1.0.4 via Check for Updates; (c) record whether `data\` survived. Also test v1.0.4 → a dummy v1.0.5: data **must** survive. Report the results honestly. The batch tool (§5.4) covers the 1.0.3 → 1.0.4 case regardless.
- Fix the `Version` fallback to match the csproj.

### 5.2 `build-msi.ps1`

- Build `src/ChromaticTelemetry/ChromaticTelemetry.csproj` with the same `Version`, `AssemblyVersion` and `FileVersion` stamping.
- The WiX source paths point to `src\ChromaticTelemetry\bin\Release\net48\`.

### 5.3 Uninstall

`msiexec /x` stops and removes the service. `installer/UninstallHelper.cs` needs no service logic; just confirm the folder delete still works after the service stops. If §5.1 step 3 moves the data wipe out of the MSI, `Uninstall.exe` is now the only thing that wipes `data\`. Keep its behaviour (full wipe after `msiexec /x`), and document that uninstalling from Windows Settings keeps the data folder.

### 5.4 Backup/restore tool: `tools/ChromaticMenu-DataTool.bat`

A standalone batch file the owner copies to each PC (e.g. via USB) and runs **before and after** the update. It must work on a **v1.0.3** PC, so it can't depend on anything new in v1.0.4. Also attach it to the v1.0.4 GitHub release.

**General**
- Plain `.bat`, ASCII, CRLF line endings. Uses only what ships with Windows 10 (`cmd`, `reg`, `robocopy`, `tasklist`/`taskkill`, `powershell` for small helpers). No emoji or fancy characters in its output.
- **Self-elevates:** if it's not running as admin, relaunch via `powershell Start-Process -Verb RunAs` and exit. The wipe can remove `data\` itself, and recreating it under Program Files needs admin.
- Warn (don't block) if Deep Freeze looks Frozen (e.g. `DFServ` / `FrzState2k` running and no Thawed state), since the backup and restore would be lost on reboot.
- Menu (loops until Exit):
  ```
  Chromatic Menu - Data Backup / Restore
  Install folder : C:\Program Files\Chromatic Menu\
  Last backup    : 2026-09-24 14:30 (12 programs, 5 tabs)

  [0] Backup  (run BEFORE updating)
  [1] Restore (run AFTER updating)
  [2] Open backup folder
  [3] Exit
  ```

**Locating data**
- Install folder: `reg query "HKLM\Software\HanazonoArchive\ChromaticMenu" /v InstallDir /reg:64`, falling back to `%ProgramFiles%\Chromatic Menu\`.
- Data folder = `<InstallDir>data`. Also check the launcher's fallback location `%LOCALAPPDATA%\ChromaticMenu\data` (used when the install folder wasn't writable). Back up whichever holds `config.json`, and back up both if both exist.
- Backup root: **`%SystemDrive%\ChromaticMenu-Backup\`**. It must be outside the install folder, because the upgrade wipes that.

**[0] Backup**
1. If `config.json` is missing, print a clear error and do nothing.
2. If a previous backup exists, rename it to `previous\` (keep exactly one older copy), so running Backup twice by mistake after a wipe can't overwrite a good backup with an empty one. **Also refuse to overwrite** if the current `config.json` has fewer programs than the existing backup, unless the user types `YES`.
3. Copy the data folder with `robocopy "<data>" "<backup>\data" /E /R:1 /W:1`, excluding `config.json.tmp`. `logs\` is optional (include it; it's small).
4. Write `<backup>\backup-info.txt` with the date and time, source path, launcher file version (from the exe), and program and tab counts (read `config.json` with a one-line PowerShell `ConvertFrom-Json`).
5. Verify: `config.json` exists in the backup and its size matches. Print "Backup OK" with the counts, or a clear failure. Treat a robocopy exit code ≥ 8 as failure.

**[1] Restore**
1. If there's no backup, print a clear error.
2. Show `backup-info.txt` and the current state (the current program count, or "no config found"). If the current config already has the same program count as the backup, say "Your data looks intact after the update. Restore anyway? (Y/N)".
3. **Close the launcher with `taskkill /IM "Chromatic Menu.exe" /F`.** It has to be a forced kill: a normal exit runs `App.OnExit → SaveConfig()`, which would overwrite the restored config with defaults.
4. Save the current (post-update) data folder to `<backup root>\pre-restore\` in case the restore itself is unwanted.
5. `robocopy "<backup>\data" "<data>" /E /R:1 /W:1`. Recreate `<data>` if it's missing (admin). Restored files inherit the folder's permissions (the MSI grants `Users` full control on `data\`), so the launcher can write to them afterwards.
6. Verify `config.json` is present and its program count matches the backup. Print "Restore OK".
7. Offer to start the launcher. On first start, v1.0.4 migrates the restored v1 config to v2 (adds the `telemetry` block only, §4.1).

**Must not**
- Touch anything outside the data folder, the backup root and the launcher process.
- Contain or print the password hash.

---

## 6. Phase 5 — Dashboard website (`docs/`, GitHub Pages)

### 6.1 Tech and deployment

- **Static only:** HTML, CSS and ES-module JS. No build step and no npm.
- Libraries from jsDelivr only, with pinned versions: `@supabase/supabase-js@2` and `chart.js@4`.
- Deployed by GitHub Pages from **branch `main`, folder `/docs`**. Add `docs/.nojekyll`.
- A professional, restrained dashboard look, like a standard admin/analytics product (think Vercel, Linear or Supabase's own dashboard): neutral surfaces, one accent color, a clear typographic hierarchy, 8 px card radius, subtle 1 px borders, dark default with a light toggle, Lucide icons as inline SVG. **No emoji.** No gradients, glows or glassmorphism. Responsive down to phone width.

### 6.2 Access flow

1. **Connect screen** (first visit): Supabase project URL + anon key → **Verify** (`GET {url}/auth/v1/settings` with `apikey`; 200 means OK). Save both to `localStorage`. A prominent link: **"New here? Set up Supabase"** → §6.3.
2. **Login screen**: email + password → `supabase.auth.signInWithPassword`. No sign-up UI. The client uses `persistSession: true` and `autoRefreshToken: true`, so the session survives closing the browser until an **explicit Logout**.
3. **Schema check** after login: call `get_pc_status()`. If it's missing, show "Your project doesn't have the schema yet" with a link to the setup guide.
4. **Logout** clears the session. **Change project** also clears the saved URL and key.

### 6.3 Setup guide page (`docs/setup.html`)

For a non-developer, step by step, with screenshots described in text (no image files needed):
1. Create a free Supabase account and project.
2. Open the SQL Editor, paste the schema (shown in a code block, loaded from `docs/supabase/schema.sql`, with a **Copy** button) and run it.
3. **Disable public sign-ups** (Authentication › Sign In / Providers › Email: turn off "Allow new users to sign up").
4. Create the dashboard login (Authentication › Users › Add user › email + password, auto-confirm).
5. Copy the Project URL and anon key (Project Settings › API) into the dashboard's Connect screen **and** into Chromatic Menu › Settings › Telemetry on each PC.
6. A warning: never paste the `service_role` key anywhere.
7. Troubleshooting: no data showing (check the service is running and Start with Windows + telemetry are on), and login fails (user not confirmed).

### 6.4 Dashboard pages and inferences

Everything is inferred from heartbeats and game requests. All data access goes through the §2.5 RPCs; never fetch unbounded raw heartbeats. Default range: today.

- **Overview**
  - KPI tiles: PCs online now / total, total on-time today, total active time today, estimated revenue today, and the utilisation rate (active ÷ on-time).
  - PC cards: Online/Offline badge, "last seen X ago", current program, today's on-time / active time / estimated revenue.
  - Auto-refreshes every 60 s.
- **Timeline:** a date picker, and one 24-hour bar per PC from `get_day_timeline`. Segments are colored active or idle; gaps mean off. Hovering shows the program and start–end times. Derived per PC: first on, last off, number of boots (segments separated by off gaps), and longest session.
- **Programs:** a date range, top programs by time (bar chart), a per-PC breakdown table, and each program's share of total active time.
- **Revenue (estimate):** a date range, estimated revenue per day stacked by PC, totals and a daily average, a comparison with the previous period of the same length, and a busy-hours heatmap (weekday × hour, from `get_hourly_heatmap`).
  - Formula: `minutes_active / minutesPerPeso`, with `minutesPerPeso` defaulting to **9** and editable in Settings.
  - Always shown note: *"Estimate based on minutes a program other than Chromatic Menu was in the foreground. The timer only turns off the monitor, so a game left open after time runs out still counts."*
- **Game requests:** a table (newest first) with a status filter (New / Added / Rejected), actions that update `status`, and a count badge of New requests in the navigation.
- **Settings:** minutes-per-peso rate (saved in `localStorage`), theme, change project, logout.

---

## 7. Phase 6 — Docs

- `README.md`: a new **Telemetry & Dashboard** section covering what's sent, what's never sent, how to disable it (Settings › Telemetry, or turn off Start with Windows) and a link to the website's setup guide. Add the dashboard to Key Features.
- `installer/license.rtf`: one paragraph on anonymous usage telemetry (PC name, program name, shop name, timestamps; no personal data; can be disabled in Settings).
- `documentation/DEPLOYMENT.md`: the v1.0.4 rollout steps exactly as in §0.2 (boot Thawed → DataTool Backup → Check for Updates → DataTool Restore → verify programs and service → refreeze), plus Supabase and dashboard setup. Explain in one paragraph why 1.0.3 → 1.0.4 needs the tool and that later upgrades don't.

---

## 8. Acceptance criteria

Report each item as verified, not verified (with the reason), or failing. Items needing admin rights, a real Supabase project or Deep Freeze may not be verifiable by the agent. Say so; don't claim them.

1. `dotnet build -c Release` of both projects: 0 errors, no avoidable warnings. `build-msi.ps1` produces a v1.0.4 MSI containing the service.
2. **Upgrades:**
   - 1.0.3 → 1.0.4 with DataTool Backup → update → Restore ends with `config.json` (programs, tabs, names, password) and `data\assets\` exactly as before, plus only the `telemetry` block added by migration.
   - Whether 1.0.3 → 1.0.4 preserves data *without* the tool is reported (either result is acceptable, but it must be reported truthfully).
   - 1.0.4 → a test 1.0.5 **must** preserve data without the tool.
   - Uninstalling via `Uninstall.exe` still wipes everything.
2b. **DataTool:** Backup refuses when `config.json` is missing. Running Backup twice keeps `previous\`. Restore force-closes the launcher, saves `pre-restore\`, and the launcher starts with the restored programs. It works on a v1.0.3 PC.
3. After a Thawed install or upgrade, `Get-Service ChromaticTelemetry` shows Running with automatic start. It keeps running across logoff and logon, and after a Frozen reboot it still runs (it's part of the frozen image).
4. With Start with Windows **off** or telemetry **off**, no rows are sent. Turning both on starts sending within one tick, with no restart and no admin prompt.
5. At a 60 s interval, one PC for one hour gives 60 ± 2 rows. Changing the interval in Settings changes the row rate and `interval_seconds`.
6. Network cut for 5 min → the buffer grows. On reconnect, a single batch insert arrives with rows in the correct time order.
7. Program values: launcher/desktop → `Chromatic Menu`; File Explorer or Settings → `Windows`; a menu game → the item's name; Chrome → `Google Chrome`; no launcher running → `Unknown` within the staleness window.
8. Request a Game: the row appears in Supabase. A second request within 5 min is rejected with a friendly message. Over-length input is blocked in the UI and in the DB.
9. Dashboard: connect → login → still logged in after closing the browser → logout works. All pages show correct data for 4 PCs. Time math uses `interval_seconds`.
10. The `anon` key cannot SELECT `heartbeats`, `game_requests` or `daily_summary` (include a curl check in the setup guide's troubleshooting).
11. Uninstall removes the service (`sc query ChromaticTelemetry` → not installed).
12. No window, console or tray icon ever appears from the service.

---

## 9. Pre-existing issues (do **not** fix unless the owner asks; mention if they block you)

- `SPEC.md` vs. code drift: double-click launch (SPEC says single click), HKCU vs. HKLM startup key, `UniformToFill` wallpaper, `DropShadowEffect` usage, hard-coded colors, and the session-lock toggle bypassing the per-open Settings password.
- Icon extraction, shortcut existence checks and Deep Freeze state checks run on the UI thread.
- `DEPLOYMENT.md` has conflicting recovery-password text and says the X button closes the app.
- `bin/` and `obj/` build output is tracked in git.
