using System;
using System.Collections.Generic;
using System.Threading;
using ChromaticMenu.Services;
using ChromaticMenu.Shared;
using Newtonsoft.Json;

namespace ChromaticTelemetry
{
    internal sealed class HeartbeatRow
    {
        [JsonProperty("ts")]
        public DateTime Timestamp { get; set; }

        [JsonProperty("pc_name")]
        public string PcName { get; set; }

        [JsonProperty("menu_name")]
        public string MenuName { get; set; }

        [JsonProperty("program")]
        public string Program { get; set; }

        [JsonProperty("interval_seconds")]
        public int IntervalSeconds { get; set; }
    }

    // Sends one heartbeat per interval. Unsent rows stay in memory only (never on
    // disk) and are flushed oldest-first together with the next row.
    internal sealed class HeartbeatWorker : IDisposable
    {
        private static readonly TimeSpan MinProgramFreshness = TimeSpan.FromMinutes(3);

        private readonly LoggerService _log;
        private readonly SupabaseRestClient _client = new SupabaseRestClient();
        private readonly List<HeartbeatRow> _buffer = new List<HeartbeatRow>();
        private readonly object _bufferLock = new object();
        private readonly object _programLock = new object();
        private readonly Timer _timer;
        private readonly string _pcName;

        private ServiceSettings _settings;
        private int _tickRunning;
        private TimeSpan _clockOffset = TimeSpan.Zero;
        private string _program;
        private DateTime _programReceivedUtc = DateTime.MinValue;
        private bool _lastSendFailed;
        private bool _loggedDisabled;

        public HeartbeatWorker(LoggerService log, ServiceSettings settings)
        {
            _log = log;
            _settings = settings;
            _pcName = TelemetryDefaults.Truncate(Environment.MachineName, TelemetryDefaults.MaxNameLength, "Unknown PC");
            _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Start()
        {
            _timer.Change(TimeSpan.Zero, TimeSpan.FromSeconds(_settings.IntervalSeconds));
        }

        public void ApplySettings(ServiceSettings settings)
        {
            var previous = _settings;
            _settings = settings;
            if (previous.IntervalSeconds != settings.IntervalSeconds)
            {
                var interval = TimeSpan.FromSeconds(settings.IntervalSeconds);
                _timer.Change(interval, interval);
            }
            _log.Info($"Settings applied: sending={(settings.ShouldSend ? "on" : "off")} " +
                      $"(startWithWindows={settings.StartWithWindows}, telemetry={settings.TelemetryEnabled}, project={(settings.HasProject ? "set" : "not set")}), " +
                      $"interval={settings.IntervalSeconds}s, shop='{settings.MenuName}', url={settings.SupabaseUrl ?? "(none)"}");
            _loggedDisabled = false;
        }

        public void UpdateProgram(string programName)
        {
            lock (_programLock)
            {
                _program = TelemetryDefaults.Truncate(programName, TelemetryDefaults.MaxProgramLength, TelemetryDefaults.UnknownProgramName);
                _programReceivedUtc = DateTime.UtcNow;
            }
        }

        private string CurrentProgram(int intervalSeconds)
        {
            var freshness = TimeSpan.FromSeconds(intervalSeconds * 2);
            if (freshness < MinProgramFreshness) freshness = MinProgramFreshness;

            lock (_programLock)
            {
                if (_program == null || DateTime.UtcNow - _programReceivedUtc > freshness)
                {
                    return TelemetryDefaults.UnknownProgramName;
                }
                return _program;
            }
        }

        private void Tick()
        {
            if (Interlocked.Exchange(ref _tickRunning, 1) == 1) return;
            try
            {
                SendHeartbeat();
            }
            catch (Exception ex)
            {
                _log.Error("Heartbeat tick failed unexpectedly.", ex);
            }
            finally
            {
                Interlocked.Exchange(ref _tickRunning, 0);
            }
        }

        private void SendHeartbeat()
        {
            var settings = _settings;
            if (!settings.ShouldSend)
            {
                lock (_bufferLock)
                {
                    if (_buffer.Count > 0)
                    {
                        _log.Info($"Sending is off; discarded {_buffer.Count} buffered heartbeat(s).");
                        _buffer.Clear();
                    }
                }
                if (!_loggedDisabled)
                {
                    _log.Info("Sending is off (Start with Windows or telemetry disabled, or no Supabase URL/key set). Waiting for a reload.");
                    _loggedDisabled = true;
                }
                return;
            }

            var row = new HeartbeatRow
            {
                Timestamp = DateTime.UtcNow + _clockOffset,
                PcName = _pcName,
                MenuName = settings.MenuName,
                Program = CurrentProgram(settings.IntervalSeconds),
                IntervalSeconds = settings.IntervalSeconds
            };

            List<HeartbeatRow> payload;
            lock (_bufferLock)
            {
                payload = new List<HeartbeatRow>(_buffer) { row };
            }

            string json = JsonConvert.SerializeObject(payload, new JsonSerializerSettings
            {
                DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'"
            });

            // One attempt per tick; the insert itself is the "is Supabase reachable" check.
            var result = _client.InsertAsync(settings.SupabaseUrl, settings.SupabaseAnonKey, "heartbeats", json)
                                .GetAwaiter().GetResult();

            if (result.ServerDate.HasValue)
            {
                _clockOffset = result.ServerDate.Value.UtcDateTime - DateTime.UtcNow;
            }

            if (result.Success)
            {
                lock (_bufferLock)
                {
                    _buffer.Clear();
                }
                if (payload.Count > 1 || _lastSendFailed)
                {
                    _log.Info($"Flush OK, {payload.Count} row(s) sent.");
                }
                _lastSendFailed = false;
                return;
            }

            _lastSendFailed = true;

            if (result.IsPermanentDataError)
            {
                // Retrying rejected data would block every later heartbeat, so drop it.
                lock (_bufferLock)
                {
                    _buffer.Clear();
                }
                _log.Warn($"Insert rejected (HTTP {result.StatusCode}): {result.Error}. Dropped {payload.Count} row(s).");
                return;
            }

            int dropped = 0;
            int depth;
            lock (_bufferLock)
            {
                _buffer.Add(row);
                if (_buffer.Count > TelemetryDefaults.BufferCap)
                {
                    dropped = _buffer.Count - TelemetryDefaults.BufferCap;
                    _buffer.RemoveRange(0, dropped);
                }
                depth = _buffer.Count;
            }

            string status = result.StatusCode == 0 ? "no response" : "HTTP " + result.StatusCode;
            _log.Warn($"Insert failed ({status}): {result.Error}. Buffered rows: {depth}.");
            if (dropped > 0)
            {
                _log.Warn($"Buffer cap of {TelemetryDefaults.BufferCap} reached; dropped the {dropped} oldest row(s).");
            }
        }

        public void Dispose()
        {
            using (var stopped = new ManualResetEvent(false))
            {
                _timer.Dispose(stopped);
                stopped.WaitOne(TimeSpan.FromSeconds(5));
            }
            _client.Dispose();
        }
    }
}
