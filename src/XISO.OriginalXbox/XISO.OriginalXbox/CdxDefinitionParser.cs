using System.Text;

namespace XISO.OriginalXbox;

public sealed class CdxDefinition
{
    public IReadOnlyList<CdxMenuItem> MenuItems { get; init; } =
        Array.Empty<CdxMenuItem>();
}

public sealed class CdxMenuItem
{
    public string Section { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Description { get; init; } = "";
    public string Action { get; init; } = "";
    public string Preview { get; init; } = "";
    public string FileName { get; init; } = "";
}

public static class CdxDefinitionParser
{
    public static CdxDefinition Parse(byte[] data)
    {
        var text =
            Encoding.Unicode.GetString(data);

        var sections =
            new Dictionary<string, Dictionary<string, string>>(
                StringComparer.OrdinalIgnoreCase);

        var mainMenuEntries =
            new List<string>();

        string? currentSection = null;

        foreach (var rawLine in text.Split(
            new[] { "\r\n", "\n", "\r" },
            StringSplitOptions.None))
        {
            var line =
                rawLine.Trim();

            if (line.Length == 0 ||
                line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            if (line.StartsWith("[", StringComparison.Ordinal) &&
                line.EndsWith("]", StringComparison.Ordinal))
            {
                currentSection =
                    line.Substring(1, line.Length - 2).Trim();

                if (!sections.ContainsKey(currentSection))
                {
                    sections[currentSection] =
                        new Dictionary<string, string>(
                            StringComparer.OrdinalIgnoreCase);
                }

                continue;
            }

            if (currentSection is null)
            {
                continue;
            }

            var equalsIndex =
                line.IndexOf('=');

            if (equalsIndex >= 0)
            {
                var key =
                    line[..equalsIndex].Trim();

                var value =
                    line[(equalsIndex + 1)..].Trim();

                sections[currentSection][key] =
                    Unquote(value);

                continue;
            }

            if (currentSection.Equals(
                "MainMenu",
                StringComparison.OrdinalIgnoreCase))
            {
                mainMenuEntries.Add(line);
            }
        }

        var menuItems =
            new List<CdxMenuItem>();

        foreach (var entry in mainMenuEntries)
        {
            if (!sections.TryGetValue(entry, out var values))
            {
                continue;
            }

            values.TryGetValue("DisplayName", out var displayName);
            values.TryGetValue("Description", out var description);
            values.TryGetValue("Action", out var action);
            values.TryGetValue("Preview", out var preview);
            values.TryGetValue("FileName", out var fileName);

            if (string.IsNullOrWhiteSpace(fileName))
            {
                continue;
            }

            menuItems.Add(
                new CdxMenuItem
                {
                    Section = entry,
                    DisplayName = displayName ?? "",
                    Description = description ?? "",
                    Action = action ?? "",
                    Preview = preview ?? "",
                    FileName = fileName
                });
        }

        return new CdxDefinition
        {
            MenuItems = menuItems
        };
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 &&
            value[0] == '"' &&
            value[^1] == '"')
        {
            return value[1..^1];
        }

        return value;
    }
}
