using vipm2.Parsing;

namespace vipm2.Models;

/// <summary>
/// Data for a single package version from the VIPR index.
/// </summary>
public sealed class ViprPackageEntry
{
    public string? PackageUrl { get; init; }
    public string? IconUrl { get; init; }
    public string? SpecUrl { get; init; }
    public string? PackageMd5 { get; init; }
    public string? PackageReleaseDate { get; init; }
    public string? PlatformExclusiveLabViewVersion { get; init; }
    public string? PlatformExclusiveOs { get; init; }
    public string? DependenciesRequires { get; init; }
    public string? PackageDisplayName { get; init; }
    public string? PlatformExclusiveVipmVersion { get; init; }

    internal static ViprPackageEntry FromSection(Dictionary<string, string> section)
    {
        return new ViprPackageEntry
        {
            PackageUrl = Get(section, "Package.URL"),
            IconUrl = Get(section, "Icon.URL"),
            SpecUrl = Get(section, "Spec.URL"),
            PackageMd5 = Get(section, "Package.MD5"),
            PackageReleaseDate = Get(section, "Package.Release Date"),
            PlatformExclusiveLabViewVersion = Get(section, "Platform.Exclusive_LabVIEW_Version"),
            PlatformExclusiveOs = Get(section, "Platform.Exclusive_OS"),
            DependenciesRequires = Get(section, "Dependencies.Requires"),
            PackageDisplayName = Get(section, "Package.Display Name"),
            PlatformExclusiveVipmVersion = Get(section, "Platform.Exclusive_VIPM_Version")
        };
    }

    private static string? Get(Dictionary<string, string> section, string key)
    {
        return section.TryGetValue(key, out var v) ? v : null;
    }
}

/// <summary>
/// VIPR package index: package name -> version -> package entry.
/// </summary>
public sealed class ViprPackageIndex
{
    private const string PackageSectionPrefix = "Package ";

    /// <summary>
    /// Packages by name, then by version.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, ViprPackageEntry>> Packages { get; }

    public ViprPackageIndex(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ViprPackageEntry>> packages)
    {
        Packages = packages;
    }

    internal static ViprPackageIndex FromParsedSections(Dictionary<string, Dictionary<string, string>> parsed)
    {
        // Ordered: package names sorted (case-insensitive), versions sorted (numeric then "latest" last)
        var byName = new SortedDictionary<string, SortedDictionary<string, ViprPackageEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var (sectionName, section) in parsed)
        {
            if (!sectionName.StartsWith(PackageSectionPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var id = sectionName.Substring(PackageSectionPrefix.Length);
            var (name, version) = SplitNameVersion(id);
            if (name is null || version is null)
                continue;

            if (!byName.TryGetValue(name, out var versions))
            {
                versions = new SortedDictionary<string, ViprPackageEntry>(VersionKeyComparer.Instance);
                byName[name] = versions;
            }
            versions[version] = ViprPackageEntry.FromSection(section);
        }

        foreach (var versions in byName.Values)
        {
            var numericVersions = versions.Keys
                .Where(v => !string.Equals(v, "latest", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (numericVersions.Count > 0)
            {
                var highest = numericVersions.MaxBy(v => v, VersionComparer.Instance)!;
                versions["latest"] = versions[highest];
            }
        }

        // Preserve iteration order: copy in sorted order (Dictionary preserves insertion order in .NET Core 3+)
        var ordered = new Dictionary<string, IReadOnlyDictionary<string, ViprPackageEntry>>(byName.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var kv in byName)
            ordered[kv.Key] = kv.Value;
        return new ViprPackageIndex(ordered);
    }

    /// <summary>
    /// Splits "packageName-version" into (name, version). Version may contain hyphens (e.g. 1.1-1),
    /// so we use the first hyphen where the part after it starts with a digit.
    /// </summary>
    private static (string? name, string? version) SplitNameVersion(string id)
    {
        for (var i = 0; i < id.Length; i++)
        {
            if (id[i] == '-' && i + 1 < id.Length && char.IsDigit(id[i + 1]))
                return (id[..i], id[(i + 1)..]);
        }
        return (null, null);
    }

    /// <summary>
    /// Resolves the version key to use for a dependency (highest version that satisfies minVersion, or "latest").
    /// </summary>
    public string ResolveDependencyVersion(string packageName, string? minVersion)
    {
        if (!Packages.TryGetValue(packageName, out var versions))
            return "latest";
        if (string.IsNullOrEmpty(minVersion))
            return "latest";
        var candidates = versions.Keys
            .Where(v => !string.Equals(v, "latest", StringComparison.OrdinalIgnoreCase))
            .Where(v => VersionComparer.Instance.Compare(v, minVersion) >= 0)
            .ToList();
        if (candidates.Count == 0)
            return "latest";
        return candidates.MaxBy(v => v, VersionComparer.Instance)!;
    }

    /// <summary>
    /// Collects the set of (packageName, version) for the given package and all transitive dependencies.
    /// </summary>
    public IReadOnlyCollection<(string PackageName, string Version)> GetPackageAndDependencies(string packageName, string? version = null)
    {
        var versionKey = version ?? "latest";
        var set = new HashSet<(string, string)>(PackageVersionComparer.Instance);
        CollectPackageAndDependencies(set, packageName, versionKey);
        return set;
    }

    private void CollectPackageAndDependencies(HashSet<(string, string)> set, string packageName, string versionKey)
    {
        if (!Packages.TryGetValue(packageName, out var versions) || !versions.TryGetValue(versionKey, out var entry))
            throw new InvalidOperationException($"Package or dependency not found in index: {packageName} {versionKey}");
        var key = (packageName, versionKey);
        if (!set.Add(key))
            return;
        foreach (var (depName, minVer) in DependencyParser.Parse(entry.DependenciesRequires))
        {
            if (string.IsNullOrWhiteSpace(depName))
                continue;
            var resolved = ResolveDependencyVersion(depName, minVer);
            CollectPackageAndDependencies(set, depName, resolved);
        }
    }

    private sealed class PackageVersionComparer : IEqualityComparer<(string, string)>
    {
        internal static readonly PackageVersionComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }

    private sealed class VersionComparer : IComparer<string>
    {
        internal static readonly VersionComparer Instance = new();

        public int Compare(string? a, string? b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;
            var partsA = a.Split('.');
            var partsB = b.Split('.');
            var max = Math.Max(partsA.Length, partsB.Length);
            for (var i = 0; i < max; i++)
            {
                var numA = i < partsA.Length && int.TryParse(partsA[i], out var na) ? na : 0;
                var numB = i < partsB.Length && int.TryParse(partsB[i], out var nb) ? nb : 0;
                if (numA != numB) return numA.CompareTo(numB);
            }
            return 0;
        }
    }

    /// <summary>Sorts version keys with "latest" last; other keys by VersionComparer.</summary>
    private sealed class VersionKeyComparer : IComparer<string>
    {
        internal static readonly VersionKeyComparer Instance = new();
        private const string LatestKey = "latest";

        public int Compare(string? a, string? b)
        {
            var aIsLatest = string.Equals(a, LatestKey, StringComparison.OrdinalIgnoreCase);
            var bIsLatest = string.Equals(b, LatestKey, StringComparison.OrdinalIgnoreCase);
            if (aIsLatest && bIsLatest) return 0;
            if (aIsLatest) return 1;
            if (bIsLatest) return -1;
            return VersionComparer.Instance.Compare(a, b);
        }
    }
}
