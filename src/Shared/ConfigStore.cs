using System;
using System.IO;
using System.Text.Json;

namespace FastResetUpdated.Shared
{
    // Loads and saves FilterConfig as a small JSON file. Both loader builds point this at
    // their own conventional settings folder (see MelonEntry / BepInExEntry), so the file
    // lives in a sensible, loader-appropriate place either way.
    public sealed class ConfigStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _filePath;
        private readonly IModLogger _logger;

        // Exposed so ModCore can point a PresetStore at the same settings folder without either
        // class needing to know the other's file-naming conventions. Deliberately not named
        // "Directory" — a property with that name would shadow the System.IO.Directory type
        // used elsewhere in this class.
        public string ConfigDirectory { get; }

        public ConfigStore(string directory, IModLogger logger)
        {
            _logger = logger;
            ConfigDirectory = directory;
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "FastResetUpdated.config.json");
        }

        public FilterConfig Load()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    FilterConfig fresh = FilterConfig.CreateDefault();
                    Save(fresh);
                    return fresh;
                }

                string json = File.ReadAllText(_filePath);
                FilterConfig loaded = JsonSerializer.Deserialize<FilterConfig>(json, JsonOptions);
                return loaded ?? FilterConfig.CreateDefault();
            }
            catch (Exception ex)
            {
                // A corrupt or unreadable config file should never stop the mod from working —
                // fall back to the known-good defaults and keep going.
                _logger.Warning($"Fast Reset Updated: failed to load config ({ex.Message}). Using defaults.");
                return FilterConfig.CreateDefault();
            }
        }

        public void Save(FilterConfig config)
        {
            try
            {
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Fast Reset Updated: failed to save config ({ex.Message}).");
            }
        }
    }
}
