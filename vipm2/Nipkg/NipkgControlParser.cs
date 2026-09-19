using System.Text;
using System.Text.RegularExpressions;
using vipm2.Models;

namespace vipm2.Nipkg;

/// <summary>
/// Parses the Debian-control-style output of <c>nipkg info-installed</c>:
/// records separated by blank lines, "Key: value" fields, continuation lines indented with a space,
/// and " ." for an empty continuation line.
/// </summary>
public static partial class NipkgControlParser
{
    public static IReadOnlyList<NipkgPackage> Parse(string output)
    {
        var packages = new List<NipkgPackage>();
        var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? currentKey = null;
        var currentValue = new StringBuilder();

        void CommitField()
        {
            if (currentKey != null)
                fields[currentKey] = currentValue.ToString();
            currentKey = null;
            currentValue.Clear();
        }

        void CommitRecord()
        {
            CommitField();
            if (fields.TryGetValue("Package", out var name) && !string.IsNullOrWhiteSpace(name))
                packages.Add(ToPackage(fields));
            fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0)
            {
                CommitRecord();
                continue;
            }

            if (line[0] == ' ' || line[0] == '\t')
            {
                // continuation of the previous field
                if (currentKey == null)
                    continue;
                var cont = line.Trim();
                currentValue.Append('\n').Append(cont == "." ? "" : cont);
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            CommitField();
            currentKey = line[..colon].Trim();
            currentValue.Append(line[(colon + 1)..].Trim());
        }
        CommitRecord();

        return packages;
    }

    private static NipkgPackage ToPackage(Dictionary<string, string> f)
    {
        string? Get(string key) => f.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? Unescape(v.Trim()) : null;

        return new NipkgPackage
        {
            Name = Get("Package")!,
            Version = Get("Version") ?? "",
            Architecture = Get("Architecture"),
            Description = Get("Description"),
            DisplayName = Get("DisplayName"),
            DisplayVersion = Get("DisplayVersion"),
            Homepage = Get("Homepage"),
            Maintainer = Get("Maintainer"),
            Section = Get("Section"),
            SourceFeed = Get("SourceFeed"),
            Eula = Get("Eula"),
            Md5 = Get("MD5sum") ?? Get("MD5Sum"),
            Sha256 = Get("SHA256"),
            Depends = Get("Depends"),
            Provides = Get("Provides")
        };
    }

    /// <summary>nipkg prints some characters as \xNN escapes (e.g. an apostrophe as \x27); decode them.</summary>
    private static string Unescape(string value) =>
        HexEscape().Replace(value, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    [GeneratedRegex(@"\\x([0-9A-Fa-f]{2})")]
    private static partial Regex HexEscape();

    /// <summary>
    /// Splits a Depends/Provides style relation list ("a (&gt;= 1.0), b, c:windows_x64 (= 2.0), d | e") into bare package names.
    /// </summary>
    public static IReadOnlyList<string> ParsePackageNames(string? relation)
    {
        if (string.IsNullOrWhiteSpace(relation))
            return Array.Empty<string>();
        var names = new List<string>();
        foreach (var token in relation.Split(','))
        {
            foreach (var alt in token.Split('|'))
            {
                var a = alt.Trim();
                var paren = a.IndexOf('(');
                if (paren >= 0) a = a[..paren].Trim();
                var arch = a.IndexOf(':');
                if (arch >= 0) a = a[..arch].Trim();
                var space = a.IndexOf(' ');
                if (space >= 0) a = a[..space].Trim();
                if (a.Length > 0) names.Add(a);
            }
        }
        return names;
    }
}
