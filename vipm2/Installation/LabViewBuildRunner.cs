namespace vipm2.Installation;

/// <summary>
/// Runs the LabVIEW Command Line Interface (CLI) ExecuteBuildSpec operation.
/// See: https://labviewwiki.org/wiki/ExecuteBuildSpec
/// </summary>
public static class LabViewBuildRunner
{
    private const string CliFileName = "LabVIEWCLI.exe";

    /// <summary>
    /// Default directory for the shared LabVIEW CLI installation.
    /// </summary>
    public static string DefaultCliDirectory { get; } = @"C:\Program Files (x86)\National Instruments\Shared\LabVIEW CLI";

    /// <summary>
    /// Runs ExecuteBuildSpec for the given project. Uses the shared LabVIEW CLI and always runs headless.
    /// </summary>
    /// <param name="projectPath">Path to the .lvproj file (will be resolved to absolute).</param>
    /// <param name="labviewVersion">LabVIEW version (e.g. "2026").</param>
    /// <param name="labviewBitness">32 or 64.</param>
    /// <param name="targetName">Optional target (e.g. "My Computer").</param>
    /// <param name="buildSpecName">Optional build specification name; when null, all build specs under the target are built.</param>
    /// <param name="cliDirectory">Optional CLI directory; defaults to <see cref="DefaultCliDirectory"/>.</param>
    /// <returns>Exit code from LabVIEWCLI.</returns>
    public static int ExecuteBuildSpec(
        string projectPath,
        string labviewVersion,
        int labviewBitness,
        string? targetName = null,
        string? buildSpecName = null,
        string? cliDirectory = null)
    {
        var cliDir = cliDirectory ?? DefaultCliDirectory;
        var cliPath = Path.Combine(cliDir, CliFileName);

        if (!File.Exists(cliPath))
        {
            throw new FileNotFoundException($"LabVIEW CLI not found: {cliPath}. Ensure the NI LabVIEW Command Line Interface add-on is installed.");
        }

        var fullProjectPath = Path.GetFullPath(projectPath);
        if (!File.Exists(fullProjectPath))
        {
            throw new FileNotFoundException($"Project file not found: {fullProjectPath}");
        }

        var labviewBasePath = labviewBitness == 64
            ? @"C:\Program Files\National Instruments"
            : @"C:\Program Files (x86)\National Instruments";
        var labviewRoot = LabViewCatalogCopier.GetLabViewRootPath(labviewVersion, labviewBasePath);
        var labviewExePath = Path.Combine(labviewRoot, "labview.exe");

        var args = new List<string>
        {
            "-OperationName", "ExecuteBuildSpec",
            "-ProjectPath", fullProjectPath,
            "-LabVIEWPath", labviewExePath
        };

        if (!string.IsNullOrWhiteSpace(targetName))
            args.AddRange(new[] { "-TargetName", targetName!.Trim() });
        if (!string.IsNullOrWhiteSpace(buildSpecName))
            args.AddRange(new[] { "-BuildSpecName", buildSpecName!.Trim() });

        args.Add("-Headless");

        var argsLine = string.Join(" ", args.Select(a => a.Contains(' ') ? "\"" + a + "\"" : a));
        Console.WriteLine($"LabVIEWCLI: {cliPath} {argsLine}");

        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = cliPath
        };
        foreach (var arg in args)
            startInfo.ArgumentList.Add(arg);

        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;

        using var process = System.Diagnostics.Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start LabVIEWCLI process.");
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (stdout.Length > 0)
            Console.WriteLine(stdout);
        if (stderr.Length > 0)
            Console.Error.WriteLine(stderr);

        return process.ExitCode;
    }
}
