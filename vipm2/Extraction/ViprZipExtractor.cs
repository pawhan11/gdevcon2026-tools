using System.IO.Compression;
using System.Text;

namespace vipm2.Extraction;

/// <summary>
/// Extracts index files from VIPR/OGPD zip archives in memory.
/// </summary>
public static class ViprZipExtractor
{
    private const string IndexViprEntryName = "index.vipr";
    private const string JkisoftOgpfEntryName = "jkisoft.ogpd";

    /// <summary>
    /// Extracts the index.vipr entry from the zip bytes and returns its text content (UTF-8).
    /// </summary>
    public static string ExtractIndexViprContent(byte[] zipBytes) =>
        ExtractZipEntryContent(zipBytes, IndexViprEntryName);

    /// <summary>
    /// Extracts the jkisoft.ogpf entry from the zip bytes and returns its text content (UTF-8).
    /// Same syntax as index.vipr (INI-style sections).
    /// </summary>
    public static string ExtractJkisoftOgpfContent(byte[] zipBytes) =>
        ExtractZipEntryContent(zipBytes, JkisoftOgpfEntryName);

    /// <summary>
    /// Extracts an entry by file name from the zip and returns its text content (UTF-8).
    /// </summary>
    /// <param name="zipBytes">Raw bytes of the zip file.</param>
    /// <param name="entryFileName">Entry name (e.g. index.vipr, jkisoft.ogpf).</param>
    /// <exception cref="InvalidOperationException">When the zip has no such entry.</exception>
    public static string ExtractZipEntryContent(byte[] zipBytes, string entryFileName)
    {
        using var stream = new MemoryStream(zipBytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var entry = archive.GetEntry(entryFileName)
            ?? archive.Entries.FirstOrDefault(e => string.Equals(e.Name, entryFileName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Zip archive does not contain an entry named '{entryFileName}'.");

        using var entryStream = entry.Open();
        using var reader = new StreamReader(entryStream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
