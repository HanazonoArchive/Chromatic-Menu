using System;
using System.IO;
using System.Text;

namespace ChromaticMenu.Services
{
    public class LoggerService
    {
        private static readonly object _lock = new object();
        private static LoggerService _instance;
        public static LoggerService Instance => _instance ?? (_instance = new LoggerService());

        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private const long MaxLogFileSizeBytes = 1024 * 1024; // 1 MB
        private const int MaxBackupFiles = 3;

        public string LogFilePath => _logFilePath;

        public LoggerService()
        {
            _logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "logs");
            _logFilePath = Path.Combine(_logDirectory, "launcher.log");
        }

        public void Info(string message) => Log("INFO", message, null);
        public void Warn(string message) => Log("WARN", message, null);
        public void Error(string message, Exception ex = null) => Log("ERROR", message, ex);

        private void Log(string level, string message, Exception ex)
        {
            try
            {
                lock (_lock)
                {
                    if (!Directory.Exists(_logDirectory))
                    {
                        Directory.CreateDirectory(_logDirectory);
                    }

                    RotateIfNeeded();

                    var sb = new StringBuilder();
                    sb.AppendFormat("[{0:yyyy-MM-dd HH:mm:ss.fff}] [{1}] {2}", DateTime.Now, level, message);
                    if (ex != null)
                    {
                        sb.AppendLine();
                        sb.AppendFormat("Exception: {0}: {1}", ex.GetType().FullName, ex.Message);
                        sb.AppendLine();
                        sb.Append(ex.StackTrace);
                    }
                    sb.AppendLine();

                    File.AppendAllText(_logFilePath, sb.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Never crash if logging fails
            }
        }

        private void RotateIfNeeded()
        {
            if (!File.Exists(_logFilePath)) return;

            var fileInfo = new FileInfo(_logFilePath);
            if (fileInfo.Length < MaxLogFileSizeBytes) return;

            try
            {
                // Rotating: launcher.2.log -> deleted, launcher.1.log -> launcher.2.log, launcher.log -> launcher.1.log
                string backup2 = Path.Combine(_logDirectory, "launcher.2.log");
                string backup1 = Path.Combine(_logDirectory, "launcher.1.log");

                if (File.Exists(backup2))
                {
                    File.Delete(backup2);
                }

                if (File.Exists(backup1))
                {
                    File.Move(backup1, backup2);
                }

                File.Move(_logFilePath, backup1);
            }
            catch
            {
                // If file locking prevents rotation, proceed to append
            }
        }
    }
}
