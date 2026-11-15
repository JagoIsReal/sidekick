using Newtonsoft.Json;

namespace Sidekick;

internal static class LegacyConfiguration
{
    internal static Configuration? TryLoad(FileInfo currentFile)
    {
        currentFile.Refresh();
        if (currentFile.Exists)
            throw new InvalidDataException("An existing Sidekick configuration could not be loaded.");
        var directory = currentFile.DirectoryName;
        if (directory is null)
            return null;

        var legacyFile = Path.Combine(directory, "Highlight.json");
        if (!File.Exists(legacyFile))
            return null;

        // Read the known schema without resolving old $type assembly names or modifying the source file.
        return JsonConvert.DeserializeObject<Configuration>(File.ReadAllText(legacyFile), new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            CheckAdditionalContent = true,
        }) ?? throw new InvalidDataException("The legacy configuration is empty.");
    }
}
