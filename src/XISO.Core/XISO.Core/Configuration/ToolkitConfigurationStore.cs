using System.Text.Json;

namespace XISO.Core.Configuration;

public sealed class ToolkitConfigurationStore
{
    public ToolkitConfiguration Load(string configurationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);

        if (!File.Exists(configurationPath))
        {
            throw new FileNotFoundException(
                "The toolkit configuration was not found.",
                configurationPath);
        }

        var json = File.ReadAllText(configurationPath);

        return
            JsonSerializer.Deserialize<ToolkitConfiguration>(json)
            ?? new ToolkitConfiguration();
    }

    public void Save(
        string configurationPath,
        ToolkitConfiguration configuration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configurationPath);
        ArgumentNullException.ThrowIfNull(configuration);

        var json =
            JsonSerializer.Serialize(
                configuration,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });

        File.WriteAllText(configurationPath, json);
    }
}