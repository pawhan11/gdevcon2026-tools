using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using vipm2.Models;

namespace vipm2.Nipkg;

/// <summary>
/// Thin wrapper around the NI Package Manager command line (nipkg.exe).
/// </summary>
public static class NipkgCli
{
    private const string ExeName = "nipkg.exe";
    public static string DefaultDirectory { get; } = @"C:\Program Files\National Instruments\NI Package Manager";

    /// <summary>
    /// Locates nipkg.exe: explicit path, then PATH, then the default install directory. Returns null when not found.
    /// </summary>
    public static string? Locate(string? explicitPath = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitPath))
            return File.Exists(explicitPath) ? explicitPath : null;

        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim().Trim('"'), ExeName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch (ArgumentException)
            {
                // invalid characters in a PATH entry; skip
            }
        }

        var fallback = Path.Combine(DefaultDirectory, ExeName);
        return File.Exists(fallback) ? fallback : null;
    }

    /// <summary>
    /// Runs <c>nipkg info-installed</c> once and returns the parsed packages, optionally filtered by name patterns.
    /// (info-installed itself accepts at most one PATTERN, so filtering is done here.)
    /// </summary>
    /// <param name="nipkgPath">Full path to nipkg.exe (see <see cref="Locate"/>).</param>
    /// <param name="patterns">Optional Unix-style name patterns (*, ?, [seq], [!seq]), e.g. "ni-teststand-*"; a package is kept when it matches any. None means all installed packages.</param>
    public static IReadOnlyList<NipkgPackage> ListInstalled(string nipkgPath, IEnumerable<string>? patterns = null)
    {
        var regexes = (patterns ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => GlobToRegex(p.Trim()))
            .ToList();

        var all = RunInfoInstalled(nipkgPath);
        if (regexes.Count == 0)
            return all;
        return all.Where(pkg => regexes.Any(r => r.IsMatch(pkg.Name))).ToList();
    }

    /// <summary>Converts a Unix-style pattern as documented by nipkg (*, ?, [seq], [!seq]) into an anchored, case-insensitive regex.</summary>
    public static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (var i = 0; i < glob.Length; i++)
        {
            var c = glob[i];
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                case '[':
                    var end = glob.IndexOf(']', i + 1);
                    if (end < 0) { sb.Append("\\["); break; }
                    var set = glob[(i + 1)..end];
                    if (set.StartsWith('!')) set = "^" + set[1..];
                    sb.Append('[').Append(set.Replace("\\", "\\\\")).Append(']');
                    i = end;
                    break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IReadOnlyList<NipkgPackage> RunInfoInstalled(string nipkgPath)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = nipkgPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8
        };
        startInfo.ArgumentList.Add("info-installed");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start nipkg process.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"nipkg info-installed failed with exit code {process.ExitCode}: {stderr.Trim()}");

        return NipkgControlParser.Parse(stdout);
    }
}
