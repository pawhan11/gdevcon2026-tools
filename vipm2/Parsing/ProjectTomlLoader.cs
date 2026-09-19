using Tomlyn;
using Tomlyn.Model;
using vipm2.Models;

namespace vipm2.Parsing;

/// <summary>
/// Loads a project TOML file ([project] and [dependencies]).
/// </summary>
public static class ProjectTomlLoader
{
    /// <summary>
    /// Parses a TOML file and returns project info and dependencies (name -> version).
    /// </summary>
    public static ProjectToml Load(string tomlPath)
    {
        var content = File.ReadAllText(tomlPath);
        var model = TomlSerializer.Deserialize<TomlTable>(content);
        if (model == null)
            return new ProjectToml();

        var name = (string?)null;
        var version = (string?)null;
        var labviewVersion = (string?)null;
        var dependencies = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (model["project"] is TomlTable projectTable)
        {
            name = GetString(projectTable, "name");
            version = GetString(projectTable, "version");
            labviewVersion = GetString(projectTable, "labview-version");
        }

        if (model["dependencies"] is TomlTable depsTable)
        {
            foreach (var key in depsTable.Keys)
            {
                var value = GetString(depsTable, key);
                if (key != null && value != null)
                    dependencies[key] = value;
            }
        }

        return new ProjectToml
        {
            Name = name,
            Version = version,
            LabViewVersion = labviewVersion,
            Dependencies = dependencies
        };
    }

    private static string? GetString(TomlTable table, string key)
    {
        if (!table.TryGetValue(key, out var obj))
            return null;
        return obj?.ToString();
    }
}
