using vipm2.Models;

namespace vipm2.Parsing;

/// <summary>
/// Parses a VI Package .spec file (INI-style sections with quoted values) into <see cref="VipSpec"/>.
/// </summary>
public static class VipSpecParser
{
    public static VipSpec Parse(string content)
    {
        var sections = ViprIndexParser.Parse(content);
        sections.TryGetValue("Package", out var package);
        sections.TryGetValue("Description", out var description);

        return new VipSpec
        {
            Name = Get(package, "Name"),
            Version = Get(package, "Version"),
            DisplayName = Get(package, "Display Name"),
            Description = Get(description, "Description"),
            Summary = Get(description, "Summary"),
            License = Get(description, "License"),
            Copyright = Get(description, "Copyright"),
            Vendor = Get(description, "Vendor"),
            Packager = Get(description, "Packager"),
            Url = Get(description, "URL")
        };
    }

    /// <summary>
    /// Returns the value with surrounding double quotes removed and whitespace trimmed; null when empty.
    /// Spec values are written as Key="value" and may contain leading tabs inside the quotes (e.g. License="\tBSD-3-Clause").
    /// </summary>
    private static string? Get(Dictionary<string, string>? section, string key)
    {
        if (section == null || !section.TryGetValue(key, out var raw))
            return null;
        var v = raw.Trim();
        if (v.Length >= 2 && v[0] == '"' && v[^1] == '"')
            v = v[1..^1];
        v = v.Trim();
        return v.Length == 0 ? null : v;
    }
}
