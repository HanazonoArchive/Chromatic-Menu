using System.Collections.ObjectModel;
using ChromaticMenu.ViewModels;

namespace ChromaticMenu.Models
{
    public class GpuInfoModel
    {
        public string Name { get; set; } = "N/A";
        public string Tag { get; set; } = "Dedicated"; // Dedicated or Integrated
        public string DisplayName => $"{Name} ({Tag})";
        public string VramString { get; set; } = "N/A";
    }

    public class DiskInfoModel
    {
        public string DriveLetter { get; set; } = "C:";
        public string DiskType { get; set; } = "Disk"; // SSD, HDD, NVMe, Disk
        public long UsedBytes { get; set; }
        public long TotalBytes { get; set; }

        public string DisplayString
        {
            get
            {
                double usedGb = UsedBytes / (1024.0 * 1024 * 1024);
                double totalGb = TotalBytes / (1024.0 * 1024 * 1024);
                return $"{DriveLetter} {DiskType}  {usedGb:F2} GB / {totalGb:F2} GB";
            }
        }
    }

    public class SystemInfoModel : ObservableObject
    {
        private string _computerName = "N/A";
        private string _cpuName = "N/A";
        private string _osVersion = "N/A";
        private string _coresAndThreads = "N/A";
        private string _ramDisplay = "N/A";
        private string _networkName = "Ethernet";
        private string _networkThroughput = "Not connected";

        public string ComputerName
        {
            get => _computerName;
            set => SetProperty(ref _computerName, value);
        }

        public string CpuName
        {
            get => _cpuName;
            set => SetProperty(ref _cpuName, value);
        }

        public string OsVersion
        {
            get => _osVersion;
            set => SetProperty(ref _osVersion, value);
        }

        public string CoresAndThreads
        {
            get => _coresAndThreads;
            set => SetProperty(ref _coresAndThreads, value);
        }

        public string RamDisplay
        {
            get => _ramDisplay;
            set => SetProperty(ref _ramDisplay, value);
        }

        public string NetworkName
        {
            get => _networkName;
            set => SetProperty(ref _networkName, value);
        }

        public string NetworkThroughput
        {
            get => _networkThroughput;
            set => SetProperty(ref _networkThroughput, value);
        }

        public ObservableCollection<DiskInfoModel> Disks { get; } = new ObservableCollection<DiskInfoModel>();
        public ObservableCollection<GpuInfoModel> Gpus { get; } = new ObservableCollection<GpuInfoModel>();
    }
}
