using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;
using ChromaticMenu.Native;

namespace ChromaticMenu.Services
{
    public class IconService
    {
        private static IconService _instance;
        public static IconService Instance => _instance ?? (_instance = new IconService());

        public string IconsDirectory
        {
            get
            {
                try
                {
                    return Path.Combine(ConfigService.Instance.AssetsDirectory, "icons");
                }
                catch
                {
                    return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "assets", "icons");
                }
            }
        }

        public IconService()
        {
        }

        public BitmapSource GetOrExtractIcon(string itemId, string targetPath, string kind, int decodeSize = 0)
        {
            if (string.IsNullOrEmpty(itemId)) return null;

            try
            {
                if (!Directory.Exists(IconsDirectory))
                {
                    Directory.CreateDirectory(IconsDirectory);
                }

                string cachedPngPath = Path.Combine(IconsDirectory, $"{itemId}.png");

                // 1. If already cached as PNG, verify it is high-resolution (>= 64px)
                if (File.Exists(cachedPngPath))
                {
                    bool isLegacySmall = false;
                    try
                    {
                        using (var fs = new FileStream(cachedPngPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        using (var img = Image.FromStream(fs, false, false))
                        {
                            if (img.Width < 64 || img.Height < 64)
                            {
                                isLegacySmall = true;
                            }
                        }
                    }
                    catch { }

                    if (!isLegacySmall)
                    {
                        return LoadFrozenBitmap(cachedPngPath, decodeSize);
                    }
                }

                // Skip extraction for URLs (they use Lucide globe icon)
                if (string.Equals(kind, "url", StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                // 2. Extract high-resolution icon
                string resolvedPath = ResolveExecutablePath(targetPath);
                if (!File.Exists(resolvedPath) && !Directory.Exists(resolvedPath))
                {
                    // If target path doesn't exist but cached PNG does (even if legacy), return it
                    if (File.Exists(cachedPngPath))
                    {
                        return LoadFrozenBitmap(cachedPngPath, decodeSize);
                    }
                    return null;
                }

                using (Bitmap bmp = ExtractHighResBitmap(resolvedPath))
                {
                    if (bmp != null)
                    {
                        bmp.Save(cachedPngPath, ImageFormat.Png);
                        return LoadFrozenBitmap(cachedPngPath, decodeSize);
                    }
                    else if (File.Exists(cachedPngPath))
                    {
                        return LoadFrozenBitmap(cachedPngPath, decodeSize);
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to extract/load icon for {itemId} ({targetPath}): {ex.Message}");
            }

            return null;
        }

        public static string ResolveExecutablePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            string cleaned = path.Trim().Trim('"');
            string expanded = Environment.ExpandEnvironmentVariables(cleaned);

            if (File.Exists(expanded) || Directory.Exists(expanded))
            {
                return expanded;
            }

            if (!Path.IsPathRooted(expanded))
            {
                // Check System directory (e.g. System32)
                try
                {
                    string sysPath = Path.Combine(Environment.SystemDirectory, expanded);
                    if (File.Exists(sysPath)) return sysPath;
                }
                catch { }

                // Check Windows directory (e.g. C:\Windows)
                try
                {
                    string winPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), expanded);
                    if (File.Exists(winPath)) return winPath;
                }
                catch { }

                // Search along system PATH
                try
                {
                    string pathEnv = Environment.GetEnvironmentVariable("PATH");
                    if (!string.IsNullOrEmpty(pathEnv))
                    {
                        foreach (var folder in pathEnv.Split(new[] { Path.PathSeparator }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            try
                            {
                                string candidate = Path.Combine(folder.Trim().Trim('"'), expanded);
                                if (File.Exists(candidate)) return candidate;
                            }
                            catch { }
                        }
                    }
                }
                catch { }
            }

            return expanded;
        }

        private Bitmap ExtractHighResBitmap(string path)
        {
            string resolvedPath = ResolveExecutablePath(path);
            try
            {
                if (resolvedPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase))
                {
                    string target = ResolveShortcutTarget(resolvedPath);
                    if (!string.IsNullOrEmpty(target) && (File.Exists(target) || Directory.Exists(target)))
                    {
                        resolvedPath = target;
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Shortcut resolution failed for {path}: {ex.Message}");
            }

            // Try 1: Win32 PrivateExtractIcons (requesting 256x256 high-resolution icon)
            try
            {
                if (File.Exists(resolvedPath))
                {
                    IntPtr[] phicon = new IntPtr[1];
                    int[] piconid = new int[1];
                    int count = NativeMethods.PrivateExtractIcons(resolvedPath, 0, 256, 256, phicon, piconid, 1, 0);
                    if (count > 0 && phicon[0] != IntPtr.Zero)
                    {
                        try
                        {
                            using (Icon ico = Icon.FromHandle(phicon[0]))
                            {
                                return ico.ToBitmap();
                            }
                        }
                        finally
                        {
                            NativeMethods.DestroyIcon(phicon[0]);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("PrivateExtractIcons extraction failed: " + ex.Message);
            }

            // Try 2: SHGetImageList JUMBO (256x256) via Windows Shell ImageList
            try
            {
                var shfi = new NativeMethods.SHFILEINFO();
                IntPtr ret = NativeMethods.SHGetFileInfo(
                    resolvedPath,
                    0,
                    ref shfi,
                    (uint)Marshal.SizeOf(shfi),
                    NativeMethods.SHGFI_SYSICONINDEX);

                if (ret != IntPtr.Zero && shfi.iIcon >= 0)
                {
                    Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950");
                    NativeMethods.IImageList iml;
                    int hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_JUMBO, ref iid, out iml);
                    if (hr == 0 && iml != null)
                    {
                        IntPtr hIcon;
                        hr = iml.GetIcon(shfi.iIcon, NativeMethods.ILD_TRANSPARENT, out hIcon);
                        if (hr == 0 && hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                using (Icon ico = Icon.FromHandle(hIcon))
                                {
                                    return ico.ToBitmap();
                                }
                            }
                            finally
                            {
                                NativeMethods.DestroyIcon(hIcon);
                            }
                        }
                    }

                    // Fallback to EXTRA LARGE (48x48)
                    hr = NativeMethods.SHGetImageList(NativeMethods.SHIL_EXTRALARGE, ref iid, out iml);
                    if (hr == 0 && iml != null)
                    {
                        IntPtr hIcon;
                        hr = iml.GetIcon(shfi.iIcon, NativeMethods.ILD_TRANSPARENT, out hIcon);
                        if (hr == 0 && hIcon != IntPtr.Zero)
                        {
                            try
                            {
                                using (Icon ico = Icon.FromHandle(hIcon))
                                {
                                    return ico.ToBitmap();
                                }
                            }
                            finally
                            {
                                NativeMethods.DestroyIcon(hIcon);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("SHGetImageList extraction failed: " + ex.Message);
            }

            // Try 3: Icon.ExtractAssociatedIcon fallback
            try
            {
                if (File.Exists(resolvedPath))
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(resolvedPath))
                    {
                        if (ico != null)
                        {
                            return ico.ToBitmap();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn("ExtractAssociatedIcon fallback failed: " + ex.Message);
            }

            return null;
        }

        private BitmapSource LoadFrozenBitmap(string pngPath, int decodeSize)
        {
            try
            {
                if (!File.Exists(pngPath)) return null;

                byte[] bytes = File.ReadAllBytes(pngPath);
                using (var ms = new MemoryStream(bytes))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    if (decodeSize > 0)
                    {
                        bitmap.DecodePixelWidth = decodeSize;
                    }
                    bitmap.StreamSource = ms;
                    bitmap.EndInit();
                    bitmap.Freeze(); // SPEC section 13: Freeze all brushes and images
                    return bitmap;
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to load frozen bitmap from {pngPath}: {ex.Message}");
                return null;
            }
        }

        public string ResolveShortcutTarget(string lnkPath)
        {
            try
            {
                if (!File.Exists(lnkPath)) return lnkPath;

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType != null)
                {
                    object shell = Activator.CreateInstance(shellType);
                    object shortcut = shellType.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
                    if (shortcut != null)
                    {
                        object target = shortcut.GetType().InvokeMember("TargetPath", System.Reflection.BindingFlags.GetProperty, null, shortcut, null);
                        if (target is string s && !string.IsNullOrEmpty(s))
                        {
                            return s;
                        }
                    }
                }
            }
            catch
            {
                // Fall back to original lnk path
            }

            return lnkPath;
        }

        public BitmapSource ExtractIconFromPathAndIndex(string itemId, string iconFile, int iconIndex, int size = 256)
        {
            if (string.IsNullOrEmpty(itemId) || string.IsNullOrEmpty(iconFile)) return null;

            try
            {
                if (!Directory.Exists(IconsDirectory))
                {
                    Directory.CreateDirectory(IconsDirectory);
                }

                string cachedPngPath = Path.Combine(IconsDirectory, $"{itemId}.png");
                string expandedPath = ResolveExecutablePath(iconFile);

                if (!File.Exists(expandedPath)) return null;

                IntPtr[] phicon = new IntPtr[1];
                int[] piconid = new int[1];
                int count = NativeMethods.PrivateExtractIcons(expandedPath, iconIndex, size, size, phicon, piconid, 1, 0);

                if (count > 0 && phicon[0] != IntPtr.Zero)
                {
                    try
                    {
                        using (Icon ico = Icon.FromHandle(phicon[0]))
                        using (Bitmap bmp = ico.ToBitmap())
                        {
                            bmp.Save(cachedPngPath, ImageFormat.Png);
                        }
                    }
                    finally
                    {
                        NativeMethods.DestroyIcon(phicon[0]);
                    }

                    return LoadFrozenBitmap(cachedPngPath, size);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to extract custom icon for {itemId} ({iconFile} #{iconIndex}): {ex.Message}");
            }

            return null;
        }

        public bool ShowPickIconDialog(IntPtr ownerHwnd, ref string iconPath, ref int iconIndex)
        {
            try
            {
                var sb = new System.Text.StringBuilder(string.IsNullOrEmpty(iconPath) ? "%SystemRoot%\\System32\\shell32.dll" : iconPath, 260);
                int index = iconIndex;
                bool ok = NativeMethods.PickIconDlg(ownerHwnd, sb, sb.Capacity, ref index);
                if (ok)
                {
                    iconPath = sb.ToString();
                    iconIndex = index;
                    return true;
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"PickIconDlg failed: {ex.Message}");
            }

            return false;
        }

        public BitmapSource ResetIconToDefault(string itemId, string targetPath, string kind, int decodeSize = 256)
        {
            try
            {
                string cachedPngPath = Path.Combine(IconsDirectory, $"{itemId}.png");
                if (File.Exists(cachedPngPath))
                {
                    File.Delete(cachedPngPath);
                }
            }
            catch (Exception ex)
            {
                LoggerService.Instance.Warn($"Failed to delete cached icon for {itemId}: {ex.Message}");
            }

            return GetOrExtractIcon(itemId, targetPath, kind, decodeSize);
        }
    }
}
