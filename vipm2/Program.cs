using System.CommandLine;
using vipm2.Clients;
using vipm2.Extraction;
using vipm2.Installation;
using vipm2.Models;
using vipm2.Parsing;
using vipm2.Sbom;

var root = new RootCommand("VIPM2 - LabVIEW YOLO vipm, tries to install dependencies in docker to workaround vipm limitaitons. NOT for production usage.");

var installCmd = new Command("install", "Install packages from a project TOML file ([dependencies] and their transitive deps).");
var tomlArg = new Argument<FileInfo>(
    "toml")
{
    Arity = ArgumentArity.ExactlyOne
};
installCmd.Add(tomlArg);

installCmd.SetAction(async (parseResult, cancellationToken) =>
{
    var toml = parseResult.GetValue(tomlArg)!;
    var tomlPath = toml.FullName;
    if (!toml.Exists)
    {
        Console.Error.WriteLine($"TOML file not found: {tomlPath}");
        Environment.Exit(1);
    }

    var project = ProjectTomlLoader.Load(tomlPath);
    if (string.IsNullOrWhiteSpace(project.LabViewVersion))
    {
        Console.Error.WriteLine("Project TOML must specify [project] labview-version (e.g. \"2026\") for the copy step.");
        Environment.Exit(1);
    }

    var client = new ViprZipDownloadClient();
    Console.WriteLine("Downloading and parsing VIPR index...");
    ViprPackageIndex? index = null;
    try
    {
        index = await client.GetPackageIndexAsync();
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"HTTP error: {ex.Message}");
        Environment.Exit(1);
    }
    if (index == null) Environment.Exit(1);
    Console.WriteLine($"Parsed {index.Packages.Count} packages.");

    Console.WriteLine($"Installing packages from: {tomlPath}");
    string? catalogPath = null;
    try
    {
        catalogPath = await client.DownloadFromProjectTomlAsync(index, tomlPath);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Download failed: {ex.Message}");
        Environment.Exit(1);
    }
    if (catalogPath == null) Environment.Exit(1);

    Console.WriteLine($"Catalog: {catalogPath}");
    foreach (var file in Directory.EnumerateFiles(catalogPath).OrderBy(Path.GetFileName))
        Console.WriteLine($"  {Path.GetFileName(file)}");

    Console.WriteLine("Extracting and merging vi.lib / user.lib from all packages...");
    var mergedPath = ViprCatalogMerger.ExtractAndMergeLibs(catalogPath);
    Console.WriteLine($"Merged output: {mergedPath}");
    foreach (var dir in new[] { "vi.lib", "user.lib" })
    {
        var dirPath = Path.Combine(mergedPath, dir);
        if (Directory.Exists(dirPath))
        {
            var count = Directory.EnumerateFiles(dirPath, "*", SearchOption.AllDirectories).Count();
            Console.WriteLine($"  {dir}: {count} file(s)");
        }
    }

    var labviewRoot = LabViewCatalogCopier.GetLabViewRootPath(project.LabViewVersion);
    Console.WriteLine($"Copying merged catalogs to LabVIEW: {labviewRoot} (overwriting conflicts)");
    try
    {
        LabViewCatalogCopier.CopyMergedToLabView(mergedPath, labviewRoot);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Copy to LabVIEW failed: {ex.Message}");
        Environment.Exit(1);
    }
    Console.WriteLine("Done.");
});

root.Add(installCmd);

var buildCmd = new Command("build", "Build a LabVIEW project using the NI LabVIEW CLI ExecuteBuildSpec operation.");
var projectPathArg = new Argument<FileInfo>(
    "project")
{
    Description = "Path to the LabVIEW project file (.lvproj).",
    Arity = ArgumentArity.ExactlyOne
};
var labviewVersionOpt = new Option<string>("--labview-version") { Description = "LabVIEW version (e.g. 2026).", Required = true };
var labviewBitnessOpt = new Option<int>("--labview-bitness") { Description = "LabVIEW bitness (32 or 64).", DefaultValueFactory = _ => 64 };
var targetNameOpt = new Option<string?>("--target-name") { Description = "Target that contains the build specification. Defaults to \"My Computer\" when not specified." };
var buildSpecNameOpt = new Option<string?>("--build-spec-name") { Description = "Name of the build specification to build. When not specified, all build specifications under the target are built." };

buildCmd.Add(projectPathArg);
buildCmd.Add(labviewVersionOpt);
buildCmd.Add(labviewBitnessOpt);
buildCmd.Add(targetNameOpt);
buildCmd.Add(buildSpecNameOpt);

buildCmd.SetAction(parseResult =>
{
    var project = parseResult.GetValue(projectPathArg)!;
    var labviewVersion = parseResult.GetValue(labviewVersionOpt)!;
    var labviewBitness = parseResult.GetValue(labviewBitnessOpt);
    var targetName = parseResult.GetValue(targetNameOpt);
    var buildSpecName = parseResult.GetValue(buildSpecNameOpt);
    var path = project.FullName;
    if (labviewBitness != 32 && labviewBitness != 64)
    {
        Console.Error.WriteLine("--labview-bitness must be 32 or 64.");
        Environment.Exit(1);
    }
    try
    {
        var exitCode = LabViewBuildRunner.ExecuteBuildSpec(path, labviewVersion, labviewBitness, targetName, buildSpecName);
        if (exitCode != 0)
            Environment.Exit(exitCode);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine(ex.Message);
        Environment.Exit(1);
    }
});

root.Add(buildCmd);

var sbomCmd = new Command("sbom", "Generate a CycloneDX 1.7 SBOM (JSON) for a project TOML: VIPM packages from [dependencies] (with transitive dependencies) plus the NI packages installed on this machine (nipkg info-installed).");
var sbomTomlArg = new Argument<FileInfo>("toml") { Description = "Path to the project TOML file ([project] and [dependencies]).", Arity = ArgumentArity.ExactlyOne };
var sbomOutputOpt = new Option<FileInfo?>("--output", "-o") { Description = "Output file path. Defaults to sbom.json next to the TOML file." };
var sbomNoNipkgOpt = new Option<bool>("--no-nipkg") { Description = "Do not include installed NI Package Manager packages." };
var sbomNipkgPatternOpt = new Option<string[]>("--nipkg-pattern") { Description = "Only include installed NI packages whose name matches this Unix-style pattern (e.g. \"ni-teststand-*\"). Repeatable; default is all installed packages.", AllowMultipleArgumentsPerToken = false };
var sbomNipkgPathOpt = new Option<string?>("--nipkg-path") { Description = "Explicit path to nipkg.exe. Defaults to PATH lookup, then the standard NI Package Manager directory." };
var sbomNoTransitiveOpt = new Option<bool>("--no-transitive") { Description = "Only include the packages listed in [dependencies], not their transitive dependencies." };
var sbomNoSpecOpt = new Option<bool>("--no-spec") { Description = "Do not download package .spec files (skips license, vendor, copyright and URL for VIPM packages)." };
var sbomProductNameOpt = new Option<string?>("--product-name") { Description = "Name of the software described by the SBOM. Defaults to [project] name." };
var sbomProductVersionOpt = new Option<string?>("--product-version") { Description = "Version of the software described by the SBOM. Defaults to [project] version." };
var sbomProductSupplierOpt = new Option<string?>("--product-supplier") { Description = "Supplier of the software described by the SBOM (metadata.component.supplier)." };

sbomCmd.Add(sbomTomlArg);
sbomCmd.Add(sbomOutputOpt);
sbomCmd.Add(sbomNoNipkgOpt);
sbomCmd.Add(sbomNipkgPatternOpt);
sbomCmd.Add(sbomNipkgPathOpt);
sbomCmd.Add(sbomNoTransitiveOpt);
sbomCmd.Add(sbomNoSpecOpt);
sbomCmd.Add(sbomProductNameOpt);
sbomCmd.Add(sbomProductVersionOpt);
sbomCmd.Add(sbomProductSupplierOpt);

sbomCmd.SetAction(async (parseResult, cancellationToken) =>
{
    var toml = parseResult.GetValue(sbomTomlArg)!;
    if (!toml.Exists)
    {
        Console.Error.WriteLine($"TOML file not found: {toml.FullName}");
        Environment.Exit(1);
    }
    var output = parseResult.GetValue(sbomOutputOpt)?.FullName
                 ?? Path.Combine(toml.DirectoryName ?? Directory.GetCurrentDirectory(), "sbom.json");

    var options = new SbomOptions
    {
        TomlPath = toml.FullName,
        IncludeNipkg = !parseResult.GetValue(sbomNoNipkgOpt),
        NipkgPatterns = parseResult.GetValue(sbomNipkgPatternOpt) ?? Array.Empty<string>(),
        NipkgPath = parseResult.GetValue(sbomNipkgPathOpt),
        IncludeTransitive = !parseResult.GetValue(sbomNoTransitiveOpt),
        FetchSpecs = !parseResult.GetValue(sbomNoSpecOpt),
        ProductName = parseResult.GetValue(sbomProductNameOpt),
        ProductVersion = parseResult.GetValue(sbomProductVersionOpt),
        ProductSupplier = parseResult.GetValue(sbomProductSupplierOpt)
    };

    try
    {
        var generator = new SbomGenerator(new ViprZipDownloadClient());
        var bom = await generator.GenerateAsync(options, cancellationToken);
        await CycloneDxWriter.WriteAsync(bom, output, cancellationToken);
        Console.WriteLine($"SBOM written to {output} ({bom.Components.Count} component(s)).");
    }
    catch (HttpRequestException ex)
    {
        Console.Error.WriteLine($"HTTP error: {ex.Message}");
        Environment.Exit(1);
    }
    catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or IOException)
    {
        Console.Error.WriteLine($"SBOM generation failed: {ex.Message}");
        Environment.Exit(1);
    }
});

root.Add(sbomCmd);

return await root.Parse(args).InvokeAsync();
