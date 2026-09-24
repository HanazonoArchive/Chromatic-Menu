using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using ChromaticMenu.Services;
using Microsoft.Win32;

namespace ChromaticTelemetry
{
    // Supervises the independent user-session background reporter from Session 0.
    // When telemetry is enabled, it ensures the reporter is running in the active user session.
    // When telemetry is disabled or during service stop, it terminates the reporter process.
    internal sealed class UserSessionManager : IDisposable
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "ChromaticTelemetryReporter";

        private readonly LoggerService _log;
        private volatile ServiceSettings _settings;
        private readonly Timer _watchTimer;
        private readonly int _ownProcessId = Process.GetCurrentProcess().Id;
        private int _checkRunning;

        #region Native Methods
        private static class NativeMethods
        {
            [DllImport("kernel32.dll")]
            public static extern uint WTSGetActiveConsoleSessionId();

            [DllImport("wtsapi32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool WTSQueryUserToken(uint sessionId, out IntPtr phToken);

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Auto)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DuplicateTokenEx(
                IntPtr hExistingToken,
                uint dwDesiredAccess,
                IntPtr lpTokenAttributes,
                int ImpersonationLevel,
                int TokenType,
                out IntPtr phNewToken);

            [DllImport("userenv.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CreateEnvironmentBlock(out IntPtr lpEnvironment, IntPtr hToken, bool bInherit);

            [DllImport("userenv.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool DestroyEnvironmentBlock(IntPtr lpEnvironment);

            [DllImport("kernel32.dll", SetLastError = true)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CloseHandle(IntPtr hObject);

            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            public struct STARTUPINFO
            {
                public int cb;
                public string lpReserved;
                public string lpDesktop;
                public string lpTitle;
                public int dwX;
                public int dwY;
                public int dwXSize;
                public int dwYSize;
                public int dwXCountChars;
                public int dwYCountChars;
                public int dwFillAttribute;
                public int dwFlags;
                public short wShowWindow;
                public short cbReserved2;
                public IntPtr lpReserved2;
                public IntPtr hStdInput;
                public IntPtr hStdOutput;
                public IntPtr hStdError;
            }

            [StructLayout(LayoutKind.Sequential)]
            public struct PROCESS_INFORMATION
            {
                public IntPtr hProcess;
                public IntPtr hThread;
                public int dwProcessId;
                public int dwThreadId;
            }

            [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
            [return: MarshalAs(UnmanagedType.Bool)]
            public static extern bool CreateProcessAsUser(
                IntPtr hToken,
                string lpApplicationName,
                string lpCommandLine,
                IntPtr lpProcessAttributes,
                IntPtr lpThreadAttributes,
                bool bInheritHandles,
                uint dwCreationFlags,
                IntPtr lpEnvironment,
                string lpCurrentDirectory,
                ref STARTUPINFO lpStartupInfo,
                out PROCESS_INFORMATION lpProcessInformation);
        }
        #endregion

        public UserSessionManager(LoggerService log, ServiceSettings settings)
        {
            _log = log;
            _settings = settings;
            _watchTimer = new Timer(_ => CheckSession(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(10));
        }

        public void ApplySettings(ServiceSettings settings)
        {
            _settings = settings;
            CheckSession();
        }

        private void CheckSession()
        {
            if (Interlocked.Exchange(ref _checkRunning, 1) == 1) return;
            try
            {
                var settings = _settings;
                if (!settings.ShouldSend)
                {
                    // Telemetry disabled: stop any running reporters and remove startup entry
                    KillUserSessionReporters();
                    SetRunKey(false);
                    return;
                }

                // Telemetry enabled: ensure startup entry is present
                SetRunKey(true);

                uint activeSession = NativeMethods.WTSGetActiveConsoleSessionId();
                if (activeSession == 0xFFFFFFFF || activeSession == 0)
                {
                    return; // No active interactive console session
                }

                // Check if a reporter process is already running in that session
                bool isRunning = Process.GetProcessesByName("ChromaticTelemetry")
                    .Any(p => p.SessionId == (int)activeSession && p.Id != _ownProcessId);

                if (!isRunning)
                {
                    LaunchReporterInSession(activeSession);
                }
            }
            catch (Exception ex)
            {
                _log.Warn("Session watcher check failed: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _checkRunning, 0);
            }
        }

        private void LaunchReporterInSession(uint sessionId)
        {
            IntPtr userToken = IntPtr.Zero;
            IntPtr primaryToken = IntPtr.Zero;
            IntPtr env = IntPtr.Zero;
            try
            {
                if (!NativeMethods.WTSQueryUserToken(sessionId, out userToken))
                {
                    return; // Session may not be fully logged on or locked
                }

                // Duplicate token into primary token
                if (!NativeMethods.DuplicateTokenEx(userToken, 0x10000000 /* MAXIMUM_ALLOWED */, IntPtr.Zero, 2 /* SecurityImpersonation */, 1 /* TokenPrimary */, out primaryToken))
                {
                    return;
                }

                NativeMethods.CreateEnvironmentBlock(out env, primaryToken, false);

                string exePath = Assembly.GetExecutingAssembly().Location;
                string workingDir = Path.GetDirectoryName(exePath);

                var si = new NativeMethods.STARTUPINFO();
                si.cb = Marshal.SizeOf(typeof(NativeMethods.STARTUPINFO));
                si.lpDesktop = @"winsta0\default";

                NativeMethods.PROCESS_INFORMATION pi;
                bool ok = NativeMethods.CreateProcessAsUser(
                    primaryToken,
                    exePath,
                    $"\"{exePath}\" --reporter",
                    IntPtr.Zero,
                    IntPtr.Zero,
                    false,
                    0x00000400 /* CREATE_UNICODE_ENVIRONMENT */ | 0x08000000 /* CREATE_NO_WINDOW */,
                    env,
                    workingDir,
                    ref si,
                    out pi);

                if (ok)
                {
                    _log.Info($"Started background telemetry reporter in user session {sessionId} (PID {pi.dwProcessId}).");
                    NativeMethods.CloseHandle(pi.hProcess);
                    NativeMethods.CloseHandle(pi.hThread);
                }
            }
            catch (Exception ex)
            {
                _log.Warn($"Failed to launch reporter in session {sessionId}: {ex.Message}");
            }
            finally
            {
                if (env != IntPtr.Zero) NativeMethods.DestroyEnvironmentBlock(env);
                if (primaryToken != IntPtr.Zero) NativeMethods.CloseHandle(primaryToken);
                if (userToken != IntPtr.Zero) NativeMethods.CloseHandle(userToken);
            }
        }

        private void KillUserSessionReporters()
        {
            try
            {
                var procs = Process.GetProcessesByName("ChromaticTelemetry")
                    .Where(p => p.Id != _ownProcessId && p.SessionId > 0);

                foreach (var p in procs)
                {
                    try
                    {
                        p.Kill();
                        _log.Info($"Stopped user-session reporter PID {p.Id} because telemetry is disabled.");
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void SetRunKey(bool enable)
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exePath = Assembly.GetExecutingAssembly().Location;
                        string value = $"\"{exePath}\" --reporter";
                        if (!string.Equals(key.GetValue(RunValueName) as string, value, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(RunValueName, value);
                        }
                    }
                    else
                    {
                        if (key.GetValue(RunValueName) != null)
                        {
                            key.DeleteValue(RunValueName, false);
                        }
                    }
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _watchTimer?.Dispose();
            KillUserSessionReporters();
        }
    }
}
