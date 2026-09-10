using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace FastResetUpdated.Shared
{
    // Named FilterConfig snapshots the player can switch between, saved as individual JSON
    // files in a "Presets" subfolder next to the main settings file. "Default" is always
    // present in the list and always resolves to FilterConfig.CreateDefault() rather than a
    // saved file, so there's always an unmodifiable, always-available baseline to switch back
    // to no matter what's been saved over the player's own named presets — it can't be
    // overwritten or deleted through this class.
    //
    // Which preset is "active" is tracked separately (see LoadActivePresetName/
    // SaveActivePresetName) as a single line in its own small file rather than as a field on
    // FilterConfig itself — a FilterConfig is also what gets serialized INTO each preset file,
    // so a field there would end up recording (and being overwritten by) whichever preset was
    // active when that preset was last saved, which isn't what it means. This class treats the
    // active-preset name purely as a UI label of "which preset the current settings came from
    // last" — it's best-effort and cosmetic, never re-validated against the actual current
    // FilterConfig contents, since the player is free to tweak settings after loading a preset
    // without that meaning the preset itself changed.
    public sealed class PresetStore
    {
        public const string DefaultPresetName = "Default";

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };

        private readonly string _presetsDirectory;
        private readonly string _activePresetFilePath;
        private readonly IModLogger _logger;

        public PresetStore(string configDirectory, IModLogger logger)
        {
            _logger = logger;
            _presetsDirectory = Path.Combine(configDirectory, "Presets");
            Directory.CreateDirectory(_presetsDirectory);
            _activePresetFilePath = Path.Combine(configDirectory, "FastResetUpdated.activepreset.txt");
        }

        public static bool IsDefault(string name) =>
            string.Equals(name, DefaultPresetName, StringComparison.OrdinalIgnoreCase);

        // Strips characters that can't appear in a filename and trims whitespace, so whatever a
        // player types in the "Save As" box can never produce a bad path or land outside the
        // presets folder. Callers should sanitize once and reuse the result (for both the save
        // itself and whatever they display/remember as the active preset name) rather than
        // sanitizing again at each use — sanitizing twice is harmless (it's idempotent) but
        // reusing the same value avoids the two ever silently drifting apart.
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
                _logger.Warning($"FastReset+: failed to list presets ({ex.Message}).");
            }
            return names;
        }

        public FilterConfig Load(string name)
        {
            if (IsDefault(name))
                return FilterConfig.CreateDefault();

            try
            {
                string path = PathFor(name);
                if (!File.Exists(path))
                {
                    _logger.Warning($"FastReset+: preset '{name}' not found.");
                    return null;
                }

                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<FilterConfig>(json, JsonOptions) ?? FilterConfig.CreateDefault();
            }
            catch (Exception ex)
            {
                _logger.Warning($"FastReset+: failed to load preset '{name}' ({ex.Message}).");
                return null;
            }
        }

        // Refuses an empty name or "Default" — Default is meant to always be the mod's own
        // baseline, never something a saved config can silently replace. A name that already
        // matches an existing preset is overwritten without confirmation, the same one-click
        // way "Reset to Defaults" already works in this menu.
        public bool Save(string name, FilterConfig config)
        {
            if (string.IsNullOrEmpty(name) || IsDefault(name))
            {
                _logger.Warning("FastReset+: preset name can't be empty or 'Default'.");
                return false;
            }

            try
            {
                string json = JsonSerializer.Serialize(config, JsonOptions);
                File.WriteAllText(PathFor(name), json);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning($"FastReset+: failed to save preset '{name}' ({ex.Message}).");
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
                _logger.Warning($"FastReset+: failed to delete preset '{name}' ({ex.Message}).");
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
                _logger.Warning($"FastReset+: failed to read active preset marker ({ex.Message}).");
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
                _logger.Warning($"FastReset+: failed to save active preset marker ({ex.Message}).");
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
