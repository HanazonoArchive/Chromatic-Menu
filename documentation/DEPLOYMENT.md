# Chromatic Menu: Deployment & Operations Guide

This guide is written for internet café ("pisonet") technicians and system administrators deploying **Chromatic Menu** on Windows 10 client PCs secured with **WinLock** and **Deep Freeze**.

---

## 1. System Requirements & Architecture

- **Operating System**: Windows 10 (64-bit, Version 22H2 Build 19045 recommended).
- **Framework**: .NET Framework 4.8 (pre-installed natively on Windows 10).
- **Dependencies**: None. `Chromatic Menu.exe` is a 100% self-contained single executable. All necessary assemblies (`Newtonsoft.Json`, Costura runtime) and vector icons are embedded.
- **Hardware Targets**:
  - Minimum 4 GB RAM.
  - Dual-core CPU or better.
  - 1366×768 or higher display resolution (adaptive up to 4K+ at 100%, 125%, 150% DPI).
  - Typical memory footprint: < 80 MB RAM (well under 150 MB ceiling).
  - Idle CPU usage: near 0%.

---

## 2. Technician Step-by-Step Deployment Workflow

Follow this procedure sequentially when setting up client machines.

### Step 1: Thaw the PC (Deep Freeze)
1. Open Deep Freeze control panel (`Ctrl + Shift + Alt + F6` or double-click the tray icon while holding `Shift`).
2. Enter the technician administrative password.
3. Select **"Boot Thawed"** and click **Apply and Reboot**.
4. Allow Windows to restart into thawed state. Confirm the Deep Freeze tray icon shows a red 'X' or flashing indicator.

### Step 2: Copy the Executable
1. Create the installation directory:
   ```cmd
   C:\Chromatic Menu
   ```
   > **Note:** Do **NOT** install under `C:\Program Files` or `C:\Program Files (x86)`. Standard Windows user accounts cannot write to `Program Files` without administrative elevation, which prevents the launcher from saving configuration, caching icons, and writing log files.
2. Copy `Chromatic Menu.exe` into `C:\Chromatic Menu\`.

### Step 3: Initial Launch & Password Setup
1. Double-click `C:\Chromatic Menu\Chromatic Menu.exe` to run it.
2. On first launch, the application automatically creates:
   - `C:\Chromatic Menu\data\config.json` (configuration).
   - `C:\Chromatic Menu\data\logs\launcher.log` (activity & error logs).
   - `C:\Chromatic Menu\data\assets\icons\` (cached program icons).
3. Click **Edit Mode** or **Settings** in the header launcher menu.
4. Enter the default administrator password:
   ```text
   admin
   ```
5. The launcher will detect the default password and prompt:
   **"First-Time Setup: You must set a new administrator password before continuing."**
6. Enter and confirm your shop's master password (minimum 4 characters) and click **Set Password**.

### Step 4: Configure Tabs and Programs
1. Toggle **Edit Mode** (from the header menu).
2. **Add Programs**:
   - Click **Add Program** (or drag `.exe`, `.lnk`, `.bat`, `.cmd` shortcuts directly from Windows Explorer onto the grid).
   - Chromatic Menu automatically extracts 256px high-resolution icons and caches them into `data/assets/icons/`.
   - Paths with known environment variables (`%ProgramFiles%`, `%SystemRoot%`, `%ProgramFiles(x86)%`) are collapsed automatically so shortcuts remain portable across different drive letters or machines.
3. **Add Websites & Folders**:
   - Use **Add Website** for online portals, web games, or café member sites.
   - Use **Add Folder** to expose shared resources or game install locations.
4. **Organize Tabs & Icons**:
   - Drag items between grid cells to reorder them.
   - Drag items onto tab headers to move them between tabs.
   - Adjust the **Icon Size** slider (48px to 160px) to suit your monitor size.
5. In **Settings > Tabs**:
   - Add new tabs, rename existing ones, and choose custom Lucide icons.
6. In **Settings > Appearance**:
   - Choose Dark or Light theme mode.
   - Select an accent color (preset swatches or custom hex).
   - Optionally set a custom shop wallpaper (rendered with `Stretch="Uniform"` to fit without distortion and covered with a subtle theme scrim for readability).
7. In **Settings > General**:
   - Set the café / shop name (e.g., `"CyberZone PisoNet"`).
   - Optionally choose a custom shop logo image.

### Step 5: Enable "Start with Windows"
1. In **Settings > General**, toggle **"Start with Windows"** to **ON**.
2. This writes the startup command to:
   ```text
   HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run -> "Chromatic Menu"
   ```
3. *Note*: Writing to `HKLM` requires administrative privileges. If running as a restricted user, run the launcher once as Administrator to toggle this setting.

### Step 6: Configure WinLock
To ensure customers remain within the café environment:
1. Open the WinLock management console.
2. In the **Allowed Applications** whitelist, add:
   ```text
   C:\Chromatic Menu\Chromatic Menu.exe
   ```
3. Also whitelist your installed game executables, web browsers, and café timer clients.
4. Keep standard WinLock restrictions enabled:
   - Disable Task Manager (`Ctrl+Shift+Esc`, `Ctrl+Alt+Del`).
   - Disable Run dialog (`Win + R`).
   - Disable Windows Start menu and Taskbar if desired.
5. Ensure `C:\Chromatic Menu\Chromatic Menu.exe` is **not** blocked from executing.

### Step 7: Export Configuration
Once the master PC is fully configured:
1. Open **Settings > Backup**.
2. Click **Export Backup**.
3. Choose a destination (e.g., a USB drive).
4. This creates a self-contained `.zip` package containing:
   - `config.json` (all tabs, programs, appearance settings, and password hash).
   - `assets/` (all cached 256px program icons, shop logo, and wallpaper).
   - `manifest.json` (app version and timestamp).

### Step 8: Deploy to Other Client PCs
1. On each client PC (with Deep Freeze thawed):
   - Copy `Chromatic Menu.exe` to `C:\Chromatic Menu\`.
   - Launch `Chromatic Menu.exe`.
   - Unlock with `admin` (or your initial password).
   - Open **Settings > Backup** and click **Import Backup**.
   - Select the `.zip` archive exported in Step 7.
2. Chromatic Menu validates the archive, safeguards against path traversal (Zip Slip), creates a safety `.bak` backup, extracts all assets, and reloads the configuration.
3. The import summary will report whether any shortcuts target files missing on that specific machine.
4. Enable **Start with Windows** in **Settings > General**.

### Step 9: Freeze the PC (Deep Freeze)
1. Open the Deep Freeze control panel.
2. Select **"Boot Frozen"** and click **Apply and Reboot**.
3. Upon reboot, the PC is locked down. Any modifications made by customers or temporary files created during user sessions will be automatically erased on every reboot.

---

## 3. Password Management & Emergency Recovery

Chromatic Menu stores passwords exclusively as one-way **SHA-256 hex hashes**. Plaintext passwords are never written to disk, stored in memory, or output in logs.

### Default Password
- Plaintext: `admin`
- SHA-256 Hash: `8c6976e5b5410415bde908bd4dee15dfb167a9c873fc4bb8a81f6f2ab448a918`
- On first unlock, the launcher requires changing this password.

### Emergency Recovery Password
If the technician forgets the configured password, the launcher includes a built-in compiled **Recovery Password**:
- **Recovery Password**: `default`
- **Compiled Recovery Hash**: `37a8eec1ce19687d132fe29051dca629d164e2c4958ba141d5f4133a33f0688f`
- Entering `default` into any password dialog unlocks the application and allows opening Settings to change the password to something new.

### Generating a Custom Recovery Hash
If you wish to change the hardcoded recovery hash before building:
1. Run the PowerShell helper tool:
   ```powershell
   .\tools\hash-password.ps1 -Password "YourSecretRecoveryPhrase"
   ```
2. Update the constant `RecoveryPasswordHash` in `src\Chromatic Menu\Services\PasswordService.cs`.
3. Rebuild the project in Release mode.

---

## 4. Manual Testing Checklist for Technicians

Perform these tests on a physical client PC with WinLock and Deep Freeze installed:

| Test ID | Test Scenario | Expected Result | Pass / Fail |
|:---:|:---|:---|:---:|
| **T-01** | **Boot Behavior** | PC boots directly into Chromatic Menu. Window is maximized, title bar is visible, and on first run prompts to set a new administrator password before anything else. | [ ] |
| **T-02** | **Window Controls** | Clicking **Minimize** (—) minimizes the window. Clicking **[X]** closes/exits the application cleanly. | [ ] |
| **T-03** | **Alt+F4 Handling** | Pressing `Alt + F4` minimizes the window; does not exit. | [ ] |
| **T-04** | **Alt+Tab & Taskbar Restore** | Pressing `Alt + Tab` or clicking the taskbar item brings the launcher back to the foreground smoothly. | [ ] |
| **T-05** | **Single Instance** | Launching `Chromatic Menu.exe` while it is already running focuses and restores the existing window without opening a second instance. | [ ] |
| **T-06** | **Legitimate Exit** | Right-clicking the taskbar icon and choosing **Close window**, terminating via Task Manager, or performing Windows Restart/Shutdown closes the launcher cleanly without hang. | [ ] |
| **T-07** | **Technical Tools** | Clicking technical tools (Task Manager, Command Prompt, Control Panel, etc.) opens them. If WinLock blocks a tool, a friendly in-app error dialog appears without crashing the launcher. | [ ] |
| **T-08** | **Hardware Monitor** | CPU, RAM, Disk, Network throughput (UP/DOWN), and GPU information update accurately every 3 seconds. Idle CPU remains under 1%. | [ ] |
| **T-09** | **Program Launching** | Clicking a program card launches the application. Launching does not steal focus, minimize, or close the launcher. Canceling a UAC prompt fails silently without error dialog. | [ ] |
| **T-10** | **Broken Shortcut Indicator** | If an executable is deleted or unmounted, the card displays the broken shortcut badge (`unlink` overlay). Clicking it shows a clear error message instead of failing silently. | [ ] |
| **T-11** | **Search Functionality** | Typing in the search bar finds programs across all tabs. Pressing `Esc` clears the search. | [ ] |
| **T-12** | **Edit Mode (Thawed)** | While Deep Freeze is Thawed, add/rename/reorder programs, modify theme, and set password. Reboot PC. Changes persist. | [ ] |
| **T-13** | **Deep Freeze Restoration (Frozen)** | While Deep Freeze is Frozen, enter Edit Mode and add a dummy program. Reboot PC. The dummy program is gone and previous configuration is restored. | [ ] |
| **T-14** | **Unwritable Folder Warning** | If the `C:\Chromatic Menu\data` folder is made read-only, Edit Mode and Settings display the red warning banner: `"Data folder is not writable! Changes cannot be saved."` | [ ] |
| **T-15** | **Emergency Recovery** | Entering `recovery-chromatic-admin` unlocks the launcher when the password is forgotten and prompts to set a new password. | [ ] |

---

## 5. Troubleshooting & Maintenance

### Log Files
Logs are maintained at:
```text
C:\Chromatic Menu\data\logs\launcher.log
```
The log file automatically rotates up to 3 files of 1 MB each (`launcher.log`, `launcher.log.1`, `launcher.log.2`). It logs startup events, configuration saves, WMI fallbacks, and launch failures with timestamps.

### Config File Corruption
If `config.json` is corrupted (e.g., due to sudden power loss before Deep Freeze freeze):
1. Chromatic Menu automatically detects JSON parse errors and attempts to restore from `config.json.bak`.
2. If `config.json.bak` is also invalid, it safely initializes factory defaults (`PisoNet`, 5 default tabs) without crashing.
3. The event is recorded in `launcher.log`.
