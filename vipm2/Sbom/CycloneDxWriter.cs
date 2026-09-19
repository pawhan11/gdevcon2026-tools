using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace vipm2.Sbom;

/// <summary>
/// Serializes a <see cref="CycloneDxBom"/> to indented CycloneDX 1.7 JSON.
/// </summary>
public static class CycloneDxWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        // Keep URLs, apostrophes and non-ASCII text readable; the document is not embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string Serialize(CycloneDxBom bom) => JsonSerializer.Serialize(bom, Options);

    public static async Task WriteAsync(CycloneDxBom bom, string outputPath, CancellationToken cancellationToken = default)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(outputPath, Serialize(bom) + Environment.NewLine, cancellationToken).ConfigureAwait(false);
    }
}
