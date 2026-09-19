namespace vipm2.Models;

/// <summary>
/// Metadata parsed from a VI Package .spec file ([Package] and [Description] sections).
/// </summary>
public sealed class VipSpec
{
    public string? Name { get; init; }
    public string? Version { get; init; }
    public string? DisplayName { get; init; }
    public string? Description { get; init; }
    public string? Summary { get; init; }
    /// <summary>Free-text license (often an SPDX id such as BSD-3-Clause, but not guaranteed).</summary>
    public string? License { get; init; }
    public string? Copyright { get; init; }
    public string? Vendor { get; init; }
    public string? Packager { get; init; }
    public string? Url { get; init; }
}
