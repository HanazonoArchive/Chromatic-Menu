using System.ServiceProcess;

namespace ChromaticTelemetry
{
    internal static class Program
    {
        private static void Main()
        {
            ServiceBase.Run(new TelemetryService());
        }
    }
}
