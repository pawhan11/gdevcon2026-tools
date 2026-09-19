namespace vipm2.Installation;

/// <summary>
/// Copies merged vi.lib and user.lib from the extraction output into the LabVIEW installation directory.
/// All existing files are overwritten on conflict.
/// </summary>
public static class LabViewCatalogCopier
{
    private const string DefaultLabViewBasePath = @"C:\Program Files\National Instruments";
    private const string ViLibDir = "vi.lib";
    private const string UserLibDir = "user.lib";

    /// <summary>
    /// Gets the LabVIEW root directory for the given version (e.g. "2026" -> "C:\Program Files\National Instruments\LabVIEW 2026").
    /// </summary>
    /// <param name="labviewVersion">Version from TOML (e.g. "2026" or "LabVIEW 2026").</param>
    /// <param name="basePath">Optional base path; defaults to "C:\Program Files\National Instruments".</param>
    public static string GetLabViewRootPath(string labviewVersion, string? basePath = null)
    {
        var baseDir = basePath ?? DefaultLabViewBasePath;
        var version = labviewVersion?.Trim() ?? "";
        if (version.Length == 0)
            throw new ArgumentException("LabVIEW version is required.", nameof(labviewVersion));

        var folderName = version.Contains("LabVIEW", StringComparison.OrdinalIgnoreCase)
            ? version
            : "LabVIEW " + version;
        return Path.Combine(baseDir, folderName);
    }

    /// <summary>
    /// Copies vi.lib and user.lib from the merged directory into the LabVIEW root, overwriting existing files.
    /// </summary>
    /// <param name="mergedDir">Path to the merged output (contains vi.lib and user.lib subdirectories).</param>
    /// <param name="labviewRoot">LabVIEW installation root (e.g. C:\Program Files\National Instruments\LabVIEW 2026).</param>
    public static void CopyMergedToLabView(string mergedDir, string labviewRoot)
    {
        if (string.IsNullOrWhiteSpace(mergedDir) || !Directory.Exists(mergedDir))
            throw new DirectoryNotFoundException($"Merged directory not found: {mergedDir}");
        if (string.IsNullOrWhiteSpace(labviewRoot))
            throw new ArgumentException("LabVIEW root path is required.", nameof(labviewRoot));

        foreach (var subDir in new[] { ViLibDir, UserLibDir })
        {
            var sourceDir = Path.Combine(mergedDir, subDir);
            if (!Directory.Exists(sourceDir))
                continue;

            var destDir = Path.Combine(labviewRoot, subDir);
            CopyDirectory(sourceDir, destDir, overwrite: true);
        }
    }

    /// <summary>
    /// Recursively copies a directory to the destination, overwriting files if <paramref name="overwrite"/> is true.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir, bool overwrite)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.EnumerateFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite);
        }

        foreach (var subDir in Directory.EnumerateDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
            CopyDirectory(subDir, destSubDir, overwrite);
        }
    }
}
