using System;
using System.Collections.Generic;
using System.Diagnostics;
using ChromaticMenu.Models;

namespace ChromaticMenu.Services
{
    public class ToolLaunchService
    {
        private static ToolLaunchService _instance;
        public static ToolLaunchService Instance => _instance ?? (_instance = new ToolLaunchService());

        public static TechnicalToolModel RequestGameTool { get; } =
            TechnicalToolModel.InApp("Request a Game", "message-square", ToolAction.RequestGame, "Ask the shop to add a game");

        public List<TechnicalToolModel> GetDefaultTools()
        {
            return new List<TechnicalToolModel>
            {
                new TechnicalToolModel("Control Panel", "sliders-horizontal", "control.exe"),
                new TechnicalToolModel("Uninstall Programs", "trash", "control.exe", "appwiz.cpl"),
                new TechnicalToolModel("File Explorer", "folder", "explorer.exe"),
                new TechnicalToolModel("Network Connections", "network", "control.exe", "ncpa.cpl"),
                new TechnicalToolModel("Device Manager", "cpu", "devmgmt.msc"),
                new TechnicalToolModel("Disk Management", "hard-drive", "diskmgmt.msc"),
                new TechnicalToolModel("Services", "settings", "services.msc"),
                new TechnicalToolModel("Task Manager", "activity", "taskmgr.exe"),
                new TechnicalToolModel("Command Prompt", "terminal", "cmd.exe"),
                new TechnicalToolModel("Windows Settings", "settings", "ms-settings:"),
                new TechnicalToolModel("System Information", "info", "msinfo32.exe")
            };
        }

        public bool LaunchTool(TechnicalToolModel tool, out string errorMessage)
        {
            errorMessage = null;

            if (tool == null)
            {
                errorMessage = "Invalid tool specified.";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = tool.FileName,
                    Arguments = tool.Arguments ?? string.Empty,
                    UseShellExecute = true
                };

                Process.Start(psi);
                LoggerService.Instance.Info($"Launched technical tool: {tool.Name} ({tool.FileName} {tool.Arguments})");
                return true;
            }
            catch (Exception ex)
            {
                // SPEC section 7: If Windows or WinLock blocks one, catch the failure and show a friendly message. Do not crash and do not retry.
                LoggerService.Instance.Warn($"Failed to launch {tool.Name}: {ex.Message}");
                errorMessage = $"Unable to open {tool.Name}: The operation was blocked or the tool could not be started.";
                return false;
            }
        }
    }
}
