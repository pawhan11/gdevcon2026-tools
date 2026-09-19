namespace vipm2.Models;

/// <summary>
/// Parsed project TOML: [project] section and [dependencies] (name -> version).
/// </summary>
public sealed class ProjectToml
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? LabViewVersion { get; init; }
    /// <summary>Package name -> version (e.g. oglib_array -> 6.0.1.20).</summary>
    public IReadOnlyDictionary<string, string> Dependencies { get; init; } = new Dictionary<string, string>();
}
