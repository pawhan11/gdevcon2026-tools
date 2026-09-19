using vipm2.Extraction;
using vipm2.Models;
using vipm2.Parsing;

namespace vipm2.Clients;

/// <summary>
/// HTTP client that replicates the VIPM request to download.ni.com for index.vipr.zip
/// and .vip packages from download.ni.com (NI LVTN) or www.jkisoft.com (301 to S3) (GET, User-Agent, Connection: Keep-Alive).
/// </summary>
public sealed class ViprZipDownloadClient : IViprZipDownloadClient
{
    private const string IndexBaseUrl = "http://download.ni.com";
    private const string LvtnVipmPath = "/evaluation/labview/lvtn/vipm/";
    private const string RequestPath = LvtnVipmPath + "index.vipr.zip";
    private const string JkiIndexUrl = "http://www.jkisoft.com/packages/jkisoft.ogpd.zip";
    /// <summary>NI LVTN: download.ni.com/evaluation/labview/lvtn/vipm/packages/... (same host as index).</summary>
    private static readonly string VipPackageBaseUrlNi = IndexBaseUrl + LvtnVipmPath;
    /// <summary>JKI: www.jkisoft.com/packages/ + Package.URL, then 301 to S3.</summary>
    private const string VipPackageBaseUrlJki = "http://www.jkisoft.com/packages/";
    private const string UserAgent = "VIPM/2026.1.0.3528 (Windows x64 10.0;)";

    private readonly HttpClient _httpClient;

    public ViprZipDownloadClient(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? CreateDefaultClient();
    }

    private static HttpClient CreateDefaultClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri(IndexBaseUrl)
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", UserAgent);
        client.DefaultRequestHeaders.Connection.Add("Keep-Alive");
        return client;
    }

    /// <inheritdoc />
    public async Task<ViprPackageIndex> GetPackageIndexAsync(CancellationToken cancellationToken = default)
    {
        // 1) NI index: index.vipr.zip -> index.vipr
        var niZipBytes = await WithRetryAsync(async ct =>
        {
            using var responseNi = await _httpClient.GetAsync(RequestPath, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            responseNi.EnsureSuccessStatusCode();
            return await responseNi.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
        var parsedNi = ViprIndexParser.Parse(ViprZipExtractor.ExtractIndexViprContent(niZipBytes));

        // 2) JKI index: jkisoft.ogpd.zip -> jkisoft.ogpf (same syntax)
        var merged = new Dictionary<string, Dictionary<string, string>>(parsedNi, StringComparer.OrdinalIgnoreCase);
        try
        {
            var jkiZipBytes = await WithRetryAsync(async ct =>
            {
                using var responseJki = await _httpClient.GetAsync(JkiIndexUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                responseJki.EnsureSuccessStatusCode();
                return await responseJki.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
            var parsedJki = ViprIndexParser.Parse(ViprZipExtractor.ExtractJkisoftOgpfContent(jkiZipBytes));
            foreach (var (section, sectionData) in parsedJki)
                merged[section] = sectionData;
        }
        catch (HttpRequestException)
        {
            // JKI index optional; proceed with NI only
        }

        return ViprPackageIndex.FromParsedSections(merged);
    }

    /// <inheritdoc />
    public async Task<byte[]> DownloadVipPackageAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default)
    {
        var versionKey = version ?? "latest";
        if (!index.Packages.TryGetValue(packageName, out var versions))
            throw new ArgumentException($"Package not found: {packageName}.", nameof(packageName));
        if (!versions.TryGetValue(versionKey, out var entry))
            throw new ArgumentException($"Version not found: {versionKey} for package {packageName}.", nameof(version));
        if (string.IsNullOrEmpty(entry.PackageUrl))
            throw new InvalidOperationException($"Package entry has no Package.URL for {packageName} {versionKey}.");

        var bytes = await TryDownloadFromUrlAsync(entry.PackageUrl, cancellationToken).ConfigureAwait(false);
        if (bytes != null)
        {
            Console.WriteLine($"Downloaded: {packageName} {versionKey}");
            return bytes;
        }

        // "latest" is only an internal index key (external services only serve versioned URLs). Retry with the
        // concrete URL we store under that key when the requested version's URL returns 404 (server may not have that build).
        const string LatestKey = "latest";
        if (!string.Equals(versionKey, LatestKey, StringComparison.OrdinalIgnoreCase) && versions.TryGetValue(LatestKey, out var highestVersionEntry) && !string.IsNullOrEmpty(highestVersionEntry.PackageUrl) && highestVersionEntry.PackageUrl != entry.PackageUrl)
        {
            bytes = await TryDownloadFromUrlAsync(highestVersionEntry.PackageUrl, cancellationToken).ConfigureAwait(false);
            if (bytes != null)
            {
                Console.WriteLine($"Downloaded: {packageName} {versionKey} (via highest available version)");
                return bytes;
            }
        }

        throw new HttpRequestException($"Package {packageName} {versionKey} not found (404 from JKI and NI). Tried: {VipPackageBaseUrlJki}{entry.PackageUrl}");
    }

    /// <inheritdoc />
    public async Task<string?> DownloadSpecAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default)
    {
        var versionKey = version ?? "latest";
        if (!index.Packages.TryGetValue(packageName, out var versions) || !versions.TryGetValue(versionKey, out var entry))
            throw new ArgumentException($"Package not found: {packageName} {versionKey}.", nameof(packageName));
        if (string.IsNullOrEmpty(entry.SpecUrl))
            return null;

        var bytes = await TryDownloadFromUrlAsync(entry.SpecUrl, cancellationToken).ConfigureAwait(false);
        return bytes == null ? null : System.Text.Encoding.UTF8.GetString(bytes);
    }

    private static readonly System.Net.HttpStatusCode[] TransientStatusCodes =
    [
        System.Net.HttpStatusCode.ServiceUnavailable,   // 503
        System.Net.HttpStatusCode.TooManyRequests,      // 429
        System.Net.HttpStatusCode.BadGateway,           // 502
        System.Net.HttpStatusCode.GatewayTimeout,       // 504
    ];

    private const int MaxRetries = 4;
    private static readonly TimeSpan RetryBaseDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Executes <paramref name="action"/> with exponential back-off on transient HTTP errors (503, 429, 502, 504).
    /// </summary>
    private static async Task<T> WithRetryAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries && ex.StatusCode.HasValue && Array.IndexOf(TransientStatusCodes, ex.StatusCode.Value) >= 0)
            {
                var delay = RetryBaseDelay * (1 << attempt); // 2 s, 4 s, 8 s, 16 s
                Console.WriteLine($"Transient error ({(int)ex.StatusCode.Value}), retrying in {delay.TotalSeconds:0}s (attempt {attempt + 1}/{MaxRetries})...");
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Tries to download from NI LVTN (download.ni.com/.../vipm/packages/...) then JKI (www.jkisoft.com/packages/...).
    /// Returns content on first 200, or null if both return 404.
    /// </summary>
    private async Task<byte[]?> TryDownloadFromUrlAsync(string packageUrl, CancellationToken cancellationToken)
    {
        // 1) NI LVTN: http://download.ni.com/evaluation/labview/lvtn/vipm/packages/name/name-ver.vip
        var urlNi = VipPackageBaseUrlNi + packageUrl;
        var niResult = await WithRetryAsync(async ct =>
        {
            using var response = await _httpClient.GetAsync(urlNi, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode)
                return (true, await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound)
                response.EnsureSuccessStatusCode(); // throws HttpRequestException with StatusCode set
            return (false, null);
        }, cancellationToken).ConfigureAwait(false);

        if (niResult.Item1)
            return niResult.Item2;

        // 2) JKI: http://www.jkisoft.com/packages/ + Package.URL (301 to S3)
        var urlJki = VipPackageBaseUrlJki + packageUrl;
        var jkiResult = await WithRetryAsync(async ct =>
        {
            using var responseJki = await _httpClient.GetAsync(urlJki, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (responseJki.IsSuccessStatusCode)
                return (true, await responseJki.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false));
            if (responseJki.StatusCode != System.Net.HttpStatusCode.NotFound)
                responseJki.EnsureSuccessStatusCode();
            return (false, null);
        }, cancellationToken).ConfigureAwait(false);

        return jkiResult.Item1 ? jkiResult.Item2 : null;
    }

    /// <inheritdoc />
    public async Task<string> SaveVipPackageAsZipInAppFolderAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default)
    {
        var bytes = await DownloadVipPackageAsync(index, packageName, version, cancellationToken).ConfigureAwait(false);
        var versionKey = version ?? "latest";
        var zipFileName = index.Packages.TryGetValue(packageName, out var versions) && versions.TryGetValue(versionKey, out var entry) && !string.IsNullOrEmpty(entry.PackageUrl)
            ? Path.ChangeExtension(Path.GetFileName(entry.PackageUrl), ".zip")
            : $"{packageName}-{versionKey}.zip";
        var savePath = Path.Combine(AppContext.BaseDirectory, zipFileName);
        await File.WriteAllBytesAsync(savePath, bytes, cancellationToken).ConfigureAwait(false);
        return savePath;
    }

    /// <inheritdoc />
    public async Task<string> DownloadPackageWithDependenciesToTempAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default)
    {
        var versionKey = version ?? "latest";
        var catalogDir = Path.Combine(Path.GetTempPath(), "vipm2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(catalogDir);

        var set = index.GetPackageAndDependencies(packageName, version);
        Console.WriteLine($"Resolved {set.Count} package(s) to download:");
        foreach (var (name, ver) in set.OrderBy(x => x.PackageName).ThenBy(x => x.Version))
        {
            Console.WriteLine($"  - {name} {ver}");
        }

        const int MaxParallelDownloads = 8;
        await Parallel.ForEachAsync(set, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = cancellationToken }, async (item, ct) =>
        {
            var (name, ver) = item;
            var bytes = await DownloadVipPackageAsync(index, name, ver, ct).ConfigureAwait(false);
            var zipFileName = index.Packages.TryGetValue(name, out var versions) && versions.TryGetValue(ver, out var entry) && !string.IsNullOrEmpty(entry.PackageUrl)
                ? Path.ChangeExtension(Path.GetFileName(entry.PackageUrl), ".zip")
                : $"{name}-{ver}.zip";
            var safeName = string.Join("_", zipFileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safeName))
                safeName = $"{name}-{ver}.zip";
            var filePath = Path.Combine(catalogDir, safeName);
            await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
        });

        return catalogDir;
    }

    /// <inheritdoc />
    public async Task<string> DownloadFromProjectTomlAsync(ViprPackageIndex index, string tomlPath, CancellationToken cancellationToken = default)
    {
        var project = ProjectTomlLoader.Load(tomlPath);
        var set = new HashSet<(string, string)>(PackageVersionTupleComparer.Instance);

        foreach (var (name, version) in project.Dependencies)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var versionKey = NormalizeDependencyVersion(version);
            var deps = index.GetPackageAndDependencies(name, versionKey);
            foreach (var item in deps)
                set.Add(item);
        }

        var catalogDir = Path.Combine(Path.GetTempPath(), "vipm2", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(catalogDir);

        Console.WriteLine($"Resolved {set.Count} package(s) from TOML dependencies:");
        foreach (var (name, ver) in set.OrderBy(x => x.Item1).ThenBy(x => x.Item2))
        {
            Console.WriteLine($"  - {name} {ver}");
        }

        const int MaxParallelDownloads = 8;
        await Parallel.ForEachAsync(set, new ParallelOptions { MaxDegreeOfParallelism = MaxParallelDownloads, CancellationToken = cancellationToken }, async (item, ct) =>
        {
            var (name, ver) = item;
            var bytes = await DownloadVipPackageAsync(index, name, ver, ct).ConfigureAwait(false);
            var zipFileName = index.Packages.TryGetValue(name, out var versions) && versions.TryGetValue(ver, out var entry) && !string.IsNullOrEmpty(entry.PackageUrl)
                ? Path.ChangeExtension(Path.GetFileName(entry.PackageUrl), ".zip")
                : $"{name}-{ver}.zip";
            var safeName = string.Join("_", zipFileName.Split(Path.GetInvalidFileNameChars()));
            if (string.IsNullOrEmpty(safeName))
                safeName = $"{name}-{ver}.zip";
            var filePath = Path.Combine(catalogDir, safeName);
            await File.WriteAllBytesAsync(filePath, bytes, ct).ConfigureAwait(false);
        });

        return catalogDir;
    }

    /// <summary>
    /// Normalizes a dependency version from TOML: trims whitespace and maps "latest" (case-insensitive) to "latest".
    /// </summary>
    private static string NormalizeDependencyVersion(string? version)
    {
        var v = version?.Trim() ?? "";
        if (v.Length == 0 || string.Equals(v, "latest", StringComparison.OrdinalIgnoreCase))
            return "latest";
        return v;
    }

    private sealed class PackageVersionTupleComparer : IEqualityComparer<(string, string)>
    {
        internal static readonly PackageVersionTupleComparer Instance = new();
        public bool Equals((string, string) x, (string, string) y) =>
            string.Equals(x.Item1, y.Item1, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Item2, y.Item2, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode((string, string) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item1), StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Item2));
    }
}
