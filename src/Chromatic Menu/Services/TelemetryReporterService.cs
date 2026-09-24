using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChromaticMenu.Models;
using ChromaticMenu.Native;
using ChromaticMenu.Shared;
using Newtonsoft.Json;

namespace ChromaticMenu.Services
{
    // Tells the ChromaticTelemetry service which program is in the foreground.
    // A Session 0 service cannot see the user's desktop, so the launcher reports it.
    // Only a friendly program name is ever sent: never window titles or paths.
    public sealed class TelemetryReporterService : IDisposable
    {
        private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan ReloadDebounce = TimeSpan.FromSeconds(2);
        private static readonly string[] ShellWindowClasses = { "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd" };

        private static TelemetryReporterService _instance;
        public static TelemetryReporterService Instance => _instance ?? (_instance = new TelemetryReporterService());

        private readonly object _pipeLock = new object();
        private readonly Dictionary<string, string> _descriptionCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _shortcutTargetCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly uint _ownProcessId = (uint)Process.GetCurrentProcess().Id;
        private readonly string _windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows).TrimEnd('\\') + "\\";

        private Timer _pollTimer;
        private Timer _reloadTimer;
        private int _pollRunning;
        private volatile Dictionary<string, string> _menuTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private NamedPipeClientStream _pipe;
        private StreamWriter _writer;
        private bool? _lastConnectOk;
        private string _lastSentProgram;
        private DateTime _lastSentUtc = DateTime.MinValue;

        public void Start()
        {
            if (_pollTimer != null) return;
            _pollTimer = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), PollInterval);
            _reloadTimer = new Timer(_ => Send(TelemetryPipeMessage.ForReload()), null, Timeout.Infinite, Timeout.Infinite);
        }

        // Coalesces bursts of saves (e.g. typing the shop name) into one reload.
        public void RequestReload()
        {
            _reloadTimer?.Change(ReloadDebounce, Timeout.InfiniteTimeSpan);
        }

        // Rebuilds the exe-path to menu-name map in the background, because
        // resolving .lnk targets goes through COM and must stay off the UI thread.
        public void UpdateMenuItems(IEnumerable<ProgramItem> items)
        {
            var snapshot = items.Select(i => new { i.Kind, i.Target, i.Name }).ToList();
            Task.Run(() =>
            {
                var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var item in snapshot)
                {
                    string path = ResolveItemExecutable(item.Kind, item.Target);
                    if (!string.IsNullOrEmpty(path) && !map.ContainsKey(path))
                    {
                        map[path] = item.Name;
                    }
                }
                _menuTargets = map;
            });
        }

        private string ResolveItemExecutable(string kind, string target)
        {
            if (string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kind, "folder", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                string resolved = IconService.ResolveExecutablePath(target);
                if (string.Equals(kind, "lnk", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_shortcutTargetCache)
                    {
                        if (!_shortcutTargetCache.TryGetValue(resolved, out string lnkTarget))
                        {
                            lnkTarget = IconService.Instance.ResolveShortcutTarget(resolved);
                            _shortcutTargetCache[resolved] = lnkTarget;
                        }
                        resolved = lnkTarget;
                    }
                }
                return string.IsNullOrEmpty(resolved) ? null : Path.GetFullPath(resolved);
            }
            catch
            {
                return null;
            }
        }

        private void Poll()
        {
            if (Interlocked.Exchange(ref _pollRunning, 1) == 1) return;
            try
            {
                string program = GetForegroundProgramName();
                if (program != _lastSentProgram || DateTime.UtcNow - _lastSentUtc >= ResendInterval)
                {
                    if (Send(TelemetryPipeMessage.ForProgram(program)))
                    {
                        _lastSentProgram = program;
                        _lastSentUtc = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Telemetry reporter poll failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _pollRunning, 0);
            }
        }

        public string GetForegroundProgramName()
        {
            IntPtr hwnd = NativeMethods.GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return TelemetryDefaults.LauncherProgramName;

            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == 0 || pid == _ownProcessId) return TelemetryDefaults.LauncherProgramName;

            var className = new StringBuilder(64);
            if (NativeMethods.GetClassName(hwnd, className, className.Capacity) > 0 &&
                ShellWindowClasses.Contains(className.ToString()))
            {
                return TelemetryDefaults.LauncherProgramName;
            }

            string exePath = GetProcessPath(pid);
            if (exePath == null) return TelemetryDefaults.UnknownProgramName;

            if (exePath.StartsWith(_windowsDirectory, StringComparison.OrdinalIgnoreCase))
            {
                return TelemetryDefaults.WindowsProgramName;
            }

            if (_menuTargets.TryGetValue(exePath, out string menuName))
            {
                return TelemetryDefaults.Truncate(menuName, TelemetryDefaults.MaxProgramLength, TelemetryDefaults.UnknownProgramName);
            }

            return DescribeExecutable(exePath);
        }

        private static string GetProcessPath(uint pid)
        {
            IntPtr handle = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (handle == IntPtr.Zero) return null;
            try
            {
                var buffer = new StringBuilder(1024);
                uint size = (uint)buffer.Capacity;
                return NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size) ? buffer.ToString() : null;
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }

        private string DescribeExecutable(string exePath)
        {
            lock (_descriptionCache)
            {
                if (_descriptionCache.TryGetValue(exePath, out string cached)) return cached;
            }

            string name = null;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(exePath);
                name = !string.IsNullOrWhiteSpace(info.FileDescription) ? info.FileDescription : info.ProductName;
            }
            catch
            {
                // Unreadable version resource; the file name is used instead.
            }

            if (string.IsNullOrWhiteSpace(name))
            {
                name = Path.GetFileNameWithoutExtension(exePath);
            }
            name = TelemetryDefaults.Truncate(name, TelemetryDefaults.MaxProgramLength, TelemetryDefaults.UnknownProgramName);

            lock (_descriptionCache)
            {
                _descriptionCache[exePath] = name;
            }
            return name;
        }

        private bool Send(TelemetryPipeMessage message)
        {
            string line = JsonConvert.SerializeObject(message);
            lock (_pipeLock)
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    if (!EnsureConnected()) return false;
                    try
                    {
                        _writer.WriteLine(line);
                        return true;
                    }
                    catch (Exception)
                    {
                        // The service restarted or dropped the connection; reconnect once.
                        ClosePipe();
                    }
                }
                return false;
            }
        }

        private bool EnsureConnected()
        {
            if (_pipe != null && _pipe.IsConnected) return true;
            ClosePipe();
            try
            {
                _pipe = new NamedPipeClientStream(".", TelemetryDefaults.PipeName, PipeDirection.Out);
                _pipe.Connect(500);
                _writer = new StreamWriter(_pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                LogConnectionState(true, null);
                return true;
            }
            catch (Exception ex)
            {
                ClosePipe();
                LogConnectionState(false, ex.Message);
                return false;
            }
        }

        private void LogConnectionState(bool connected, string error)
        {
            if (_lastConnectOk == connected) return;
            _lastConnectOk = connected;
            if (connected)
            {
                LoggerService.Instance.Info("Connected to the ChromaticTelemetry service.");
            }
            else
            {
                LoggerService.Instance.Info("ChromaticTelemetry service is not reachable: " + error);
            }
        }

        private void ClosePipe()
        {
            try { _writer?.Dispose(); } catch { }
            try { _pipe?.Dispose(); } catch { }
            _writer = null;
            _pipe = null;
        }

        public void Dispose()
        {
            _pollTimer?.Dispose();
            _reloadTimer?.Dispose();
            _pollTimer = null;
            _reloadTimer = null;
            lock (_pipeLock)
            {
                ClosePipe();
            }
        }
    }
}
