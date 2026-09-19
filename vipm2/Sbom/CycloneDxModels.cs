using System.Text.Json.Serialization;

namespace vipm2.Sbom;

// Minimal CycloneDX 1.7 JSON model (only the subset vipm2 emits).
// Field names follow https://cyclonedx.org/docs/1.7/json/.

public sealed class CycloneDxBom
{
    [JsonPropertyName("$schema")] public string Schema { get; init; } = "http://cyclonedx.org/schema/bom-1.7.schema.json";
    [JsonPropertyName("bomFormat")] public string BomFormat { get; init; } = "CycloneDX";
    [JsonPropertyName("specVersion")] public string SpecVersion { get; init; } = "1.7";
    [JsonPropertyName("serialNumber")] public string SerialNumber { get; init; } = "urn:uuid:" + Guid.NewGuid().ToString("D");
    [JsonPropertyName("version")] public int Version { get; init; } = 1;
    [JsonPropertyName("metadata")] public CycloneDxMetadata? Metadata { get; init; }
    [JsonPropertyName("components")] public List<CycloneDxComponent> Components { get; init; } = new();
    [JsonPropertyName("dependencies")] public List<CycloneDxDependency> Dependencies { get; init; } = new();
}

public sealed class CycloneDxMetadata
{
    /// <summary>ISO 8601 UTC timestamp.</summary>
    [JsonPropertyName("timestamp")] public string Timestamp { get; init; } = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'");
    [JsonPropertyName("tools")] public CycloneDxTools? Tools { get; init; }
    [JsonPropertyName("component")] public CycloneDxComponent? Component { get; init; }
    [JsonPropertyName("properties")] public List<CycloneDxProperty>? Properties { get; init; }
}

public sealed class CycloneDxTools
{
    [JsonPropertyName("components")] public List<CycloneDxComponent> Components { get; init; } = new();
}

public sealed class CycloneDxComponent
{
    /// <summary>application | library | framework | ...</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("bom-ref")] public string? BomRef { get; init; }
    [JsonPropertyName("supplier")] public CycloneDxOrganization? Supplier { get; init; }
    [JsonPropertyName("publisher")] public string? Publisher { get; init; }
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("version")] public string? Version { get; init; }
    [JsonPropertyName("description")] public string? Description { get; init; }
    [JsonPropertyName("hashes")] public List<CycloneDxHash>? Hashes { get; init; }
    [JsonPropertyName("licenses")] public List<CycloneDxLicenseChoice>? Licenses { get; init; }
    [JsonPropertyName("copyright")] public string? Copyright { get; init; }
    [JsonPropertyName("purl")] public string? Purl { get; init; }
    [JsonPropertyName("externalReferences")] public List<CycloneDxExternalReference>? ExternalReferences { get; init; }
    [JsonPropertyName("properties")] public List<CycloneDxProperty>? Properties { get; init; }
}

public sealed class CycloneDxOrganization
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("url")] public List<string>? Url { get; init; }
    [JsonPropertyName("contact")] public List<CycloneDxContact>? Contact { get; init; }
}

public sealed class CycloneDxContact
{
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("email")] public string? Email { get; init; }
}

public sealed class CycloneDxHash
{
    /// <summary>MD5 | SHA-1 | SHA-256 | ...</summary>
    [JsonPropertyName("alg")] public required string Alg { get; init; }
    [JsonPropertyName("content")] public required string Content { get; init; }
}

public sealed class CycloneDxLicenseChoice
{
    [JsonPropertyName("license")] public CycloneDxLicense? License { get; init; }
    /// <summary>SPDX expression; mutually exclusive with <see cref="License"/>.</summary>
    [JsonPropertyName("expression")] public string? Expression { get; init; }
}

public sealed class CycloneDxLicense
{
    /// <summary>SPDX license id (e.g. BSD-3-Clause). Mutually exclusive with <see cref="Name"/>.</summary>
    [JsonPropertyName("id")] public string? Id { get; init; }
    /// <summary>Free-text license name when no SPDX id applies.</summary>
    [JsonPropertyName("name")] public string? Name { get; init; }
    [JsonPropertyName("url")] public string? Url { get; init; }
}

public sealed class CycloneDxExternalReference
{
    /// <summary>website | distribution | documentation | license | vcs | ...</summary>
    [JsonPropertyName("type")] public required string Type { get; init; }
    [JsonPropertyName("url")] public required string Url { get; init; }
    [JsonPropertyName("comment")] public string? Comment { get; init; }
}

public sealed class CycloneDxProperty
{
    [JsonPropertyName("name")] public required string Name { get; init; }
    [JsonPropertyName("value")] public required string Value { get; init; }
}

public sealed class CycloneDxDependency
{
    [JsonPropertyName("ref")] public required string Ref { get; init; }
    [JsonPropertyName("dependsOn")] public List<string>? DependsOn { get; init; }
}
