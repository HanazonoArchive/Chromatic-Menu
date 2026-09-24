using System;
using System.ServiceProcess;

namespace ChromaticTelemetry
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            if (args != null && args.Length > 0 && string.Equals(args[0], "--reporter", StringComparison.OrdinalIgnoreCase))
            {
                UserSessionReporter.Run();
            }
            else
            {
                ServiceBase.Run(new TelemetryService());
            }
        }
    }
}
