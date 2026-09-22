using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using ChromaticMenu.Models;
using ChromaticMenu.Native;
using Microsoft.Win32;

namespace ChromaticMenu.Services
{
    public class SystemInfoSnapshot
    {
        public string ComputerName { get; set; } = "N/A";
        public string CpuName { get; set; } = "N/A";
        public string OsVersion { get; set; } = "N/A";
        public string CoresAndThreads { get; set; } = "N/A";
        public ulong InstalledRamBytes { get; set; }
        public List<GpuInfoModel> Gpus { get; set; } = new List<GpuInfoModel>();

        // Live fields
        public string RamDisplay { get; set; } = "N/A";
        public List<DiskInfoModel> Disks { get; set; } = new List<DiskInfoModel>();
        public string NetworkName { get; set; } = "Ethernet";
        public string NetworkThroughput { get; set; } = "Not connected";
    }

    public class SystemInfoService
    {
        private static SystemInfoService _instance;
        public static SystemInfoService Instance => _instance ?? (_instance = new SystemInfoService());

        private Timer _liveTimer;
        private Action<SystemInfoSnapshot> _onUpdateCallback;

        // Cached static data
        private string _computerName = "N/A";
        private string _cpuName = "N/A";
        private string _osVersion = "N/A";
        private string _coresAndThreads = "N/A";
        private ulong _installedRamBytes = 0;
        private List<GpuInfoModel> _cachedGpus = new List<GpuInfoModel>();

        // Disk type cache (DriveLetter -> Type string, e.g. "C" -> "SSD")
        private readonly Dictionary<string, string> _cachedDiskTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string _cachedDriveLettersKey = string.Empty;

        // Network throughput calculation state
        private string _lastAdapterId;
        private long _lastBytesSent;
        private long _lastBytesReceived;
        private DateTime _lastNetworkSampleTime = DateTime.MinValue;
        private bool _hasLoggedFirstSnapshot = false;

        public SystemInfoService()
        {
        }

        public void Initialize()
        {
            // Gather all static values on background thread
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    LoadStaticInfo();
                }
                catch (Exception ex)
                {
                    LoggerService.Instance.Error("Failed to load static system info.", ex);
                }
            });
        }

        public void StartMonitoring(Action<SystemInfoSnapshot> onUpdate)
        {
            _onUpdateCallback = onUpdate;

            // Run initial sample immediately
            ThreadPool.QueueUserWorkItem(_ =>
            {
                if (string.IsNullOrEmpty(_computerName) || _computerName == "N/A")
                {
                    LoadStaticInfo();
                }
                PerformLiveUpdate();
            });

            // 3-second live refresh cycle per SPEC section 7 / user request
            _liveTimer = new Timer(_ => PerformLiveUpdate(), null, 3000, 3000);
        }

        public void StopMonitoring()
        {
            _liveTimer?.Dispose();
            _liveTimer = null;
        }

        private void LoadStaticInfo()
        {
            // 1. Computer Name
            try
            {
                _computerName = Environment.MachineName;
            }
            catch
            {
                _computerName = "N/A";
            }

            // 2. CPU: Cleaned name + base clock per SPEC section 7
            try
            {
                _cpuName = GetCleanedCpuName();
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read CPU info: " + ex.Message);
                _cpuName = "N/A";
            }

            // 3. OS: Universal Windows 10 and Windows 11 detection from registry and OSVersion
            try
            {
                _osVersion = GetOsVersion();
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read OS version: " + ex.Message);
                _osVersion = "N/A";
            }

            // 4. Cores / Threads sum across sockets
            try
            {
                _coresAndThreads = GetCoresAndThreads();
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read CPU cores/threads: " + ex.Message);
                _coresAndThreads = "N/A";
            }

            // 5. Installed RAM (sum of Win32_PhysicalMemory.Capacity)
            try
            {
                _installedRamBytes = GetTotalInstalledRamBytes();
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read installed RAM capacity: " + ex.Message);
                _installedRamBytes = 0;
            }

            // 6. Real GPUs: Dedicated first, up to 2 GPUs, 64-bit safe VRAM
            try
            {
                _cachedGpus = GetGpuList();
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read GPU list: " + ex.Message);
                _cachedGpus = new List<GpuInfoModel>();
            }
        }

        private void PerformLiveUpdate()
        {
            try
            {
                var snapshot = new SystemInfoSnapshot
                {
                    ComputerName = _computerName,
                    CpuName = _cpuName,
                    OsVersion = _osVersion,
                    CoresAndThreads = _coresAndThreads,
                    InstalledRamBytes = _installedRamBytes,
                    Gpus = new List<GpuInfoModel>(_cachedGpus)
                };

                // 1. Live RAM
                snapshot.RamDisplay = GetLiveRamString(_installedRamBytes);

                // 2. Live Disks
                snapshot.Disks = GetLiveDisks();

                // 3. Live Network
                var (netName, netThroughput) = GetLiveNetworkThroughput();
                snapshot.NetworkName = netName;
                snapshot.NetworkThroughput = netThroughput;

                // Dispatch snapshot to subscriber
                _onUpdateCallback?.Invoke(snapshot);

                if (!_hasLoggedFirstSnapshot)
                {
                    _hasLoggedFirstSnapshot = true;
                    LoggerService.Instance.Info(string.Format("SystemInfo: Computer={0}, CPU={1}, OS={2}, CT={3}, RAM={4}, Net={5} ({6}), Disks=[{7}], GPUs=[{8}]",
                        snapshot.ComputerName,
                        snapshot.CpuName,
                        snapshot.OsVersion,
                        snapshot.CoresAndThreads,
                        snapshot.RamDisplay,
                        snapshot.NetworkName,
                        snapshot.NetworkThroughput,
                        string.Join("; ", snapshot.Disks.Select(d => d.DisplayString)),
                        string.Join("; ", snapshot.Gpus.Select(g => g.DisplayName + " " + g.VramString))));
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Error("Error during live system info update.", ex);
            }
        }

        #region CPU Name & Base Clock Cleaning

        private string GetCleanedCpuName()
        {
            string rawName = string.Empty;
            int mhz = 0;

            using (var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0"))
            {
                if (key != null)
                {
                    rawName = key.GetValue("ProcessorNameString") as string ?? string.Empty;
                    object mhzVal = key.GetValue("~MHz");
                    if (mhzVal is int intMhz)
                    {
                        mhz = intMhz;
                    }
                }
            }

            if (string.IsNullOrEmpty(rawName))
            {
                using (var searcher = new ManagementObjectSearcher("SELECT Name, MaxClockSpeed FROM Win32_Processor"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        rawName = mo["Name"]?.ToString() ?? string.Empty;
                        if (mhz == 0 && mo["MaxClockSpeed"] != null)
                        {
                            mhz = Convert.ToInt32(mo["MaxClockSpeed"]);
                        }
                        break;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(rawName)) return "N/A";

            // Strip vendor text such as "AMD", "(R)", "(TM)", "Processor", "6-Core", "8-Core", "CPU @ ...", etc.
            string cleaned = rawName;
            cleaned = Regex.Replace(cleaned, @"\bAMD\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bIntel\b", "", RegexOptions.IgnoreCase);
            cleaned = cleaned.Replace("(R)", "").Replace("(r)", "").Replace("(TM)", "").Replace("(tm)", "");
            cleaned = Regex.Replace(cleaned, @"\b\d+-Core\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bProcessor\b", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\bCPU\s*@\s*[\d\.]+\s*[GgMm][Hh][Zz]", "", RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();

            // Format base clock, e.g. 3593 MHz -> 3.6 GHz
            string clockString = string.Empty;
            if (mhz > 0)
            {
                double ghz = mhz / 1000.0;
                clockString = $"  {ghz:F1} GHz";
            }

            return cleaned + clockString;
        }

        #endregion

        #region OS Version

        private string GetOsVersion()
        {
            string productName = null;
            string currentBuild = null;

            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (key != null)
                    {
                        productName = key.GetValue("ProductName") as string;
                        currentBuild = key.GetValue("CurrentBuild") as string ?? key.GetValue("CurrentBuildNumber") as string;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Failed to read OS version from registry: " + ex.Message);
            }

            // Fallback to Environment.OSVersion if registry did not provide a build number
            if (string.IsNullOrWhiteSpace(currentBuild))
            {
                int envBuild = Environment.OSVersion.Version.Build;
                currentBuild = envBuild > 0 ? envBuild.ToString() : "Unknown";
            }

            // Windows 11 builds start at 22000 (e.g. 22000, 22621, 22631, 26100+)
            bool isWindows11 = int.TryParse(currentBuild, out int buildNumber) && buildNumber >= 22000;

            if (string.IsNullOrWhiteSpace(productName))
            {
                productName = isWindows11 ? "Windows 11" : "Windows 10";
            }
            else if (isWindows11)
            {
                // Microsoft often retains "Windows 10" in the ProductName registry value for backwards compatibility.
                // If the build is 22000+, dynamically correct "Windows 10" to "Windows 11".
                if (productName.Contains("Windows 10"))
                {
                    productName = productName.Replace("Windows 10", "Windows 11");
                }
                else if (!productName.Contains("Windows 11"))
                {
                    productName = "Windows 11 " + productName;
                }
            }

            return $"{productName}  Build {currentBuild}";
        }

        #endregion

        #region Cores & Threads

        private string GetCoresAndThreads()
        {
            int totalCores = 0;
            int totalThreads = 0;

            using (var searcher = new ManagementObjectSearcher("SELECT NumberOfCores, NumberOfLogicalProcessors FROM Win32_Processor"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (mo["NumberOfCores"] != null)
                        totalCores += Convert.ToInt32(mo["NumberOfCores"]);
                    if (mo["NumberOfLogicalProcessors"] != null)
                        totalThreads += Convert.ToInt32(mo["NumberOfLogicalProcessors"]);
                }
            }

            if (totalCores > 0 && totalThreads > 0)
            {
                return $"{totalCores} Cores / {totalThreads} Threads";
            }

            return $"{Environment.ProcessorCount} Threads";
        }

        #endregion

        #region RAM Calculation

        private ulong GetTotalInstalledRamBytes()
        {
            ulong total = 0;
            using (var searcher = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
            {
                foreach (ManagementObject mo in searcher.Get())
                {
                    if (mo["Capacity"] != null)
                    {
                        total += Convert.ToUInt64(mo["Capacity"]);
                    }
                }
            }
            return total;
        }

        private string GetLiveRamString(ulong installedBytes)
        {
            var memStatus = new NativeMethods.MEMORYSTATUSEX();
            if (!NativeMethods.GlobalMemoryStatusEx(memStatus))
            {
                return "N/A";
            }

            // Used RAM = TotalPhys - AvailPhys
            ulong usedBytes = memStatus.ullTotalPhys - memStatus.ullAvailPhys;
            ulong totalBytes = installedBytes > 0 ? installedBytes : memStatus.ullTotalPhys;

            double usedGb = usedBytes / (1024.0 * 1024 * 1024);
            double totalGb = totalBytes / (1024.0 * 1024 * 1024);

            return $"{usedGb:F2} GB / {totalGb:F2} GB";
        }

        #endregion

        #region Disks & Disk Type Detection

        private List<DiskInfoModel> GetLiveDisks()
        {
            var result = new List<DiskInfoModel>();
            var drives = DriveInfo.GetDrives()
                .Where(d => d.DriveType == DriveType.Fixed && d.IsReady)
                .OrderBy(d => d.Name)
                .ToList();

            string currentKey = string.Join(",", drives.Select(d => d.Name));
            if (currentKey != _cachedDriveLettersKey)
            {
                UpdateDiskTypeMapping(drives);
                _cachedDriveLettersKey = currentKey;
            }

            foreach (var drive in drives)
            {
                string letter = drive.Name.TrimEnd('\\');
                string type = "Disk";
                if (_cachedDiskTypes.TryGetValue(letter, out string detectedType))
                {
                    type = detectedType;
                }

                long total = drive.TotalSize;
                long free = drive.TotalFreeSpace;
                long used = total - free;

                result.Add(new DiskInfoModel
                {
                    DriveLetter = letter,
                    DiskType = type,
                    UsedBytes = used,
                    TotalBytes = total
                });
            }

            return result;
        }

        private void UpdateDiskTypeMapping(List<DriveInfo> drives)
        {
            _cachedDiskTypes.Clear();

            // 1. Query MSFT_Partition (DriveLetter -> DiskNumber)
            var partitionDiskMap = new Dictionary<char, uint>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                    "SELECT DriveLetter, DiskNumber FROM MSFT_Partition WHERE DriveLetter <> 0"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object dlObj = mo["DriveLetter"];
                        object dnObj = mo["DiskNumber"];
                        if (dlObj != null && dnObj != null)
                        {
                            char dl = Convert.ToChar(dlObj);
                            uint dn = Convert.ToUInt32(dnObj);
                            partitionDiskMap[char.ToUpperInvariant(dl)] = dn;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("MSFT_Partition query failed: " + ex.Message);
            }

            // 2. Query MSFT_PhysicalDisk (DeviceId -> MediaType, BusType, Model)
            var physicalDiskTypes = new Dictionary<uint, string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(@"root\Microsoft\Windows\Storage",
                    "SELECT DeviceId, MediaType, BusType, Model FROM MSFT_PhysicalDisk"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        object idObj = mo["DeviceId"];
                        object mtObj = mo["MediaType"];
                        object btObj = mo["BusType"];
                        if (idObj != null)
                        {
                            uint devId = Convert.ToUInt32(idObj);
                            int mediaType = mtObj != null ? Convert.ToInt32(mtObj) : 0;
                            int busType = btObj != null ? Convert.ToInt32(btObj) : 0;

                            string diskType = "Disk";
                            if (busType == 17) // BusType 17 = NVMe per SPEC section 7
                            {
                                diskType = "NVMe";
                            }
                            else if (mediaType == 4) // MediaType 4 = SSD
                            {
                                diskType = "SSD";
                            }
                            else if (mediaType == 3) // MediaType 3 = HDD
                            {
                                diskType = "HDD";
                            }
                            else
                            {
                                // Unspecified (e.g. MediaType 0), fall back to seek-penalty query / model
                                string modelStr = mo["Model"]?.ToString() ?? string.Empty;
                                diskType = QuerySeekPenalty(devId, modelStr);
                            }

                            physicalDiskTypes[devId] = diskType;
                            if (mo["Model"] != null)
                            {
                                physicalDiskModels[devId] = mo["Model"].ToString();
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("MSFT_PhysicalDisk query failed: " + ex.Message);
            }

            // 3. Join partition map to physical disk types
            foreach (var drive in drives)
            {
                char letterChar = char.ToUpperInvariant(drive.Name[0]);
                string letterStr = letterChar + ":";
                string detectedType = "Disk";
                string model = string.Empty;

                if (partitionDiskMap.TryGetValue(letterChar, out uint diskNum))
                {
                    physicalDiskModels.TryGetValue(diskNum, out model);
                    if (physicalDiskTypes.TryGetValue(diskNum, out string pType) && pType != "Disk")
                    {
                        detectedType = pType;
                    }
                    else
                    {
                        detectedType = QuerySeekPenalty(letterChar, model);
                    }
                }
                else
                {
                    detectedType = QuerySeekPenalty(letterChar, model);
                }

                _cachedDiskTypes[letterStr] = detectedType;
            }
        }

        private readonly Dictionary<uint, string> physicalDiskModels = new Dictionary<uint, string>();

        private string QuerySeekPenalty(uint physicalDiskNumber, string model)
        {
            string devicePath = @"\\.\PhysicalDrive" + physicalDiskNumber;
            return RunSeekPenaltyCheck(devicePath, model);
        }

        private string QuerySeekPenalty(char driveLetter, string model)
        {
            string devicePath = @"\\.\" + driveLetter + ":";
            return RunSeekPenaltyCheck(devicePath, model);
        }

        private string RunSeekPenaltyCheck(string devicePath, string model)
        {
            IntPtr hDevice = NativeMethods.CreateFile(
                devicePath,
                0, // 0 query access, does not require administrator privileges
                NativeMethods.FILE_SHARE_READ | NativeMethods.FILE_SHARE_WRITE,
                IntPtr.Zero,
                NativeMethods.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (hDevice != IntPtr.Zero && hDevice != new IntPtr(-1))
            {
                try
                {
                    var query = new NativeMethods.STORAGE_PROPERTY_QUERY
                    {
                        PropertyId = NativeMethods.StorageDeviceSeekPenaltyProperty,
                        QueryType = NativeMethods.PropertyStandardQuery
                    };

                    var descriptor = new NativeMethods.DEVICE_SEEK_PENALTY_DESCRIPTOR();
                    uint bytesReturned;

                    bool success = NativeMethods.DeviceIoControl(
                        hDevice,
                        NativeMethods.IOCTL_STORAGE_QUERY_PROPERTY,
                        ref query,
                        (uint)Marshal.SizeOf(typeof(NativeMethods.STORAGE_PROPERTY_QUERY)),
                        ref descriptor,
                        (uint)Marshal.SizeOf(typeof(NativeMethods.DEVICE_SEEK_PENALTY_DESCRIPTOR)),
                        out bytesReturned,
                        IntPtr.Zero);

                    if (success)
                    {
                        return descriptor.IncursSeekPenalty ? "HDD" : "SSD";
                    }
                }
                catch
                {
                    // Ignore and try model fallback
                }
                finally
                {
                    NativeMethods.CloseHandle(hDevice);
                }
            }

            // Fallback: If seek penalty query fails or is unsupported (common on older HDDs)
            if (!string.IsNullOrEmpty(model))
            {
                string m = model.ToUpperInvariant();
                if (m.Contains("SSD")) return "SSD";
                if (m.Contains("HDD") || m.Contains("HD") || m.Contains("SPIN") || m.Contains("BARRACUDA")) return "HDD";
            }

            return "HDD";
        }

        #endregion

        #region GPU & VRAM

        /*
         * Dedicated vs Integrated Classification:
         * We query the display class registry (HKLM\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\0*).
         * 1. Software adapters (Microsoft Basic Render Driver, Remote Display Adapter, etc.) are ignored.
         * 2. Intel integrated graphics (UHD, HD, Iris) and AMD APUs (Vega, Radeon Graphics on Ryzen APUs without discrete dGPU branding)
         *    are classified as "Integrated", and their VRAM is marked "(shared)".
         * 3. Discrete GPUs (AMD Radeon RX, R9, R7, HD discrete; NVIDIA GeForce, RTX, GTX, Quadro; Intel Arc)
         *    are classified as "Dedicated", and their dedicated memory is formatted as e.g. "4.00 GB".
         * 4. Dedicated GPUs take priority and are sorted first. We show up to 2 GPUs per SPEC section 7.
         */
        private List<GpuInfoModel> GetGpuList()
        {
            var list = new List<GpuInfoModel>();

            const string classKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using (var classKey = Registry.LocalMachine.OpenSubKey(classKeyPath))
            {
                if (classKey == null) return list;

                foreach (string subkeyName in classKey.GetSubKeyNames())
                {
                    if (!Regex.IsMatch(subkeyName, @"^\d{4}$")) continue;

                    using (var subkey = classKey.OpenSubKey(subkeyName))
                    {
                        if (subkey == null) continue;

                        string driverDesc = subkey.GetValue("DriverDesc") as string;
                        if (string.IsNullOrWhiteSpace(driverDesc)) continue;

                        // Ignore software / virtual adapters
                        if (driverDesc.IndexOf("Basic Render", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            driverDesc.IndexOf("Remote Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            driverDesc.IndexOf("Virtual", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            driverDesc.IndexOf("V-Display", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            driverDesc.IndexOf("Miracast", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            continue;
                        }

                        // Retrieve 64-bit safe VRAM from HardwareInformation.qwMemorySize
                        ulong vramBytes = 0;
                        object qwMem = subkey.GetValue("HardwareInformation.qwMemorySize");
                        if (qwMem is long longMem && longMem > 0)
                        {
                            vramBytes = (ulong)longMem;
                        }
                        else
                        {
                            object mem = subkey.GetValue("HardwareInformation.MemorySize");
                            if (mem is int intMem && intMem > 0)
                            {
                                vramBytes = (ulong)(uint)intMem;
                            }
                        }

                        // Classify Dedicated vs Integrated
                        bool isIntegrated = ClassifyIsIntegrated(driverDesc, vramBytes);
                        string tag = isIntegrated ? "Integrated" : "Dedicated";

                        // Format VRAM
                        string vramStr;
                        if (isIntegrated)
                        {
                            if (vramBytes >= 1024 * 1024 * 1024)
                            {
                                double gb = vramBytes / (1024.0 * 1024 * 1024);
                                vramStr = $"{gb:F2} GB (shared)";
                            }
                            else if (vramBytes > 0)
                            {
                                double mb = vramBytes / (1024.0 * 1024);
                                vramStr = $"{Math.Round(mb)} MB (shared)";
                            }
                            else
                            {
                                vramStr = "512 MB (shared)";
                            }
                        }
                        else
                        {
                            double gb = vramBytes / (1024.0 * 1024 * 1024);
                            vramStr = $"{gb:F2} GB";
                        }

                        list.Add(new GpuInfoModel
                        {
                            Name = driverDesc,
                            Tag = tag,
                            VramString = vramStr
                        });
                    }
                }
            }

            // Dedicated first, up to 2 GPUs
            return list
                .OrderBy(g => g.Tag == "Dedicated" ? 0 : 1)
                .Take(2)
                .ToList();
        }

        private bool ClassifyIsIntegrated(string name, ulong vramBytes)
        {
            string lower = name.ToLowerInvariant();

            // Intel Arc is dedicated; Intel HD / UHD / Iris is integrated
            if (lower.Contains("intel"))
            {
                if (lower.Contains("arc")) return false;
                return true;
            }

            // AMD APUs
            if (lower.Contains("radeon(tm) graphics") ||
                lower.Contains("vega 3") || lower.Contains("vega 6") ||
                lower.Contains("vega 7") || lower.Contains("vega 8") ||
                lower.Contains("vega 11") || lower.Contains("apu"))
            {
                return true;
            }

            // NVIDIA discrete is dedicated
            if (lower.Contains("geforce") || lower.Contains("rtx") ||
                lower.Contains("gtx") || lower.Contains("quadro") || lower.Contains("nvidia"))
            {
                return false;
            }

            // Discrete AMD (RX, R9, R7, HD 7000+)
            if (lower.Contains("rx ") || lower.Contains("rx5") || lower.Contains("rx 5") ||
                lower.Contains("rx 6") || lower.Contains("rx 7") || lower.Contains("r9") ||
                lower.Contains("r7") || lower.Contains("hd 7") || lower.Contains("hd 8"))
            {
                return false;
            }

            // Fallback: If VRAM <= 2GB and no discrete markers, treat as integrated
            if (vramBytes <= 2147483648L && !lower.Contains("discrete"))
            {
                return true;
            }

            return false;
        }

        #endregion

        #region Network Throughput & Active Adapter

        private (string adapterName, string throughput) GetLiveNetworkThroughput()
        {
            NetworkInterface activeNic = null;

            try
            {
                // Find operational, non-loopback, non-virtual interface with default gateway
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;

                    string desc = nic.Description.ToLowerInvariant();
                    if (desc.Contains("virtual") || desc.Contains("vpn") ||
                        desc.Contains("hyper-v") || desc.Contains("vethernet") ||
                        desc.Contains("tap-") || desc.Contains("virtualbox") ||
                        desc.Contains("vmware") || desc.Contains("wireguard"))
                    {
                        continue;
                    }

                    var ipProps = nic.GetIPProperties();
                    if (ipProps == null) continue;

                    bool hasGateway = ipProps.GatewayAddresses != null &&
                                      ipProps.GatewayAddresses.Any(g => g.Address != null &&
                                                                       !g.Address.ToString().Equals("0.0.0.0") &&
                                                                       !g.Address.ToString().Equals("::"));
                    if (hasGateway)
                    {
                        activeNic = nic;
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("Network interface query failed: " + ex.Message);
            }

            if (activeNic == null)
            {
                _lastAdapterId = null;
                return ("Ethernet", "Not connected");
            }

            string typeName = (activeNic.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                ? "Wi-Fi"
                : "Ethernet";

            var stats = activeNic.GetIPStatistics();
            long currentSent = stats.BytesSent;
            long currentRecv = stats.BytesReceived;
            DateTime now = DateTime.UtcNow;

            if (_lastAdapterId != activeNic.Id || _lastNetworkSampleTime == DateTime.MinValue)
            {
                _lastAdapterId = activeNic.Id;
                _lastBytesSent = currentSent;
                _lastBytesReceived = currentRecv;
                _lastNetworkSampleTime = now;
                return (typeName, "UP 0 Kbps   DOWN 0 Kbps");
            }

            double elapsedSeconds = (now - _lastNetworkSampleTime).TotalSeconds;
            if (elapsedSeconds < 0.5)
            {
                elapsedSeconds = 1.0;
            }

            long deltaSent = Math.Max(0, currentSent - _lastBytesSent);
            long deltaRecv = Math.Max(0, currentRecv - _lastBytesReceived);

            _lastBytesSent = currentSent;
            _lastBytesReceived = currentRecv;
            _lastNetworkSampleTime = now;

            // Convert to bits per second
            double upBps = (deltaSent * 8.0) / elapsedSeconds;
            double downBps = (deltaRecv * 8.0) / elapsedSeconds;

            string upStr = FormatSpeed(upBps);
            string downStr = FormatSpeed(downBps);

            return (typeName, $"UP {upStr}   DOWN {downStr}");
        }

        private string FormatSpeed(double bps)
        {
            double kbps = bps / 1000.0;
            if (kbps < 1000.0)
            {
                return $"{Math.Round(kbps)} Kbps";
            }
            double mbps = kbps / 1000.0;
            return $"{mbps:F2} Mbps";
        }

        #endregion
    }
}
