using System;
using System.IO;
using ChromaticMenu.Services;

class TestExtract
{
    [STAThread]
    static void Main()
    {
        string notepad = @"C:\Windows\notepad.exe";
        var img = IconService.Instance.GetOrExtractIcon("test_notepad", notepad, "exe", 256);
        Console.WriteLine("Notepad icon: " + (img != null ? string.Format("{0}x{1}", img.PixelWidth, img.PixelHeight) : "NULL"));
        
        string cacheFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "assets", "icons", "test_notepad.png");
        Console.WriteLine("Cache file exists: " + File.Exists(cacheFile));
        if (File.Exists(cacheFile))
        {
            var fi = new FileInfo(cacheFile);
            Console.WriteLine("Cache file size: " + fi.Length + " bytes");
        }
    }
}
