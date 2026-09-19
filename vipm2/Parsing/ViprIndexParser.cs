namespace vipm2.Parsing;

/// <summary>
/// Parses index.vipr INI-style content into a section -> key/value dictionary.
/// </summary>
public static class ViprIndexParser
{
    /// <summary>
    /// Parses vipr text into a dictionary: section name -> (key -> value).
    /// Section headers are lines like [Self] or [Package name-version]; key=value until next section or end.
    /// </summary>
    /// <param name="content">Raw index.vipr text content.</param>
    /// <returns>Dictionary of section name to dictionary of key-value pairs.</returns>
    public static Dictionary<string, Dictionary<string, string>> Parse(string content)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string>? currentSection = null;
        var sectionName = (string?)null;
        
        
        foreach (var rawLine in content.Split('\n'))
        {

            var line = rawLine.TrimEnd('\r').Trim();
            if (string.IsNullOrEmpty(line))
                continue;

            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                sectionName = line[1..^1];
                currentSection = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                result[sectionName] = currentSection;
                continue;
            }

            var eq = line.IndexOf('=');
            if (eq > 0 && currentSection != null)
            {
                var key = line[..eq].Trim();
                var value = line[(eq + 1)..].Trim();
                currentSection[key] = value;
            }
        }

        return result;
    }
}
