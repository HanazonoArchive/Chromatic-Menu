using System;
using System.IO;
using System.ServiceProcess;
using System.Threading;
using ChromaticMenu.Services;
using ChromaticMenu.Shared;

namespace ChromaticTelemetry
{
    public sealed class TelemetryService : ServiceBase
    {
        private static readonly TimeSpan ConfigRetryInterval = TimeSpan.FromMinutes(5);

        private readonly LoggerService _log = new LoggerService("telemetry");
        private string _configPath;
        private HeartbeatWorker _worker;
        private PipeServer _pipe;
        private UserSessionManager _sessionManager;
        private Timer _configRetryTimer;
        private string _lastConfigError;
        private DateTime _lastConfigErrorLogUtc = DateTime.MinValue;
        private readonly object _reloadLock = new object();

        public TelemetryService()
        {
            ServiceName = TelemetryDefaults.ServiceName;
            CanStop = true;
            CanShutdown = true;
            CanPauseAndContinue = false;
            AutoLog = false;
        }

        protected override void OnStart(string[] args)
        {
            string dataDir = ServiceSettings.FindDataDirectory();
            _log.ConfigureDirectory(dataDir);
            _configPath = Path.Combine(dataDir, "config.json");
            _log.Info($"ChromaticTelemetry starting on {Environment.MachineName}. Config: {_configPath}");

            var settings = LoadSettings();
            _worker = new HeartbeatWorker(_log, settings);
            _pipe = new PipeServer(_log, OnPipeMessage);
            _sessionManager = new UserSessionManager(_log, settings);

            _pipe.Start();
            _worker.Start();

            // If config.json was missing or broken at boot, keep retrying quietly.
            _configRetryTimer = new Timer(_ => RetryConfigIfBroken(), null, ConfigRetryInterval, ConfigRetryInterval);
        }

        private void OnPipeMessage(TelemetryPipeMessage message)
        {
            switch (message.Type)
            {
                case TelemetryPipeMessage.ProgramType:
                    _worker.UpdateProgram(message.Value);
                    break;
                case TelemetryPipeMessage.ReloadType:
                    Reload();
                    break;
            }
        }

        private void Reload()
        {
            lock (_reloadLock)
            {
                var settings = LoadSettings();
                _worker.ApplySettings(settings);
                _sessionManager?.ApplySettings(settings);
            }
        }

        private void RetryConfigIfBroken()
        {
            if (_lastConfigError == null) return;
            Reload();
        }

        private ServiceSettings LoadSettings()
        {
            var settings = ServiceSettings.Load(_configPath, out string error);
            if (error == null)
            {
                if (_lastConfigError != null)
                {
                    _log.Info("config.json is readable again.");
                }
                _lastConfigError = null;
                return settings;
            }

            // Log the same config problem at most once per retry interval.
            if (error != _lastConfigError || DateTime.UtcNow - _lastConfigErrorLogUtc >= ConfigRetryInterval)
            {
                _log.Warn(error + " Telemetry stays off until it can be read.");
                _lastConfigErrorLogUtc = DateTime.UtcNow;
            }
            _lastConfigError = error;
            return settings;
        }

        protected override void OnStop()
        {
            _log.Info("ChromaticTelemetry stopping.");
            _configRetryTimer?.Dispose();
            _sessionManager?.Dispose();
            _pipe?.Dispose();
            _worker?.Dispose();
        }

        protected override void OnShutdown()
        {
            OnStop();
        }
    }
}
