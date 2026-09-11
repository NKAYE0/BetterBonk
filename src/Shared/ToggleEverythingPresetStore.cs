using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BetterBonk.Shared
{
    // Named snapshots of which achievements/unlockables are currently toggled OFF via Toggle
    // Everything, saved as individual JSON files in a "ToggleEverythingPresets" subfolder next
    // to the main settings file. Deliberately a separate store from PresetStore (which snapshots
    // FilterConfig) rather than a generalization of it: the two save completely different kinds
    // of state, live in different folders, and track a different "active preset" marker, so
    // keeping them independent avoids the two ever accidentally interacting, at the cost of some
    // duplicated shape between this file and PresetStore.cs.
    //
    // "Default" is always present in the list and always resolves to an empty snapshot (nothing
    // toggled off — Toggle Everything's own unmodified baseline) rather than a saved file, same
    // as PresetStore's "Default" always being FilterConfig.CreateDefault(). It can't be
    // overwritten or deleted through this class.
    public sealed class ToggleEverythingPresetStore
    {
        public const string DefaultPresetName = "Default";

        private readonly string _presetsDirectory;
        private readonly string _activePresetFilePath;
        private readonly IModLogger _logger;

        public ToggleEverythingPresetStore(string configDirectory, IModLogger logger)
        {
            _logger = logger;
            _presetsDirectory = Path.Combine(configDirectory, "ToggleEverythingPresets");
            Directory.CreateDirectory(_presetsDirectory);
            _activePresetFilePath = Path.Combine(configDirectory, "BetterBonk.activetoggleeverythingpreset.txt");
        }

        public static bool IsDefault(string name) =>
            string.Equals(name, DefaultPresetName, StringComparison.OrdinalIgnoreCase);

        // Same rationale as PresetStore.SanitizeName — strips filename-invalid characters so
        // whatever's typed in the "Save As" box can never produce a bad path.
        public static string SanitizeName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
                return string.Empty;

            char[] invalid = Path.GetInvalidFileNameChars();
            char[] kept = rawName.Where(c => !invalid.Contains(c)).ToArray();
            return new string(kept).Trim();
        }

        // "Default" first, then every saved preset alphabetically.
        public List<string> ListPresetNames()
        {
            var names = new List<string> { DefaultPresetName };
            try
            {
                var saved = new List<string>();
                foreach (string file in Directory.GetFiles(_presetsDirectory, "*.json"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (!IsDefault(name))
                        saved.Add(name);
                }
                saved.Sort(StringComparer.OrdinalIgnoreCase);
                names.AddRange(saved);
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to list Toggle Everything presets ({ex.Message}).");
            }
            return names;
        }

        // Null on a real failure (bad JSON, read error) — distinguished from "Default", which
        // returns a fresh empty list every time rather than null, so a caller can always tell
        // "nothing to load" apart from "here's an intentionally empty preset".
        public List<string> Load(string name)
        {
            if (IsDefault(name))
                return new List<string>();

            try
            {
                string path = PathFor(name);
                if (!File.Exists(path))
                {
                    _logger.Warning($"BetterBonk: Toggle Everything preset '{name}' not found.");
                    return null;
                }

                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to load Toggle Everything preset '{name}' ({ex.Message}).");
                return null;
            }
        }

        public bool Save(string name, List<string> inactivatedNames)
        {
            if (string.IsNullOrEmpty(name) || IsDefault(name))
            {
                _logger.Warning("BetterBonk: Toggle Everything preset name can't be empty or 'Default'.");
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(inactivatedNames ?? new List<string>());
                File.WriteAllText(PathFor(name), json);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to save Toggle Everything preset '{name}' ({ex.Message}).");
                return false;
            }
        }

        public void Delete(string name)
        {
            if (IsDefault(name))
                return;

            try
            {
                string path = PathFor(name);
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to delete Toggle Everything preset '{name}' ({ex.Message}).");
            }
        }

        public string LoadActivePresetName()
        {
            try
            {
                if (File.Exists(_activePresetFilePath))
                {
                    string name = File.ReadAllText(_activePresetFilePath).Trim();
                    if (!string.IsNullOrEmpty(name))
                        return name;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to read active Toggle Everything preset marker ({ex.Message}).");
            }
            return DefaultPresetName;
        }

        public void SaveActivePresetName(string name)
        {
            try
            {
                File.WriteAllText(_activePresetFilePath, string.IsNullOrEmpty(name) ? DefaultPresetName : name);
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to save active Toggle Everything preset marker ({ex.Message}).");
            }
        }

        private string PathFor(string name)
        {
            string safeName = SanitizeName(name);
            if (safeName.Length == 0)
                safeName = "Preset";
            return Path.Combine(_presetsDirectory, safeName + ".json");
        }
    }
}
