namespace vipm2.Models;

/// <summary>
/// An installed NI Package Manager package as reported by <c>nipkg info-installed</c>.
/// </summary>
public sealed class NipkgPackage
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public string? Architecture { get; init; }
    public string? Description { get; init; }
    public string? DisplayName { get; init; }
    public string? DisplayVersion { get; init; }
    public string? Homepage { get; init; }
    /// <summary>Raw Maintainer field, e.g. "National Instruments &lt;support@ni.com&gt;".</summary>
    public string? Maintainer { get; init; }
    public string? Section { get; init; }
    public string? SourceFeed { get; init; }
    public string? Eula { get; init; }
    public string? Md5 { get; init; }
    public string? Sha256 { get; init; }
    /// <summary>Raw Depends field, e.g. "ni-labview-runtime-engine,ni-certificates (&gt;= 24.5.1)".</summary>
    public string? Depends { get; init; }
    /// <summary>Raw Provides field, e.g. "jki-dragon (= 2026.3.0-897)".</summary>
    public string? Provides { get; init; }
}
