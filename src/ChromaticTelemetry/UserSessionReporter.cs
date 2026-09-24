using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using ChromaticMenu.Services;
using ChromaticMenu.Shared;

namespace ChromaticTelemetry
{
    // Headless background process that runs in the interactive user desktop session.
    // Watches the foreground window and feeds the active program to the Windows Service
    // via the local named pipe, even when Chromatic Menu.exe is closed.
    // Respects Chromatic Menu settings: if telemetry is disabled, it exits immediately.
    internal static class UserSessionReporter
    {
        private const string MutexName = @"Global\ChromaticTelemetry_Reporter_Mutex";
        private static readonly TimeSpan ReconnectInterval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(30);

        public static void Run()
        {
            // 1. Single-instance check per session/machine
            bool createdNew;
            Mutex mutex;
            try
            {
                mutex = new Mutex(true, MutexName, out createdNew);
            }
            catch
            {
                return;
            }

            if (!createdNew)
            {
                return;
            }

            string dataDir = ServiceSettings.FindDataDirectory();
            string configPath = Path.Combine(dataDir, "config.json");

            // 2. Check if telemetry is enabled in config
            var settings = ServiceSettings.Load(configPath, out _);
            if (!settings.ShouldSend)
            {
                return; // Disabled in settings -> exit immediately
            }

            var log = new LoggerService("reporter");
            log.ConfigureDirectory(dataDir);
            log.Info($"Independent telemetry reporter started in session {Process.GetCurrentProcess().SessionId} (PID {Process.GetCurrentProcess().Id}).");

            var detector = new ForegroundDetector();
            detector.ReloadMenuTargets(configPath);

            string lastSentProgram = null;
            DateTime lastSentUtc = DateTime.MinValue;
            DateTime lastConfigCheckUtc = DateTime.UtcNow;
            DateTime lastPipeAttemptUtc = DateTime.MinValue;
            NamedPipeClientStream pipe = null;
            StreamWriter writer = null;

            try
            {
                while (true)
                {
                    // Every 5 seconds, verify config has not disabled telemetry
                    if ((DateTime.UtcNow - lastConfigCheckUtc).TotalSeconds >= 5)
                    {
                        lastConfigCheckUtc = DateTime.UtcNow;
                        var currentSettings = ServiceSettings.Load(configPath, out _);
                        if (!currentSettings.ShouldSend)
                        {
                            log.Info("Telemetry disabled in config; independent reporter exiting.");
                            break;
                        }
                    }

                    string program = detector.Detect(configPath);
                    bool changed = program != lastSentProgram;
                    bool keepAlive = (DateTime.UtcNow - lastSentUtc) >= KeepAliveInterval;

                    if (changed)
                    {
                        log.Info($"Active program: '{program}'");
                    }

                    // Attempt pipe reconnection if needed (rate-limited)
                    if (pipe == null || !pipe.IsConnected)
                    {
                        if ((DateTime.UtcNow - lastPipeAttemptUtc) >= ReconnectInterval)
                        {
                            lastPipeAttemptUtc = DateTime.UtcNow;
                            try
                            {
                                writer?.Dispose();
                                pipe?.Dispose();
                                pipe = new NamedPipeClientStream(".", TelemetryDefaults.PipeName, PipeDirection.Out, PipeOptions.Asynchronous);
                                pipe.Connect(250);
                                writer = new StreamWriter(pipe, Encoding.UTF8) { AutoFlush = true };
                            }
                            catch
                            {
                                pipe = null;
                                writer = null;
                            }
                        }
                    }

                    // Send to pipe if connected and program changed or keepalive due
                    if (writer != null && pipe != null && pipe.IsConnected)
                    {
                        if (changed || keepAlive)
                        {
                            try
                            {
                                var msg = TelemetryPipeMessage.ForProgram(program);
                                string json = Newtonsoft.Json.JsonConvert.SerializeObject(msg);
                                writer.WriteLine(json);
                                lastSentProgram = program;
                                lastSentUtc = DateTime.UtcNow;
                            }
                            catch (Exception ex)
                            {
                                log.Warn("Pipe send error: " + ex.Message);
                                writer?.Dispose();
                                pipe?.Dispose();
                                pipe = null;
                                writer = null;
                            }
                        }
                    }
                    else if (changed)
                    {
                        // Even if pipe is momentarily disconnected, record the change
                        lastSentProgram = program;
                    }

                    Thread.Sleep(1000);
                }
            }
            catch (Exception ex)
            {
                log.Warn("Independent reporter loop terminated: " + ex.Message);
            }
            finally
            {
                writer?.Dispose();
                pipe?.Dispose();
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Dispose();
            }
        }
    }
}
