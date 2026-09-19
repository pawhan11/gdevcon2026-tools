using vipm2.Models;

namespace vipm2.Clients;

/// <summary>
/// Downloads the VIPR zip index from the NI evaluation server and returns the package index.
/// Can also download .vip packages from the package network by name.
/// </summary>
public interface IViprZipDownloadClient
{
    /// <summary>
    /// Downloads the VIPR index and returns the parsed package index (name -> version -> entry).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The package index.</returns>
    Task<ViprPackageIndex> GetPackageIndexAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a .vip package by name from the index (and optional version; default is "latest").
    /// </summary>
    /// <param name="index">The package index from <see cref="GetPackageIndexAsync"/>.</param>
    /// <param name="packageName">Package name (e.g. labview_open_source_project_lib_labview_open_source_palette).</param>
    /// <param name="version">Version key, or null for "latest".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Raw bytes of the .vip file.</returns>
    Task<byte[]> DownloadVipPackageAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a .vip package by name and saves it as a .zip file in the application directory.
    /// </summary>
    /// <param name="index">The package index from <see cref="GetPackageIndexAsync"/>.</param>
    /// <param name="packageName">Package name.</param>
    /// <param name="version">Version key, or null for "latest".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the saved .zip file.</returns>
    Task<string> SaveVipPackageAsZipInAppFolderAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads a package and all its dependencies (transitive) into a temp catalog directory.
    /// Each file is saved as .zip using the package filename from the index.
    /// </summary>
    /// <param name="index">The package index from <see cref="GetPackageIndexAsync"/>.</param>
    /// <param name="packageName">Package name.</param>
    /// <param name="version">Version key, or null for "latest".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the temp catalog directory containing the package and all dependency .zip files.</returns>
    Task<string> DownloadPackageWithDependenciesToTempAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads all packages listed in a project TOML [dependencies] and their transitive dependencies into a temp catalog.
    /// </summary>
    /// <param name="index">The package index from <see cref="GetPackageIndexAsync"/>.</param>
    /// <param name="tomlPath">Path to the project TOML file (e.g. with [project] and [dependencies]).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full path of the temp catalog directory.</returns>
    Task<string> DownloadFromProjectTomlAsync(ViprPackageIndex index, string tomlPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Downloads the .spec file (Spec.URL) of a package version and returns its text, or null when the index has no
    /// Spec.URL for it or neither NI nor JKI serves the file.
    /// </summary>
    /// <param name="index">The package index from <see cref="GetPackageIndexAsync"/>.</param>
    /// <param name="packageName">Package name.</param>
    /// <param name="version">Version key, or null for "latest".</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> DownloadSpecAsync(ViprPackageIndex index, string packageName, string? version = null, CancellationToken cancellationToken = default);
}
