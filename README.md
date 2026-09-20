# Chromatic Menu

A lightweight, customizable desktop launcher and kiosk menu designed for cybercafes, PisoNet terminals, gaming lounges, and multi-user Windows environments.

<p align="center">
  <img src="program-ui.png" alt="Chromatic Menu User Interface" width="100%" />
</p>

Built with C# and WPF on .NET Framework 4.8, Chromatic Menu provides an organized, full-screen portal for games and applications, featuring live hardware monitoring, administrator lockouts, Faronics Deep Freeze awareness, and a WiX-powered MSI installer with automated updates.

---

## Key Features

### 🎮 Application & Game Launcher
- **Tabbed Organization**: Group programs into custom tabs (e.g., Online Games, Offline Games, Productivity, Utilities) with customizable icons.
- **Drag-and-Drop Management**: Reorder items, transfer entries between categories, or edit launch targets, parameters, and working directories on the fly.
- **Broken Path Detection**: Automatically verifies shortcut targets on launch and flags broken or missing executables.
- **Right-Click Context Menus**: Run as Administrator, open file location, inspect shortcut properties, or launch directly.

### Deep Freeze Awareness & Auto-Updates
- **Live State Inspection**: Probes the system registry, active kernel drivers (`DeepFrz.sys`, `DFServ.sys`), and background processes to detect Faronics Deep Freeze installations.
- **Frozen vs. Thawed Detection**: Determines whether the machine is currently **Boot Frozen** or **Thawed** via registry queries and official `DFC.exe` status calls.
- **Safe Auto-Updater**: Queries the GitHub Releases API for new `.msi` packages. If Deep Freeze is Boot Frozen, updates are safely locked with an on-screen warning so updates are never lost on reboot.
- **1-Click Passive Installation**: When Thawed, downloads the latest MSI and executes a passive upgrade without requiring manual re-configuration.

### Administrator & Kiosk Security
- **Session Locking**: Lock and Unlock toggle with visual indicator. Requires an administrator password to enter Edit Mode, modify appearance, or access Settings.
- **First-Time Password Setup**: Enforces setting a custom administrator password on initial deployment.
- **Kiosk-Friendly Windowing**: Borderless maximized layout clamped to the active monitor work area (`WM_GETMINMAXINFO`) to prevent taskbar overlap or clipping.

### System Telemetry & Diagnostics
- **Live Hardware Stats**: Real-time polling of CPU model, active GPU, RAM usage, storage volume capacities, local IP address, and system uptime.
- **Technical Tool Shortcuts**: One-click access to Windows built-in diagnostic utilities (Task Manager, Device Manager, Network Connections, DirectX Diagnostic Tool, Volume Control).

### Themes & Customization
- **Modern Dark & Light Themes**: Polished themes with custom transparency and glass-inspired styling.
- **Color Palettes**: Curated accent colors (Sky Blue, Indigo, Emerald, Amber, Rose, Purple, Cyan, Orange) plus hex inputs for custom backgrounds, surfaces, and text.
- **Typography & Font Scaling**: Curated system font selection with real-time UI scaling from `0.8x` to `1.4x`.
- **Custom Wallpapers**: Full-window wallpaper support with centered uniform aspect fill and subtle scrim overlay for readability.

### Backup & Portability
- **JSON Configuration**: Configuration is stored in atomic JSON format (`data/config.json`).
- **Export & Import**: Export backup packages (`.zip`) and import them onto other terminal PCs with automatic validation.

---

## Tech Stack

| Component | Technology |
| :--- | :--- |
| **Framework** | .NET Framework 4.8 / WPF (Windows Presentation Foundation) |
| **Language** | C# |
| **Architecture** | MVVM (Model-View-ViewModel) with custom `RelayCommand` and `ObservableObject` |
| **Packaging** | Costura.Fody (assembly embedding) |
| **Installer** | WiX Toolset v4 (MSI package with `WixUI_InstallDir` and `WixToolset.Util.wixext`) |
| **Telemetry** | Win32 APIs, Windows Management Instrumentation (`System.Management`) |

---

## Directory Structure

```
Chromatic Menu/
├── bin/
│   └── Release/
│       ├── ChromaticMenu-Setup.msi    # Generated WiX v4 installer
│       └── Uninstall.exe              # Self-relocating uninstaller helper
├── installer/
│   ├── ChromaticMenu.wxs              # WiX v4 setup definition
│   ├── UninstallHelper.cs             # Standalone uninstaller source
│   └── license.rtf                    # EULA license displayed in wizard
├── src/
│   └── Chromatic Menu/
│       ├── Controls/                  # Lucide vector icon controls & custom elements
│       ├── Converters/                # WPF XAML value converters
│       ├── Models/                    # Data models (Config, ProgramItem, Tab, SystemInfo)
│       ├── Native/                    # Win32 P/Invoke methods
│       ├── Resources/                 # App icon, Lucide SVG geometries, themes, styles
│       ├── Services/                  # Business logic (DeepFreeze, Update, Config, Launch)
│       ├── ViewModels/                # MainViewModel & ProgramItemViewModel
│       └── Views/                     # MainWindow.xaml & MainWindow.xaml.cs
├── build-msi.ps1                      # Automated build and MSI generation script
└── README.md
```

---

## Building from Source

### Prerequisites
- Windows 10 or Windows 11
- [.NET SDK](https://dotnet.microsoft.com/download) (6.0+ SDK recommended for building the project)
- [.NET Framework 4.8 Developer Pack](https://dotnet.microsoft.com/download/dotnet-framework/net48)
- WiX Toolset v4 (`dotnet tool install --global wix --version 4.0.6`)

### Build Executable
```powershell
dotnet build -c Release "src\Chromatic Menu\Chromatic Menu.csproj"
```
The compiled standalone executable will be located in:
`src\Chromatic Menu\bin\Release\net48\Chromatic Menu.exe`

### Build MSI Installer
Run the automated build script:
```powershell
.\build-msi.ps1
```

To build a specific release version (e.g. `1.0.1`):
```powershell
.\build-msi.ps1 -Version "1.0.1"
```
The script will:
1. Compile the project stamped with the specified version.
2. Compile the standalone `Uninstall.exe` helper.
3. Build the setup package via WiX v4 at `bin\Release\ChromaticMenu-Setup.msi`.

---

## Installation & Deployment

### Interactive Setup
Double-click `ChromaticMenu-Setup.msi` to run the setup wizard:
1. **Default Target**: Installs to `C:\Program Files (x86)\Chromatic Menu\`.
2. **Shortcuts**: Automatically creates Desktop and Start Menu (`Programs > Chromatic Menu`) shortcuts.
3. **Permissions**: Sets up the writable runtime folders (`data\`, `data\assets\`, `data\logs\`).

### Silent Deployment (Batch / Network Scripts)
To deploy across multiple cafe terminals silently:
```cmd
msiexec.exe /i "ChromaticMenu-Setup.msi" /qn
```

### Complete Uninstallation
Uninstalling completely cleans up all files, including runtime data, logs, and icons:
- **Via Start Menu**: Click `Start Menu > Chromatic Menu > Uninstall Chromatic Menu`.
- **Via Folder**: Run `C:\Program Files (x86)\Chromatic Menu\Uninstall.exe`.
- **Via Windows Settings**: Select `Chromatic Menu` in **Installed Apps** and click **Uninstall**.

---

## Publishing Updates

1. **Bump Version & Compile**:
   ```powershell
   .\build-msi.ps1 -Version "1.0.1"
   ```
2. **Create GitHub Release**:
   - Tag: `v1.0.1`
   - Attach: `bin\Release\ChromaticMenu-Setup.msi`
   - Publish the release.
3. **Client Pick-Up**:
   - Client machines check for updates via **Settings > About > Check for Updates**.
   - If Deep Freeze is **Thawed**, clicking **Download & Install Update** automatically applies the upgrade.

---

## License & Credits

Created and maintained by [HanazonoArchive](https://github.com/HanazonoArchive).

Distributed under the MIT License. Free for personal, commercial (cybercafe / piso-net), and organizational use.
