using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace Sidekick;

internal sealed class ConfigurationStore
{
    private readonly IDalamudPluginInterface pluginInterface;
    private readonly IPluginLog log;

    public ConfigurationStore(IDalamudPluginInterface pluginInterface, IPluginLog log)
    {
        this.pluginInterface = pluginInterface;
        this.log = log;

        try
        {
            var loaded = pluginInterface.GetPluginConfig();
            if (loaded is not null and not Configuration)
                throw new InvalidDataException("Unexpected configuration type.");
            var legacy = loaded is null && pluginInterface.ConfigFile is { } file ? LegacyConfiguration.TryLoad(file) : null;
            Value = (Configuration?)loaded ?? legacy ?? new Configuration();
            Value.ValidateAndMigrate();
            if (legacy is not null)
                Save();
        }
        catch (Exception exception)
        {
            log.Error("Sidekick settings could not be loaded ({ErrorType}); the existing file will not be overwritten.",
                exception.GetType().Name);
            Value = new Configuration();
            CanSave = false;
        }
    }

    public Configuration Value { get; }
    public bool CanSave { get; } = true;
    public bool SaveFailed { get; private set; }

    public void Save()
    {
        if (!CanSave)
            return;

        try
        {
            pluginInterface.SavePluginConfig(Value);
            SaveFailed = false;
        }
        catch (Exception exception)
        {
            if (!SaveFailed)
                log.Error("Sidekick settings could not be saved ({ErrorType}).", exception.GetType().Name);
            SaveFailed = true;
        }
    }
}

