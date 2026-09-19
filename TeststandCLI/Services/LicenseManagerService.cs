using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NationalInstruments.TestStand.Interop.API;
using TestStandCLI.Logging;

namespace TestStandCLI.Services;

public static class LicenseManagerService
{
    private static readonly Dictionary<string, List<(string LicenseName, string LicenseVersion)>> VersionToLicense = new()
    {
        {
            "25.5", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "25.0500"),
                ("TestStand_CustSeqEd_Pkg", "25.0500"),
                ("TestStand_DebugDeploy_Pkg", "25.0500"),
                ("TestStand_DevSystem_Pkg", "25.0500")
            }
        },
        {
            "24.9", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "24.0900"),
                ("TestStand_CustSeqEd_Pkg", "24.0900"),
                ("TestStand_DebugDeploy_Pkg", "24.0900"),
                ("TestStand_DevSystem_Pkg", "24.0900")
            }
        },
        {
            "23.8", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "23.0800"),
                ("TestStand_CustSeqEd_Pkg", "23.0800"),
                ("TestStand_DebugDeploy_Pkg", "23.0800"),
                ("TestStand_DevSystem_Pkg", "23.0800")
            }
        },
        {
            "22.0", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "22.0000"),
                ("TestStand_CustSeqEd_Pkg", "22.0000"),
                ("TestStand_DebugDeploy_Pkg", "22.0000"),
                ("TestStand_DevSystem_Pkg", "22.0000")
            }
        },
        {
            "21.1", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "21.0100"),
                ("TestStand_CustSeqEd_Pkg", "21.0100"),
                ("TestStand_DebugDeploy_Pkg", "21.0100"),
                ("TestStand_DevSystem_Pkg", "21.0100")
            }
        },
        {
            "20.0", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "20.0000"),
                ("TestStand_CustSeqEd_Pkg", "20.0000"),
                ("TestStand_DebugDeploy_Pkg", "20.0000"),
                ("TestStand_DevSystem_Pkg", "20.0000")
            }
        },
        {
            "19.0", new List<(string, string)>
            {
                ("TestStand_BaseDeploy_Pkg", "19.0000"),
                ("TestStand_CustSeqEd_Pkg", "19.0000"),
                ("TestStand_DebugDeploy_Pkg", "19.0000"),
                ("TestStand_DevSystem_Pkg", "19.0000")
            }
        },
    };
    const string LicenseDirectory = "C:\\ProgramData\\National Instruments\\License Manager\\Licenses";

    const string ActivatorPath =
        "C:\\Program Files (x86)\\National Instruments\\Shared\\License Manager\\bin\\nilmUtil.exe";

    public static void ActivateLicenseKey(string licenseKey)
    {
        var versions = VersionSelectorService.ListTestStandVersions();
        foreach (var version in versions)
        {
            if (!VersionToLicense.TryGetValue(version.Version, out var licenses))
            {
                ConsoleLogger.Debug("No license mapping found for TestStand version {0}", version.Version);
                Environment.ExitCode = 1; 
                return;
            }

            List<bool> activated = new List<bool>();
            foreach (var (licenseName, activationCode) in licenses)
            {
                activated.Add(ActivateLicense(licenseName, activationCode, licenseKey));
            }

            var licenseActivated = activated.Any(x => x == true);
            if (licenseActivated)
            {
                ConsoleLogger.Information("License activated");
                Environment.ExitCode = 0;
            }
            else
            {
                ConsoleLogger.Information("License not activated");
                Environment.ExitCode = 1;
            }
        }
        
    }


    private static bool ActivateLicense(string packageName, string version, string serialNumber)
    {
        var licenseFileName = packageName + "_" + version.Replace(".", "") + ".lc";
        var licenseBefore = File.ReadAllText(Path.Combine(LicenseDirectory, licenseFileName));
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = ActivatorPath,
                Arguments =
                    $"-s -activate \"{packageName}\" -version \"{version}\" -serialnumber \"{serialNumber}\" -firstname \"CI Pipeline\" -lastname \"CI Pipeline\" -organizationname \"CI Pipeline\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                process.WaitForExit();

                var output = process.StandardOutput.ReadToEnd();
                var error = process.StandardError.ReadToEnd();

                if (process.ExitCode == 0)
                {
                    ConsoleLogger.Information("Successfully executed license activation for {0} version {1}",
                        packageName, version);
                    if (!string.IsNullOrEmpty(output))
                    {
                        ConsoleLogger.Debug("Output: {0}", output);
                    }
                }
                else
                {
                    ConsoleLogger.Error("Failed to activate license for {0} version {1}", packageName, version);
                    if (!string.IsNullOrEmpty(error))
                    {
                        ConsoleLogger.Error("Error: {0}", error);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            ConsoleLogger.Error("Error activating license: {0}", ex.Message);
            Environment.ExitCode = 1;
        }

        var licenseAfter = File.ReadAllText(Path.Combine(LicenseDirectory, licenseFileName));

        if (licenseAfter != licenseBefore)
        {
            ConsoleLogger.Debug("Change in license detected - License after: {0}", licenseAfter);
            return true;
        }
        else
        {
            ConsoleLogger.Debug("No change in license detected");
            return false;
        }
    }
}