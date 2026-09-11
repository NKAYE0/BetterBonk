using System;
using System.IO;
using System.Text.Json;

namespace BetterBonk.Shared
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
            MigrateFromOldBrandIfNeeded(directory, logger);
            Directory.CreateDirectory(directory);
            _filePath = Path.Combine(directory, "BetterBonk.config.json");
        }

        // One-time migration for players upgrading from the old "FastResetUpdated" mod name: if
        // the new BetterBonk settings folder doesn't exist yet but a sibling FastResetUpdated
        // folder from a previous install does, copy its contents over (config, Presets
        // subfolder, active-preset marker) so nothing is lost across the rename. Only runs when
        // the new folder is completely absent, so it can never overwrite settings someone has
        // already started customizing under the new name.
        private static void MigrateFromOldBrandIfNeeded(string newDirectory, IModLogger logger)
        {
            try
            {
                if (Directory.Exists(newDirectory))
                    return;

                string parent = Path.GetDirectoryName(newDirectory);
                if (string.IsNullOrEmpty(parent))
                    return;

                string oldDirectory = Path.Combine(parent, "FastResetUpdated");
                if (!Directory.Exists(oldDirectory))
                    return;

                CopyDirectoryRenamingBrand(oldDirectory, newDirectory);
                logger.Msg($"BetterBonk: migrated settings from '{oldDirectory}' to '{newDirectory}'.");
            }
            catch (Exception ex)
            {
                logger.Warning($"BetterBonk: failed to migrate old settings ({ex.Message}).");
            }
        }

        // Recursively copies sourceDir into destDir, renaming any file that starts with the old
        // "FastResetUpdated." brand prefix to start with "BetterBonk." instead (e.g. the main
        // config file and the active-preset marker). Files without that prefix (saved presets,
        // named by the player) are copied as-is. Never overwrites — this only ever runs against
        // a destination that didn't exist a moment ago.
        private static void CopyDirectoryRenamingBrand(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            const string oldPrefix = "FastResetUpdated.";
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string fileName = Path.GetFileName(filePath);
                if (fileName.StartsWith(oldPrefix, StringComparison.OrdinalIgnoreCase))
                    fileName = "BetterBonk." + fileName.Substring(oldPrefix.Length);

                File.Copy(filePath, Path.Combine(destDir, fileName), overwrite: false);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string subDirName = Path.GetFileName(subDir);
                CopyDirectoryRenamingBrand(subDir, Path.Combine(destDir, subDirName));
            }
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
                _logger.Warning($"BetterBonk: failed to load config ({ex.Message}). Using defaults.");
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
                _logger.Warning($"BetterBonk: failed to save config ({ex.Message}).");
            }
        }
    }
}
