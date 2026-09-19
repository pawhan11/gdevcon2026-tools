namespace vipm2.Parsing;

/// <summary>
/// Parses Dependencies.Requires from VIPR index (e.g. "name>=ver,name2>=ver2").
/// </summary>
public static class DependencyParser
{
    /// <summary>
    /// Parses a Dependencies.Requires string into (package name, optional minimum version).
    /// </summary>
    /// <param name="requires">Value of Dependencies.Requires, or null/empty.</param>
    /// <returns>List of (packageName, minVersion); minVersion is null when no constraint.</returns>
    public static IReadOnlyList<(string PackageName, string? MinVersion)> Parse(string? requires)
    {
        if (string.IsNullOrWhiteSpace(requires))
            return Array.Empty<(string, string?)>();

        var list = new List<(string, string?)>();
        foreach (var token in requires.Split(','))
        {
            var t = token.Trim();
            if (string.IsNullOrEmpty(t))
                continue;

            var idx = t.IndexOf(">=", StringComparison.Ordinal);
            if (idx >= 0)
            {
                list.Add((t[..idx].Trim(), t[(idx + 2)..].Trim()));
                continue;
            }
            idx = t.IndexOf('=');
            if (idx >= 0)
            {
                list.Add((t[..idx].Trim(), t[(idx + 1)..].Trim()));
                continue;
            }
            list.Add((t, null));
        }
        return list;
    }
}
