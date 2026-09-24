using System;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ChromaticMenu.Services;
using ChromaticMenu.Shared;
using Newtonsoft.Json;

namespace ChromaticTelemetry
{
    // Receives newline-delimited JSON messages from the launcher in the user session.
    // Runs on its own task so a slow or stuck client never delays heartbeats.
    internal sealed class PipeServer : IDisposable
    {
        private const int MaxLineLength = 4096;

        private readonly LoggerService _log;
        private readonly Action<TelemetryPipeMessage> _onMessage;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private NamedPipeServerStream _current;
        private Task _loop;
        private DateTime _lastErrorLogUtc = DateTime.MinValue;

        public PipeServer(LoggerService log, Action<TelemetryPipeMessage> onMessage)
        {
            _log = log;
            _onMessage = onMessage;
        }

        public void Start()
        {
            _loop = Task.Run(() => RunAsync(_cts.Token));
        }

        private static NamedPipeServerStream CreatePipe()
        {
            // A pipe created by LocalSystem is not writable by normal users unless
            // the ACL says so, and the launcher runs as a standard user.
            var security = new PipeSecurity();
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
                PipeAccessRights.ReadWrite, AccessControlType.Allow));
            security.AddAccessRule(new PipeAccessRule(
                new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
                PipeAccessRights.FullControl, AccessControlType.Allow));

            return new NamedPipeServerStream(TelemetryDefaults.PipeName, PipeDirection.In, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, security);
        }

        private async Task RunAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using (var pipe = CreatePipe())
                    {
                        _current = pipe;
                        await pipe.WaitForConnectionAsync(token).ConfigureAwait(false);
                        using (var reader = new StreamReader(pipe, Encoding.UTF8))
                        {
                            string line;
                            while (!token.IsCancellationRequested &&
                                   (line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                HandleLine(line);
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    LogRateLimited("Pipe server error: " + ex.Message);
                    try
                    {
                        await Task.Delay(1000, token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
                finally
                {
                    _current = null;
                }
            }
        }

        private void HandleLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            if (line.Length > MaxLineLength)
            {
                LogRateLimited("Ignored an oversized pipe message.");
                return;
            }

            TelemetryPipeMessage message;
            try
            {
                message = JsonConvert.DeserializeObject<TelemetryPipeMessage>(line);
            }
            catch (Exception ex)
            {
                LogRateLimited("Ignored a malformed pipe message: " + ex.Message);
                return;
            }

            if (message?.Type == null) return;
            _onMessage(message);
        }

        private void LogRateLimited(string text)
        {
            if (DateTime.UtcNow - _lastErrorLogUtc < TimeSpan.FromMinutes(1)) return;
            _lastErrorLogUtc = DateTime.UtcNow;
            _log.Warn(text);
        }

        public void Dispose()
        {
            _cts.Cancel();
            try
            {
                // ReadLineAsync cannot be cancelled; closing the pipe unblocks it.
                _current?.Dispose();
                _loop?.Wait(TimeSpan.FromSeconds(3));
            }
            catch (AggregateException)
            {
                // The loop already logged its own failures.
            }
            _cts.Dispose();
        }
    }
}
