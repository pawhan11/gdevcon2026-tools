using System;
using System.CommandLine;
using TestStandCLI.Logging;
using TestStandCLI.Services;

namespace TestStandCLI;

internal class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var verboseOption = new Option<bool>("--verbose", "-v"){ Description = "Enable verbose (debug) logging"};

        var rootCommand = new RootCommand("TestStandCLI");

        var analyzeCommand = new Command("analyze", "Analyze project in directory");
        analyzeCommand.Add(verboseOption);
        var analyzeDirectoryArg = new Argument<string>("directory") { Description = "Directory where analysis will execute" };
        var junitReportOption = new Option<string>("--junit-report") { Description = "Output path for JUnit XML report" };
        analyzeCommand.Add(analyzeDirectoryArg);
        analyzeCommand.Add(junitReportOption);
        analyzeCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var directory = parseResult.GetValue(analyzeDirectoryArg);
            var junitReportPath = parseResult.GetValue(junitReportOption);
            ConsoleLogger.Information("Analyzing project in directory: {0}", directory);
            SequenceAnalyzerService.AnalyzeDirectory(directory, junitReportPath);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(analyzeCommand);

        var protectCommand = new Command("protect", "Protect sequences in directory");
        protectCommand.Add(verboseOption);
        var protectDirectoryArg = new Argument<string>("directory") { Description = "Directory where sequences will be protected" };
        var protectFormatOption = new Option<string>("--format") { Description = "File writing format: xml or bin (default: keep current format)" };
        protectFormatOption.AcceptOnlyFromAmong("xml", "bin");
        protectCommand.Add(protectDirectoryArg);
        protectCommand.Add(protectFormatOption);
        protectCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var directory = parseResult.GetValue(protectDirectoryArg);
            var format = parseResult.GetValue(protectFormatOption);
            ConsoleLogger.Information("Protecting sequences in directory: {0}", directory);
            TestStandService.ProtectDirectory(directory, format);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(protectCommand);

        var executeCommand = new Command("execute", "Execute sequence file");
        executeCommand.Add(verboseOption);
        var executeSequenceArg = new Argument<string>("sequence-file") { Description = "Path to sequence file" };
        var executeEntryArg = new Argument<string>("entry-point") { Description = "Subsequence to execute" };
        executeCommand.Add(executeSequenceArg);
        executeCommand.Add(executeEntryArg);
        executeCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var sequenceFile = parseResult.GetValue(executeSequenceArg);
            var entryPoint = parseResult.GetValue(executeEntryArg);
            ConsoleLogger.Information("Executing sequence file: {0}", sequenceFile);
            ConsoleLogger.Information("Entry point: {0}", entryPoint);
            TestStandService.ExecuteSequenceFile(sequenceFile, entryPoint);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(executeCommand);

        var checkActiveCommand = new Command("check-active", "Check active TestStand version");
        checkActiveCommand.Add(verboseOption);
        checkActiveCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            try
            {
                var active = TestStandService.ReadActiveTestStand();
                ConsoleLogger.Information("{0}", active);
                Environment.Exit(0);
            }
            catch (Exception)
            {
                ConsoleLogger.Error("Unknown");
                Environment.Exit(1);
            }
        });
        rootCommand.Add(checkActiveCommand);

        var listInstalledCommand = new Command("list-installed", "List installed TestStand versions");
        listInstalledCommand.Add(verboseOption);
        listInstalledCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var installedVersions = VersionSelectorService.ListTestStandVersions();
            if (installedVersions.Length == 0)
            {
                ConsoleLogger.Information("No TestStand versions found installed on this system.");
                Environment.Exit(1);
            }
            foreach (var version in installedVersions)
            {
                ConsoleLogger.Information("Version: {0}, Product Name: {1}, Path: {2}",
                    version.Version,
                    version.ProductName ?? "N/A",
                    version.Path ?? "N/A");
            }
            Environment.Exit(0);
        });
        rootCommand.Add(listInstalledCommand);

        var setActiveCommand = new Command("set-active", "Set active TestStand version");
        setActiveCommand.Add(verboseOption);
        var setActiveVersionArg = new Argument<string>("version") { Description = "TestStand version to activate" };
        setActiveCommand.Add(setActiveVersionArg);
        setActiveCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var version = parseResult.GetValue(setActiveVersionArg);
            VersionSelectorService.ActivateTSVersion(version);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(setActiveCommand);

        var activateLicenseCommand = new Command("activate-license", "Activate license");
        activateLicenseCommand.Add(verboseOption);
        var activateLicenseSerialArg = new Argument<string>("serial-number") { Description = "Serial number for activation" };
        activateLicenseCommand.Add(activateLicenseSerialArg);
        activateLicenseCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var serialNumber = parseResult.GetValue(activateLicenseSerialArg);
            ConsoleLogger.Information("Serial number to activate: {0}", serialNumber);

            LicenseManagerService.ActivateLicenseKey(serialNumber);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(activateLicenseCommand);

        var checkLicenseCommand = new Command("check-license", "Check license");
        checkLicenseCommand.Add(verboseOption);
        checkLicenseCommand.SetAction(parseResult =>
        {
            SetVerbose(parseResult, verboseOption);
            var license = TestStandService.ReadLicense();
            ConsoleLogger.Information("{0}", license);
            Environment.Exit(Environment.ExitCode);
        });
        rootCommand.Add(checkLicenseCommand);

        return rootCommand.Parse(args).Invoke();
    }

    private static void SetVerbose(ParseResult parseResult, Option<bool> verboseOption)
    {
        ConsoleLogger.Verbose = parseResult.GetValue(verboseOption);
    }
}
