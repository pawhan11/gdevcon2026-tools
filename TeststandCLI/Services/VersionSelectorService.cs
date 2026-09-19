using System;
using System.IO;
using System.Linq;

namespace TestStandCLI.Services;
using Microsoft.Win32;
using TestStandCLI.Logging;
using System.Collections.Generic;
using System.Diagnostics;

public static class VersionSelectorService
{
    private const string RegistryPath = "SOFTWARE\\National Instruments\\TestStand";

    private const string TsSelectorPath =
        "C:\\Program Files (x86)\\National Instruments\\Shared\\TestStand Version Selector\\TSVerSelect.exe";
    private const string TsSelectorLogDir = "C:\\ProgramData\\National Instruments\\TestStand Version Selector"; 
    
    public static TestStandVersion[] ListTestStandVersions()
    {
        var versions = new List<TestStandVersion>();
        var testStandKey = Registry.LocalMachine.OpenSubKey(RegistryPath);
        
        if (testStandKey == null)
            return versions.ToArray();
        
        foreach (string versionKeyName in testStandKey.GetSubKeyNames())
        {
            var versionKey = testStandKey.OpenSubKey(versionKeyName);
            if (versionKey == null) continue;
            
            var version = new TestStandVersion
            {
                Path = GetRegistryStringValue(versionKey, "Path"),
                ProductCode = GetRegistryStringValue(versionKey, "ProductCode"),
                ProductName = GetRegistryStringValue(versionKey, "ProductName"),
                VersionString = GetRegistryStringValue(versionKey, "VersionString"),
                Version = GetRegistryStringValue(versionKey, "Version")
            };
            
            versions.Add(version);
        }
        
        return versions.ToArray();
    }
    
    private static string GetRegistryStringValue(RegistryKey key, string valueName)
    {
        try
        {
            return key.GetValue(valueName)?.ToString() ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }
    

    public static void ActivateTSVersion(string version)
    {
        var versions = ListTestStandVersions();
        var selectedVersion = versions.FirstOrDefault(v => v.Version == version);
        if (selectedVersion == null)
        {
            throw new ArgumentException($"TestStand version {version} not found.");
        }

        if (!File.Exists(TsSelectorPath))
        {
            throw new FileNotFoundException($"TestStand Version Selector not found at: {TsSelectorPath}");
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = TsSelectorPath,
            Arguments = $"/version {version} /noprompt",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        if (!Directory.Exists(TsSelectorLogDir))
        {
            Directory.CreateDirectory(TsSelectorLogDir);
        }
        
        var monitor = new DirectoryFileMonitor(TsSelectorLogDir, "*.log", true);
        monitor.StartMonitoring();
        monitor.DataWritten += (sender, e) =>
        {
            ConsoleLogger.Debug("Version Selector Logs: {0}", e.NewData);
        };
        
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            ConsoleLogger.Debug("Version selector failed.");
            var errorOutput = process.StandardError.ReadToEnd();
            Environment.ExitCode = 1;
        }
        else
        {
            monitor.StopMonitoring();
            monitor.Dispose();
            ConsoleLogger.Debug("Version selector finished.");
            Environment.ExitCode = 0;
        }
        
        
    }
    

}

public class TestStandVersion
{
    public string Path { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public string VersionString { get; set; }
    public string Version { get; set; }
}
