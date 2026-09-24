# Chromatic Menu: Master Specification

You are building **Chromatic Menu**, a native Windows desktop launcher for coin-operated internet ccafé PCs ("pisonet"). Follow this spec exactly. If something is unclear or missing, ask before guessing.

## 1. Goal and context

- Customers drop a coin and use a PC. The PC boots straight into this launcher, which shows every program the shop offers, organized in tabs. Customers click a program to open it.
- The PCs run **WinLock** (blocks the taskbar, Task Manager, Win+R, etc.) and **Deep Freeze** (restores the disk on every reboot). Both are outside this project. Do not try to replicate them.
- The shop technician configures the launcher **only while Deep Freeze is Thawed**, then freezes the PC. There is no coin or timer integration. The launcher does not know about time or coins.
- Users range from casual customers to tech-savvy people, so the technical tools are visible to everyone. Do not hide them behind a password.

## 2. Hard constraints

- **Windows 10 only** (primary target: 22H2, build 19045). Everything must run on a stock Windows 10 install with nothing to install.
- **C# + WPF on .NET Framework 4.8** (already included in Windows 10). Use an SDK-style `.csproj` targeting `net48`. Do not target .NET 6/7/8. Do not use WinUI, MAUI, Electron, WebView or Tauri.
- **Output is a single `.exe`** that can be copied to another PC and run alone. Allowed NuGet packages: `Newtonsoft.Json` and `Costura.Fody` (to embed DLLs), and nothing else without asking me first. Framework assemblies such as `System.Net.Http` and `System.ServiceProcess` are allowed. If Costura does not work with the current SDK, tell me and propose the smallest alternative.
- Must run well on low-spec PCs (4 GB RAM, old dual-core CPUs, 1366x768 monitors). Targets: under 150 MB RAM, near 0% idle CPU, window visible within 3 seconds of launch.
- App manifest: `requestedExecutionLevel = asInvoker`, `PerMonitorV2` DPI awareness, Windows 10 `supportedOS` entry (so OS APIs do not lie about the version).
- Language: **English only**. No localization framework.

## 3. Engineering rules (anti-slop, non-negotiable)

1. **No placeholders.** No `TODO`, `// implement later`, stub methods that return fake data, or `NotImplementedException` in delivered code. If a feature belongs to a later phase, do not create its shell early.
2. **No invented APIs, packages or icon names.** If you are not sure an API exists on .NET Framework 4.8, check the docs or confirm it compiles. If you are not sure a Lucide icon exists, check the official repository.
3. **The project must build** (`dotnet build -c Release`) with zero errors and no avoidable warnings at the end of every phase. Say honestly what you could not run or verify. Never claim something was tested when it was not.
4. **Minimal dependencies.** No MVVM framework. Write a small `ObservableObject` and `RelayCommand` yourself.
5. **Architecture:** light MVVM. Views (XAML), ViewModels, Models, and Services (system info, config, launching, icons, password, export/import, startup, logging). No business logic in code-behind except pure UI mechanics (drag and drop, window chrome).
6. **Never block the UI thread.** WMI, disk, network and icon work runs off the UI thread and marshals results back.
7. **Never crash silently.** Global exception handlers log the error, show a friendly in-app message, and keep the launcher running. A crashed launcher on a shop PC means a stuck customer.
8. **No dead code, no commented-out code, no duplicated helpers.** Names should be descriptive. Comments explain *why*, not *what*.
9. **Handle failure states for real:** missing files, corrupt config, blocked launches, no network adapter, WMI failure. Each has defined behavior below.

## 4. Visual design rules

The goal is a clean, modern, restrained look that does **not** look AI-generated or templated.

- **Icons:** only **Lucide** icons (ISC license), converted to XAML geometry. **No emoji, no Segoe MDL2/Fluent font icons, no clip-art, no image-based UI icons.** Program icons come from the programs themselves (see section 9).
- **Lucide pipeline:** get SVGs from the official Lucide repository. Write a small conversion tool in `/tools` that turns SVG elements (`path`, `circle`, `rect`, `line`, `polyline`, `polygon`, `ellipse`) into WPF path data, and generates `Resources/Icons/LucideIcons.xaml` (a `ResourceDictionary` of `Geometry`). Build a `LucideIcon` control: 24x24 viewBox, `Fill = none`, `Stroke` bound to the foreground, `StrokeThickness = 2` scaled with size, round caps and joins. Adding a new icon must be a one-command job. If you cannot download SVGs, tell me exactly which files to drop into `/tools/svg`.
- **Style:** flat surfaces, one accent color, a strict spacing scale (4/8/12/16/24/32), corner radius 8 for cards and 12 for modals, 1px subtle borders. Font defaults to Segoe UI.
- **Forbidden:** purple-to-blue gradients, glassmorphism/blur, neon glows, heavy drop shadows, `BlurEffect`/`DropShadowEffect` (slow on old PCs), stock "dashboard template" looks, decorative animation.
- **Motion:** hover/press transitions of 120 to 150 ms at most. No looping animations.
- **Every interactive element** has visible hover, pressed, focus and disabled states. Every icon-only button has a tooltip.
- **Theming via tokens:** all colors, spacing and radii live in resource dictionaries (`Background`, `Surface`, `SurfaceHover`, `Border`, `TextPrimary`, `TextSecondary`, `Accent`, `Danger`, `Warning`). No hard-coded colors in views.

## 5. Window and app behavior

- **Starts with Windows** as the first program on screen (the technician enables this in Settings; see section 11). It opens **maximized** (not exclusive fullscreen), and it must respect the taskbar work area when the taskbar is visible.
- **Custom title bar** (WindowChrome): shop logo and name on the left; on the right a **Minimize** button and an **X** button. **Both only minimize the window.** No maximize button. `ResizeMode` prevents resizing.
- **Alt+F4 only minimizes** (handle it in `PreviewKeyDown`). Do **not** cancel `Closing`, because closing from the taskbar's right-click menu, Task Manager, and Windows shutdown/restart/logoff must work normally (`SessionEnding` is never blocked).
- Once minimized, the launcher stays minimized until the user brings it back (Alt+Tab or the taskbar). It never restores itself.
- **Launching a program does not change the launcher.** No minimizing, no topmost, no focus stealing, no hooks, no process tracking, no "bring launcher back when the game closes". The launcher behaves like any normal Windows program.
  - Exception: the telemetry reporter (section 17) reads the foreground program's name every few seconds. It never tracks, hooks, or controls processes.
- Clicking a program that is already running simply starts another instance, exactly as Windows does. The launcher does nothing special.
- **Single instance:** a named mutex. A second launch of the launcher restores and activates the existing window, then exits.
- Every program is an independent shortcut. Steam, Epic and similar are just programs; games inside them are not tracked. LAN games are added as separate entries, one per exe.

## 6. Layout

```
┌──────────────────────────────────────────────────────────────────────────┐
│ [logo] Shop Name                                            [—]   [X]    │  title bar
├─────────────────────────┬──────────────────────┬─────────────────────────┤
│ COLUMN 1                │ COLUMN 2             │ COLUMN 3                │  header
│ Computer info (live)    │ Technical tools      │ Launcher menu           │
├─────────────────────────┴──────────────────────┴─────────────────────────┤
│ [Lan Games][Online Games][Browser][School & Office][Others]   [ Search ] │  tab bar
│ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐                                          │
│ │icon │ │icon │ │icon │ │icon │   ...                                    │  program grid
│ │name │ │name │ │name │ │name │                                          │
│ └─────┘ └─────┘ └─────┘ └─────┘                                          │
└──────────────────────────────────────────────────────────────────────────┘
```

- Two regions below the title bar: **header** (three columns, star-sized roughly 44% / 34% / 22%) and **body** (tabs on top, grid below).
- The layout adapts automatically to the monitor's resolution and DPI (test 1366x768, 1600x900, 1920x1080 at 100/125/150%). Use star sizing, min sizes and a compact spacing mode for heights under about 800 px. The header must never overflow or push the body off screen.
- When Edit Mode is on, a slim toolbar appears between the header and the body (see section 8).

## 7. Header

All values come from the real machine. The examples below only show the format. **Every live value refreshes every 3 seconds** on a background thread. Static values are read once at startup and cached. Do **not** display any "!" or "live" marker; that was only a note for me.

### Column 1: computer info

Two sub-columns of label/value pairs.

Left sub-column:
- **Computer Name:** `DESKTOP-3FGC32C`
- **CPU:** cleaned name plus base clock, e.g. `Ryzen 5 3600  3.6 GHz` (strip vendor text such as "AMD", "(R)", "(TM)", "Processor", "6-Core", "CPU @ ...").
- **RAM (live):** `15.34 GB / 16.00 GB`, used / installed. Installed total is the sum of `Win32_PhysicalMemory.Capacity`; used is from `GlobalMemoryStatusEx`.
- **Disks (live):** one line per fixed drive, auto-detected: `C: SSD  97.32 GB / 120.00 GB`. Show only **drive letter and type** (SSD, HDD or NVMe). No brand or model. Fixed drives only (skip removable, optical, network). Show used / total. If there are more than 4 drives, use a second sub-column and never overflow.

Right sub-column:
- **OS:** `Windows 10 Pro  Build 19045` (registry `HKLM\SOFTWARE\Microsoft\Windows NT\CurrentVersion`: `ProductName`, `CurrentBuild`).
- **C/T:** `6 Cores / 12 Threads` (sum across sockets).
- **Ethernet (live):** the adapter **currently in use**: the operational, non-loopback, non-virtual interface that has a default gateway. Label it by its real type (Ethernet or Wi-Fi). Show `UP 350 Kbps` and `DOWN 60 Kbps`, computed from byte-counter deltas between samples. Scale automatically (Kbps below 1000, then Mbps with 2 decimals). Re-detect the adapter every tick. If no adapter is up, show `Not connected`.
- **GPU:** every real GPU, dedicated first, each tagged **Dedicated** or **Integrated**, e.g. `Radeon RX 550/550 Series (Dedicated)`. Ignore "Microsoft Basic Render Driver" and other software adapters. Show up to 2 GPUs.
- **VRAM:** for dedicated GPUs the dedicated memory size (`4.00 GB`). For integrated GPUs show the reserved size marked shared, e.g. `512 MB (shared)`. **Do not use `Win32_VideoController.AdapterRAM`** (it caps at 4 GB and is unreliable). Use DXGI (`DXGI_ADAPTER_DESC1.DedicatedVideoMemory` from a 64-bit-safe call) or the display-class registry value `HardwareInformation.qwMemorySize`. **Do not show a memory type** (DDR5, GDDR5); it cannot be read reliably.
- **Dedicated vs integrated detection:** pick the most reliable method available on Windows 10 (for example D3D12 `UMA` architecture flag, `D3DKMT` adapter type, or a documented name-based fallback for edge cases such as Ryzen APUs that reserve 2 GB). Document the choice in code comments and handle laptops with both GPUs.

**Disk type detection:** use the Storage Management provider (`root\Microsoft\Windows\Storage`): `MSFT_Partition` (drive letter to disk number) joined to `MSFT_PhysicalDisk` (`MediaType` 3 = HDD, 4 = SSD; `BusType` 17 = NVMe). If the type is unspecified, fall back to a seek-penalty query (`IOCTL_STORAGE_QUERY_PROPERTY`); if that also fails, show `Disk`. Compute the drive-to-type mapping once and only recompute when the set of drives changes.

Units: 1 GB = 1024³ bytes (matches what Windows shows). Format with two decimals.

### Column 2: technical tools

Always visible to everyone. A compact grid of icon + label buttons. Data-driven (one list in code) so more tools can be added easily. Initial list:

| Label | Command |
|---|---|
| Control Panel | `control.exe` |
| Uninstall Programs | `control.exe appwiz.cpl` |
| File Explorer | `explorer.exe` |
| Network Connections | `control.exe ncpa.cpl` |
| Device Manager | `devmgmt.msc` |
| Disk Management | `diskmgmt.msc` |
| Services | `services.msc` |
| Task Manager | `taskmgr.exe` |
| Command Prompt | `cmd.exe` |
| Windows Settings | `ms-settings:` |
| System Information | `msinfo32.exe` |

If Windows or WinLock blocks one, catch the failure and show a friendly message (see section 9). Do not crash and do not retry.

### Column 3: launcher menu

Buttons: **Settings**, **Theme**, **Font**, **Edit Mode**. All four require the password (section 8). Theme and Font are shortcuts that open the Settings modal on the Appearance page.

## 8. Body, Edit Mode and password

### Normal mode

- **Tab bar** on top, horizontally scrollable if there are many tabs. Each tab has a Lucide icon and a name. Default tabs: **Lan Games**, **Online Games**, **Browser**, **School & Office**, **Others**. All are renamable and deletable, with at least one tab always required.
- **Search box** in the tab bar, included by default. It searches program names across **all tabs** (case-insensitive substring), shows matches in one grid with a small tab label per result, and clears with Esc. Empty state: "No programs found".
- **Program grid:** auto-flowing, ordered grid of cards (icon + name below, name wraps to 2 lines with ellipsis, full name and path in a tooltip). **Single click or Enter launches.** Arrow keys move focus.
- A tab with no programs shows a friendly empty state.
- **Wallpaper** (if set) sits behind the grid, drawn to **fit** (`Stretch = Uniform`, centered, whole image visible, never stretched or distorted), over the theme background color, with a theme-aware scrim so cards stay readable.

### Password and unlock

- Pressing Edit Mode or Settings (or Theme / Font) opens a small password dialog.
- Accepted if the SHA-256 of the input matches the **stored password hash** (default or custom) **or** the **hard-coded recovery hash** compiled into the app. Both are hashes only; no plaintext in source, config or logs. No salt (my decision).
- The default password is `admin` (hash only). On first successful unlock the user must set a new password before continuing.
- Provide `tools/hash-password.ps1`, which prints the SHA-256 of a string, so I can generate the recovery hash myself. **Do not ask me for the recovery password in chat.** Put the hash in one clearly named constant and tell me where.
- Edit Mode stays on until toggled off or the launcher is restarted. It has no idle timeout. Settings requires the password every time it opens.
- Edit Mode and Settings show a small non-blocking banner: "Changes made while Deep Freeze is active are lost on restart."

### Edit Mode

A slim toolbar is shown: **Icon size** slider, **Add program**, **Add website**, **Add folder**, **Exit Edit Mode**. Everything auto-saves immediately.

- **Add program:** right-click empty grid space, or the toolbar, opens a file dialog (filter: programs `*.exe;*.lnk;*.bat;*.cmd`, plus all files). **Drag files from Windows Explorer onto the grid** also adds them. Validate that the item is a valid type (exe, lnk, bat, cmd, `.url`, http/https link, folder) and that it exists. Reject anything else with a clear message.
- **Add website:** a small dialog for name and URL (must start with http:// or https://).
- **Add folder:** a native folder picker.
- **Rename:** F2 or context menu, inline edit.
- **Reorder:** drag and drop within the grid with a ghost preview and an insertion marker. Order is stored per tab. Dropping onto another tab header moves the item there.
- **Icon size:** slider from 48 to 160 px. Card and label sizes scale with it. It is a global setting. Default depends on resolution.
- **Change icon:** like Windows Properties > Change Icon. Let me choose a `.ico`, `.exe` or `.dll` file (with an icon-index picker for exe/dll). Prefer the native Windows icon picker (`PickIconDlg` from shell32) if it works reliably; otherwise build a simple picker. **Reset to default** restores the icon extracted from the program.
- **Edit details** (double-click or context menu): name, target path, arguments, start-in folder.
- **Move to tab**, **Remove** (Delete key, with confirmation).
- **Multi-select:** Ctrl+click and Shift+click for bulk **Move to tab** and **Remove**.
- **Right-click menu on an item in Edit Mode:** Run, Rename, Change icon, Edit details, Move to tab, Remove.

## 9. Program items, icons and launching

**Item fields:** id, tab id, order, display name, kind (`exe` | `lnk` | `bat` | `url` | `folder`), target, arguments, working directory (defaults to the exe's folder), icon source (`auto` or file + index).

There is **no "always run as admin" option.**

**Launching:**
- `ProcessStartInfo` with `UseShellExecute = true`, the item's arguments and working directory.
- If Windows says elevation is required (Win32 error 740), retry once with verb `runas` (the normal Windows UAC prompt). If the user cancels UAC (error 1223), do nothing silently.
- `.lnk`: let the shell handle it. URLs open in the default browser. Folders open in Explorer.
- Any other failure: show a non-blocking in-app message with a short reason and log the details. Never crash.

**Broken shortcuts:** check that the target exists at load, on tab change, on window activation, and before launching (skip URLs). If it is missing, draw a **broken-shortcut Lucide icon overlay** (for example `file-warning` or `unlink`) on the card, use the tooltip "Program not found: <path>", and on click show the message instead of launching.

**Program icons:**
- Extract a **high-resolution** icon (256 px where available) with `IShellItemImageFactory` (`SIIGBF_ICONONLY`), fall back to `Icon.ExtractAssociatedIcon`, then to a default Lucide app icon.
- Cache each icon as a PNG in `assets/icons/{itemId}.png` when the item is added or its icon changes, so it still displays if the target is missing or the config is imported on another PC. Load lazily, frozen, with a decode size that matches the display size.
- For `.lnk` items, resolve the target (`IShellLink`) for the icon and the properties actions.

**Paths:** when saving a path, replace known prefixes with environment variables (`%ProgramFiles%`, `%ProgramFiles(x86)%`, `%SystemRoot%`, and similar) so a config imported on another PC still resolves. Expand them at launch.

## 10. Context menu in Normal mode

Right-click on a program card, logical and not restrictive:
- **Run**
- **Run as administrator** (native Windows behavior, `runas`)
- **Open file location** (Explorer with the item selected; for `.lnk` open the target's location; hidden for URLs)
- **Properties** (the native Windows Properties dialog via `ShellExecuteEx` with verb `properties`; hidden for URLs)

## 11. Settings modal

Opens after the password. A modal with a left navigation and these pages (Esc closes, Enter confirms, focus stays inside the modal, dark scrim behind):

1. **General:** shop name, logo (optional), **Start with Windows** toggle (writes an `HKLM\...\Run` entry; if not allowed, show a clear message that admin rights are needed).
2. **Appearance:** theme mode (Dark, Light), **accent color**, **custom colors** (background, surface, text, accent) using a small built-in picker (preset swatches + hex input, no third-party picker), **custom wallpaper** (fit, see section 8), **font family** (a curated list limited to fonts actually installed; include Segoe UI, Arial, Tahoma, Verdana, Calibri, Bahnschrift, Consolas where present) and a **font size scale** slider. Changes preview live.
3. **Tabs:** add, rename, choose a Lucide icon, reorder (up/down), delete. When deleting a tab that has programs, ask: move them to another tab (dropdown) or delete them too, or cancel.
4. **Security:** change password (requires the current password; new password is hashed).
5. **Backup:** **Export** and **Import** (section 12).
6. **About:** version, config path, log path, "Open log folder".

Defaults for branding are neutral ("PisoNet" name, no logo, no wallpaper). Everything is editable here.

## 12. Data, config and export/import

- **Config** is a single JSON file, `config.json`, in a `data` folder next to the exe. Include a `version` number and a migration hook. Recommended install folder: `C:\Chromatic Menu` (Windows lets normal users write there; `Program Files` would not).
- **Saving:** atomic (write temp file, then replace), keep `config.json.bak` before each save. If the config is corrupt on load, restore the `.bak`; if that fails, load defaults, and log it. If the folder is not writable, show a clear message in Edit Mode and Settings.
- **Password hash** is stored in the config as SHA-256 hex.
- **Assets** (`data/assets/`): copied logo, wallpaper and cached program icons, so an export is self-contained.
- **Export** creates a **ZIP** (`System.IO.Compression`) containing `config.json`, `assets/`, and a small `manifest.json` (app version, export date). The file contains the password hash, so warn the user in the UI.
- **Import** reads a ZIP, validates it (manifest and config version, path traversal safe), asks for confirmation, **backs up the current config**, then replaces config and assets. After import, report how many programs have broken paths on this PC.

Config shape (example, not exhaustive):

```json
{
  "version": 1,
  "passwordHash": "…",
  "mustChangePassword": true,
  "branding": { "shopName": "PisoNet", "logo": null, "wallpaper": null },
  "appearance": {
    "theme": "dark", "accent": "#…", "customColors": null,
    "fontFamily": "Segoe UI", "fontScale": 1.0, "iconSize": 96
  },
  "startWithWindows": false,
  "tabs": [ { "id": "…", "name": "Lan Games", "icon": "gamepad-2" } ],
  "items": [ {
    "id": "…", "tabId": "…", "order": 0, "name": "…", "kind": "exe",
    "target": "%ProgramFiles%\\…", "arguments": "", "workingDirectory": "",
    "icon": { "source": "auto", "path": null, "index": 0 }
  } ]
}
```

## 13. Performance and reliability

- One 3-second refresh cycle for all live header values, on a background thread. Cache static WMI/DXGI results; run slow queries once.
- No `BlurEffect`/`DropShadowEffect`. Freeze all brushes and images. Decode wallpaper at screen size.
- **Rotating log** at `data/logs/launcher.log` (1 MB x 3 files) for crashes, failed launches and config problems. Do not log passwords or full environment dumps.
- The launcher must start and function with no config, no network and no WMI access, using defaults and showing `N/A` for anything it cannot read.

## 14. Project structure

```
Chromatic Menu/
  SPEC.md
  Chromatic Menu.sln
  src/Chromatic Menu/
    App.xaml, app.manifest, Chromatic Menu.csproj
    Views/          (MainWindow, HeaderPanel, TabBar, ProgramGrid, dialogs, SettingsModal)
    ViewModels/
    Models/
    Services/       (SystemInfo, DiskInfo, GpuInfo, NetworkMonitor, Config, Launch,
                     Icon, Password, ExportImport, Startup, Logger)
    Native/         (P/Invoke and COM interop)
    Controls/       (LucideIcon, ColorPicker, ...)
    Resources/      (Themes, Icons/LucideIcons.xaml, Styles)
  src/ChromaticTelemetry/  (Windows service: heartbeat sender, named pipe server)
  src/Shared/       (files linked into both projects: telemetry defaults, pipe message)
  tools/            (svg-to-xaml converter, hash-password.ps1, ChromaticMenu-DataTool.bat, svg/)
  docs/             (GitHub Pages dashboard website + docs/supabase/schema.sql)
  documentation/    (DEPLOYMENT.md, TELEMETRY-SPEC.md)
```

## 15. Out of scope

Coin/timer integration, remaining-time display, user accounts, automatic update checks on boot, multi-language, touch-specific UI, and any attempt to replace WinLock or Deep Freeze.

## 16. Definition of done (applies to every phase)

- Builds clean in Release. App launches. Nothing from the phase is stubbed.
- The phase's acceptance checklist is walked through and each line is reported as verified, not verified (and why), or failing.
- You list every assumption you made and every file you changed.
- You **stop** at the end of the phase and wait for me.

## 17. Telemetry & online features (v1.0.4)

Full details: `documentation/TELEMETRY-SPEC.md`. Summary:

- A Windows service, **ChromaticTelemetry** (LocalSystem, automatic start, no UI), sends one heartbeat per interval (default 60 s, configurable 30–600 s) to Supabase: timestamp, PC name, shop name (`branding.shopName`), foreground program name, and interval. It sends only when **Start with Windows** and **telemetry** are both enabled. Failed sends are buffered **in memory only** (max 720 rows) and flushed oldest-first in one batch on the next success.
- The launcher reports the foreground program's name to the service over the named pipe `ChromaticTelemetry.Pipe`. Built-in Windows programs are reported as `Windows`; the launcher and desktop as `Chromatic Menu`. Window titles, paths, usernames, IPs and hardware IDs are never sent.
- Settings has a **Telemetry** page (enable, Supabase URL and anon key, interval, test connection, service status). Changes apply through a pipe `reload` message; no admin rights are needed.
- Technical tools include **Request a Game** (title + description, sent to Supabase, 5-minute cooldown). No password is required.
- A static dashboard website in `docs/` (GitHub Pages) reads the data through Supabase Auth and RLS-protected RPC functions.
- Allowed network use: HTTPS to the configured Supabase project, plus the existing GitHub release check.
