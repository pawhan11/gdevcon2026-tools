using System.Reflection;
using System.Text.RegularExpressions;
using vipm2.Clients;
using vipm2.Models;
using vipm2.Nipkg;
using vipm2.Parsing;

namespace vipm2.Sbom;

/// <summary>
/// Options for <see cref="SbomGenerator"/>.
/// </summary>
public sealed class SbomOptions
{
    public required string TomlPath { get; init; }
    /// <summary>Include transitive VIPM dependencies resolved from the VIPR index (default true).</summary>
    public bool IncludeTransitive { get; init; } = true;
    /// <summary>Download each package's .spec file for license, vendor, copyright and URL (default true).</summary>
    public bool FetchSpecs { get; init; } = true;
    /// <summary>Include installed NI Package Manager packages via <c>nipkg info-installed</c> (default true).</summary>
    public bool IncludeNipkg { get; init; } = true;
    /// <summary>Optional nipkg name patterns (e.g. "ni-teststand-*"); empty means all installed packages.</summary>
    public IReadOnlyList<string> NipkgPatterns { get; init; } = Array.Empty<string>();
    /// <summary>Explicit path to nipkg.exe; null resolves via PATH and the default install directory.</summary>
    public string? NipkgPath { get; init; }
    public string? ProductName { get; init; }
    public string? ProductVersion { get; init; }
    public string? ProductSupplier { get; init; }
}

/// <summary>
/// Builds a CycloneDX SBOM for a project TOML: VIPM packages from [dependencies] (plus transitive dependencies resolved
/// through the VIPR index) and the NI packages installed on this machine as reported by nipkg.
/// The component layout mirrors what the JKI VIPM CLI emits (purl types pkg:vipm and pkg:nipkg) so both outputs can be merged.
/// </summary>
public sealed partial class SbomGenerator
{
    private const string LatestKey = "latest";
    private const int MaxParallelSpecDownloads = 8;

    private readonly IViprZipDownloadClient _client;
    private readonly TextWriter _log;

    public SbomGenerator(IViprZipDownloadClient client, TextWriter? log = null)
    {
        _client = client;
        _log = log ?? Console.Out;
    }

    public async Task<CycloneDxBom> GenerateAsync(SbomOptions options, CancellationToken cancellationToken = default)
    {
        var project = ProjectTomlLoader.Load(options.TomlPath);
        var productName = options.ProductName ?? project.Name ?? Path.GetFileNameWithoutExtension(options.TomlPath);
        var productVersion = options.ProductVersion ?? project.Version ?? "0.0.0";
        var rootRef = Purl("vipm", productName, productVersion);

        var components = new List<CycloneDxComponent>();
        var dependencies = new List<CycloneDxDependency>();
        var rootDependsOn = new List<string>();

        // --- VIPM packages ---------------------------------------------------------------------------------------
        _log.WriteLine("Downloading and parsing VIPR index...");
        var index = await _client.GetPackageIndexAsync(cancellationToken).ConfigureAwait(false);
        _log.WriteLine($"Parsed {index.Packages.Count} packages.");

        var (vipmSet, directRefs, unresolved) = ResolveVipmPackages(index, project, options.IncludeTransitive);
        rootDependsOn.AddRange(directRefs);
        _log.WriteLine($"Resolved {vipmSet.Count} VIPM package(s) ({directRefs.Count} direct).");

        var specs = new Dictionary<(string, string), VipSpec?>(PackageVersionComparer.Instance);
        if (options.FetchSpecs)
        {
            _log.WriteLine("Downloading package spec files...");
            var lock_ = new object();
            await Parallel.ForEachAsync(vipmSet,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelSpecDownloads, CancellationToken = cancellationToken },
                async (item, ct) =>
                {
                    VipSpec? spec = null;
                    try
                    {
                        var text = await _client.DownloadSpecAsync(index, item.Name, item.VersionKey, ct).ConfigureAwait(false);
                        if (text != null)
                            spec = VipSpecParser.Parse(text);
                    }
                    catch (HttpRequestException ex)
                    {
                        Console.Error.WriteLine($"Warning: could not download spec for {item.Name} {item.VersionKey}: {ex.Message}");
                    }
                    lock (lock_) specs[(item.Name, item.VersionKey)] = spec;
                });
        }

        foreach (var item in vipmSet.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ThenBy(x => x.Version, StringComparer.OrdinalIgnoreCase))
        {
            specs.TryGetValue((item.Name, item.VersionKey), out var spec);
            components.Add(BuildVipmComponent(item, spec));

            var dependsOn = new List<string>();
            foreach (var (depName, minVer) in DependencyParser.Parse(item.Entry?.DependenciesRequires))
            {
                if (string.IsNullOrWhiteSpace(depName)) continue;
                var resolvedKey = index.ResolveDependencyVersion(depName, minVer);
                var match = vipmSet.FirstOrDefault(x => x.Name.Equals(depName, StringComparison.OrdinalIgnoreCase) && x.VersionKey.Equals(resolvedKey, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    dependsOn.Add(match.BomRef);
            }
            dependencies.Add(new CycloneDxDependency { Ref = item.BomRef, DependsOn = dependsOn.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList() });
        }

        foreach (var (name, version) in unresolved)
        {
            // Declared in the TOML but not present in any index: still record it so the SBOM reflects the declaration.
            Console.Error.WriteLine($"Warning: {name} {version} not found in the VIPR/JKI index; emitting component without metadata.");
            var bomRef = Purl("vipm", name, version);
            components.Add(new CycloneDxComponent { Type = "library", BomRef = bomRef, Name = name, Version = version, Purl = bomRef });
            dependencies.Add(new CycloneDxDependency { Ref = bomRef, DependsOn = new List<string>() });
            rootDependsOn.Add(bomRef);
        }

        // --- NI Package Manager packages -------------------------------------------------------------------------
        if (options.IncludeNipkg)
        {
            var nipkgPath = NipkgCli.Locate(options.NipkgPath)
                ?? throw new FileNotFoundException("nipkg.exe not found on PATH or in the default NI Package Manager directory. Use --nipkg-path or --no-nipkg.");
            _log.WriteLine($"Listing installed NI packages via: {nipkgPath}" + (options.NipkgPatterns.Count > 0 ? " " + string.Join(" ", options.NipkgPatterns) : ""));
            var installed = NipkgCli.ListInstalled(nipkgPath, options.NipkgPatterns);
            _log.WriteLine($"Found {installed.Count} installed NI package(s).");

            // name (including Provides virtual names) -> bom-ref, for the dependency graph
            var refByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var nipkgRefs = new Dictionary<NipkgPackage, string>();
            foreach (var pkg in installed)
            {
                var bomRef = Purl("nipkg", pkg.Name, pkg.Version);
                nipkgRefs[pkg] = bomRef;
                refByName.TryAdd(pkg.Name, bomRef);
                foreach (var provided in NipkgControlParser.ParsePackageNames(pkg.Provides))
                    refByName.TryAdd(provided, bomRef);
            }

            foreach (var pkg in installed.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Version, StringComparer.OrdinalIgnoreCase))
            {
                var bomRef = nipkgRefs[pkg];
                components.Add(BuildNipkgComponent(pkg, bomRef));
                var dependsOn = NipkgControlParser.ParsePackageNames(pkg.Depends)
                    .Select(n => refByName.TryGetValue(n, out var r) ? r : null)
                    .Where(r => r != null && r != bomRef)
                    .Select(r => r!)
                    .Distinct()
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToList();
                dependencies.Add(new CycloneDxDependency { Ref = bomRef, DependsOn = dependsOn });
                rootDependsOn.Add(bomRef);
            }
        }

        // --- Root / metadata -------------------------------------------------------------------------------------
        dependencies.Insert(0, new CycloneDxDependency
        {
            Ref = rootRef,
            DependsOn = rootDependsOn.Distinct().OrderBy(x => x, StringComparer.Ordinal).ToList()
        });

        var metadataProps = new List<CycloneDxProperty>
        {
            new() { Name = "vipm2:toml", Value = Path.GetFileName(options.TomlPath) },
            new() { Name = "vipm2:machine", Value = Environment.MachineName }
        };
        if (!string.IsNullOrWhiteSpace(project.LabViewVersion))
            metadataProps.Add(new CycloneDxProperty { Name = "vipm2:labview-version", Value = project.LabViewVersion });

        return new CycloneDxBom
        {
            Metadata = new CycloneDxMetadata
            {
                Tools = new CycloneDxTools
                {
                    Components =
                    {
                        new CycloneDxComponent { Type = "application", Name = "vipm2", Version = ToolVersion(), Publisher = "ConverterTec" }
                    }
                },
                Component = new CycloneDxComponent
                {
                    Type = "application",
                    BomRef = rootRef,
                    Name = productName,
                    Version = productVersion,
                    Purl = rootRef,
                    Supplier = string.IsNullOrWhiteSpace(options.ProductSupplier) ? null : new CycloneDxOrganization { Name = options.ProductSupplier }
                },
                Properties = metadataProps
            },
            Components = components,
            Dependencies = dependencies
        };
    }

    // ---------------------------------------------------------------------------------------------------------------

    private sealed class VipmItem
    {
        public required string Name { get; init; }
        /// <summary>Index key used for lookup ("latest" or a concrete version).</summary>
        public required string VersionKey { get; init; }
        /// <summary>Concrete version reported in the SBOM.</summary>
        public required string Version { get; init; }
        public ViprPackageEntry? Entry { get; init; }
        public string BomRef => Purl("vipm", Name, Version);
    }

    private static (List<VipmItem> Set, List<string> DirectRefs, List<(string Name, string Version)> Unresolved)
        ResolveVipmPackages(ViprPackageIndex index, ProjectToml project, bool includeTransitive)
    {
        var set = new Dictionary<(string, string), VipmItem>(PackageVersionComparer.Instance);
        var directRefs = new List<string>();
        var unresolved = new List<(string, string)>();

        foreach (var (name, version) in project.Dependencies)
        {
            var versionKey = NormalizeVersion(version);
            if (!index.Packages.TryGetValue(name, out var versions) || !versions.ContainsKey(versionKey))
            {
                unresolved.Add((name, versionKey));
                continue;
            }

            IReadOnlyCollection<(string PackageName, string Version)> resolved;
            try
            {
                resolved = includeTransitive
                    ? index.GetPackageAndDependencies(name, versionKey)
                    : new[] { (name, versionKey) };
            }
            catch (InvalidOperationException ex)
            {
                // A transitive dependency is missing from the index; keep the declared package itself.
                Console.Error.WriteLine($"Warning: {ex.Message} (while resolving dependencies of {name} {versionKey})");
                resolved = new[] { (name, versionKey) };
            }

            foreach (var (pkgName, pkgKey) in resolved)
            {
                var item = ToItem(index, pkgName, pkgKey);
                set.TryAdd((pkgName, pkgKey), item);
            }
            directRefs.Add(ToItem(index, name, versionKey).BomRef);
        }

        return (set.Values.ToList(), directRefs, unresolved);
    }

    private static VipmItem ToItem(ViprPackageIndex index, string name, string versionKey)
    {
        ViprPackageEntry? entry = null;
        var concrete = versionKey;
        if (index.Packages.TryGetValue(name, out var versions) && versions.TryGetValue(versionKey, out entry))
        {
            if (string.Equals(versionKey, LatestKey, StringComparison.OrdinalIgnoreCase))
            {
                // "latest" is an alias to the highest concrete version's entry; report the concrete version.
                var match = versions.FirstOrDefault(kv => !string.Equals(kv.Key, LatestKey, StringComparison.OrdinalIgnoreCase) && ReferenceEquals(kv.Value, entry));
                if (match.Key != null)
                    concrete = match.Key;
            }
        }
        return new VipmItem { Name = name, VersionKey = versionKey, Version = concrete, Entry = entry };
    }

    private static CycloneDxComponent BuildVipmComponent(VipmItem item, VipSpec? spec)
    {
        var props = new List<CycloneDxProperty>();
        var displayName = spec?.DisplayName ?? item.Entry?.PackageDisplayName;
        if (!string.IsNullOrWhiteSpace(displayName))
            props.Add(new CycloneDxProperty { Name = "vipm:display-name", Value = displayName });
        if (!string.IsNullOrWhiteSpace(item.Entry?.PackageReleaseDate))
            props.Add(new CycloneDxProperty { Name = "vipm:release-date", Value = item.Entry.PackageReleaseDate });
        if (!string.IsNullOrWhiteSpace(item.Entry?.PlatformExclusiveLabViewVersion))
            props.Add(new CycloneDxProperty { Name = "vipm:labview-version", Value = item.Entry.PlatformExclusiveLabViewVersion });

        var hashes = new List<CycloneDxHash>();
        if (!string.IsNullOrWhiteSpace(item.Entry?.PackageMd5))
            hashes.Add(new CycloneDxHash { Alg = "MD5", Content = item.Entry.PackageMd5.ToLowerInvariant() });

        var external = new List<CycloneDxExternalReference>();
        if (!string.IsNullOrWhiteSpace(spec?.Url))
            external.Add(new CycloneDxExternalReference { Type = "website", Url = spec.Url });

        var supplier = spec?.Vendor ?? spec?.Packager;

        return new CycloneDxComponent
        {
            Type = "library",
            BomRef = item.BomRef,
            Name = item.Name,
            Version = item.Version,
            Description = spec?.Description ?? spec?.Summary,
            Supplier = string.IsNullOrWhiteSpace(supplier) ? null : new CycloneDxOrganization { Name = supplier },
            Licenses = ToLicenses(spec?.License),
            Copyright = spec?.Copyright,
            Purl = item.BomRef,
            Hashes = hashes.Count > 0 ? hashes : null,
            ExternalReferences = external.Count > 0 ? external : null,
            Properties = props.Count > 0 ? props : null
        };
    }

    private static CycloneDxComponent BuildNipkgComponent(NipkgPackage pkg, string bomRef)
    {
        var props = new List<CycloneDxProperty>();
        if (!string.IsNullOrWhiteSpace(pkg.DisplayName)) props.Add(new CycloneDxProperty { Name = "vipm:display-name", Value = pkg.DisplayName });
        if (!string.IsNullOrWhiteSpace(pkg.DisplayVersion)) props.Add(new CycloneDxProperty { Name = "vipm:display-version", Value = pkg.DisplayVersion });
        if (!string.IsNullOrWhiteSpace(pkg.Architecture)) props.Add(new CycloneDxProperty { Name = "nipkg:architecture", Value = pkg.Architecture });
        if (!string.IsNullOrWhiteSpace(pkg.Section)) props.Add(new CycloneDxProperty { Name = "nipkg:section", Value = pkg.Section });
        if (!string.IsNullOrWhiteSpace(pkg.Eula)) props.Add(new CycloneDxProperty { Name = "nipkg:eula", Value = pkg.Eula });

        var hashes = new List<CycloneDxHash>();
        if (!string.IsNullOrWhiteSpace(pkg.Md5)) hashes.Add(new CycloneDxHash { Alg = "MD5", Content = pkg.Md5.ToLowerInvariant() });
        if (!string.IsNullOrWhiteSpace(pkg.Sha256)) hashes.Add(new CycloneDxHash { Alg = "SHA-256", Content = pkg.Sha256.ToLowerInvariant() });

        var external = new List<CycloneDxExternalReference>();
        if (!string.IsNullOrWhiteSpace(pkg.Homepage)) external.Add(new CycloneDxExternalReference { Type = "website", Url = pkg.Homepage });
        if (!string.IsNullOrWhiteSpace(pkg.SourceFeed) && Uri.IsWellFormedUriString(pkg.SourceFeed, UriKind.Absolute) && !pkg.SourceFeed.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
            external.Add(new CycloneDxExternalReference { Type = "distribution", Url = pkg.SourceFeed, Comment = "NI Package Manager feed" });

        return new CycloneDxComponent
        {
            Type = "library",
            BomRef = bomRef,
            Name = pkg.Name,
            Version = pkg.Version,
            Description = pkg.Description,
            Supplier = ParseMaintainer(pkg.Maintainer),
            Purl = bomRef,
            Hashes = hashes.Count > 0 ? hashes : null,
            ExternalReferences = external.Count > 0 ? external : null,
            Properties = props.Count > 0 ? props : null
        };
    }

    /// <summary>"National Instruments &lt;support@ni.com&gt;" -> supplier name + contact email.</summary>
    private static CycloneDxOrganization? ParseMaintainer(string? maintainer)
    {
        if (string.IsNullOrWhiteSpace(maintainer)) return null;
        var m = MaintainerRegex().Match(maintainer);
        if (!m.Success)
            return new CycloneDxOrganization { Name = maintainer.Trim() };
        var name = m.Groups["name"].Value.Trim();
        var email = m.Groups["email"].Value.Trim();
        return new CycloneDxOrganization
        {
            Name = name.Length > 0 ? name : email,
            Contact = email.Length > 0 ? new List<CycloneDxContact> { new() { Email = email } } : null
        };
    }

    [GeneratedRegex(@"^(?<name>[^<]*)<(?<email>[^>]+)>\s*$")]
    private static partial Regex MaintainerRegex();

    /// <summary>SPDX ids commonly seen in VI Package specs; anything else is emitted as a license name.</summary>
    private static readonly HashSet<string> KnownSpdxIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "0BSD", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "BSD-4-Clause", "BSL-1.0", "CC0-1.0", "CC-BY-4.0", "CC-BY-SA-4.0",
        "EPL-1.0", "EPL-2.0", "GPL-2.0-only", "GPL-2.0-or-later", "GPL-3.0-only", "GPL-3.0-or-later", "ISC",
        "LGPL-2.1-only", "LGPL-2.1-or-later", "LGPL-3.0-only", "LGPL-3.0-or-later", "MIT", "MPL-2.0", "Unlicense", "Zlib", "WTFPL"
    };

    private static List<CycloneDxLicenseChoice>? ToLicenses(string? license)
    {
        if (string.IsNullOrWhiteSpace(license)) return null;
        var l = license.Trim();
        var canonical = KnownSpdxIds.TryGetValue(l, out var id) ? id : null;
        return new List<CycloneDxLicenseChoice>
        {
            new() { License = canonical != null ? new CycloneDxLicense { Id = canonical } : new CycloneDxLicense { Name = l } }
        };
    }

    private static string NormalizeVersion(string? version)
    {
        var v = version?.Trim() ?? "";
        return v.Length == 0 || string.Equals(v, LatestKey, StringComparison.OrdinalIgnoreCase) ? LatestKey : v;
    }

    /// <summary>Builds a package URL (purl), percent-encoding the characters the purl spec reserves in the version segment.</summary>
    private static string Purl(string type, string name, string version)
    {
        static string Enc(string s) => s.Replace("%", "%25").Replace("+", "%2B").Replace(" ", "%20").Replace("@", "%40").Replace("#", "%23").Replace("?", "%3F");
        return $"pkg:{type}/{Enc(name)}@{Enc(version)}";
    }

    private static string ToolVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version;
        return v == null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
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
}
