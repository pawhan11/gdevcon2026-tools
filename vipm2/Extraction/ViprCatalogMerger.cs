using System.IO.Compression;

namespace vipm2.Extraction;

/// <summary>
/// Extracts all .zip packages in a catalog directory and merges vi.lib and user.lib
/// from each into common output vi.lib and user.lib folders.
/// </summary>
public static class ViprCatalogMerger
{
    private const string ViLibPrefix = "vi.lib/";
    private const string UserLibPrefix = "user.lib/";

    /// <summary>
    /// Extracts every .zip in <paramref name="catalogDir"/> and merges all vi.lib and user.lib
    /// contents into <paramref name="outputDir"/>/vi.lib and .../user.lib (overwrites on path collision).
    /// </summary>
    /// <returns>Path to the output directory (same as <paramref name="outputDir"/>).</returns>
    public static string ExtractAndMergeLibs(string catalogDir, string? outputDir = null, CancellationToken cancellationToken = default)
    {
        var outDir = outputDir ?? Path.Combine(catalogDir, "merged");
        var viLibDir = Path.Combine(outDir, "vi.lib");
        var userLibDir = Path.Combine(outDir, "user.lib");
        Directory.CreateDirectory(viLibDir);
        Directory.CreateDirectory(userLibDir);

        var zipFiles = Directory.EnumerateFiles(catalogDir, "*.zip").ToList();
        foreach (var zipPath in zipFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            MergeLibsFromZip(zipPath, viLibDir, userLibDir);
        }

        return outDir;
    }

    private static void MergeLibsFromZip(string zipPath, string viLibDir, string userLibDir)
    {
        using var stream = File.OpenRead(zipPath);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        foreach (var entry in archive.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;

            var full = entry.FullName.Replace('\\', '/').TrimEnd('/');
            string? relativePath = null;
            string? baseDir = null;

            // vi.lib and user.lib may be at root or in a subfolder (e.g. PackageName/vi.lib/...)
            var viIdx = full.IndexOf(ViLibPrefix, StringComparison.OrdinalIgnoreCase);
            var userIdx = full.IndexOf(UserLibPrefix, StringComparison.OrdinalIgnoreCase);

            if (viIdx >= 0 && (userIdx < 0 || viIdx <= userIdx))
            {
                relativePath = full.Substring(viIdx + ViLibPrefix.Length);
                baseDir = viLibDir;
            }
            else if (userIdx >= 0)
            {
                relativePath = full.Substring(userIdx + UserLibPrefix.Length);
                baseDir = userLibDir;
            }

            if (relativePath == null || baseDir == null || relativePath.Length == 0)
                continue;

            if (relativePath.Contains("..", StringComparison.Ordinal))
                continue;

            var relativeNormalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
            var destPath = Path.Combine(baseDir, relativeNormalized);
            var destDir = Path.GetDirectoryName(destPath);
            if (string.IsNullOrEmpty(destDir))
                continue;

            Directory.CreateDirectory(destDir);
            try
            {
                entry.ExtractToFile(destPath, overwrite: true);
            }
            catch
            {
                // Skip if extract fails (e.g. directory entry)
            }
        }
    }
}
