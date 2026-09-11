using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Il2Cpp;
// 0Harmony.dll defines a legacy, non-nested "Harmony" namespace (backward-compat shims from
// Harmony 1.x) alongside the real HarmonyLib.Harmony class. That namespace sits at the global
// level, so it beats the bare "Harmony" identifier (CS0118) AND conflicts with a using-alias
// of that same name (CS0576) — the alias itself has to be spelled differently.
using HarmonyApi = HarmonyLib.Harmony;

namespace BetterBonk.Shared
{
    // "Toggle Everything" support — unlocks every achievement-gated item/upgrade and lets the
    // player individually switch each one on or off from the game's own achievement UI instead
    // of waiting to earn it normally. The player authorized reusing MegabonkToggleEverything.dll's
    // code exactly as-is for this feature. Rather than hand-transcribing its Harmony patches
    // into this project (one of them — the custom item-rarity fallback/dedup logic used when
    // picking a random item — is intricate enough that a manual re-derivation from decompiled
    // IL risked subtly changing behaviour the author already has working), this project
    // references their compiled DLL directly (see lib/MegabonkToggleEverything.dll and the
    // "MegabonkToggleEverything" reference in both .csproj files) and takes over applying and
    // removing its patches itself here, gated on the config toggle below, instead of letting the
    // DLL's own MelonMod entry point apply them unconditionally the moment it's loaded.
    //
    // NOT YET CONFIRMED BY A REAL BUILD: for typeof(...).Assembly below to even load,
    // MegabonkToggleEverything.dll has to be resolvable at runtime alongside the built
    // BetterBonk DLL. Both .csproj files now copy it to where each loader should find it
    // (MelonLoader's UserLibs folder; the same BepInEx\plugins\BetterBonk subfolder as this
    // mod's own DLL) based on how each loader's dependency resolution is documented to work,
    // but neither placement has been verified against your actual install. If the log shows an
    // assembly-load error mentioning "MegabonkToggleEverything" when this toggle is turned on,
    // that placement is the first thing to check — for MelonLoader, confirm the DLL landed in
    // UserLibs (not Mods, which would also load its own separate always-on MelonMod class); for
    // BepInEx, confirm it's sitting next to BetterBonk.dll in BepInEx\plugins\BetterBonk.
    public sealed partial class ModCore
    {
        private const string ToggleEverythingHarmonyId = "nk.betterbonk.toggleeverything";

        private HarmonyApi _toggleEverythingHarmony;
        private bool _toggleEverythingApplied;

        private void UpdateToggleEverything()
        {
            if (Config.ToggleEverythingEnabled == _toggleEverythingApplied)
                return;

            if (Config.ToggleEverythingEnabled)
                ApplyToggleEverything();
            else
                RemoveToggleEverything();
        }

        private void ApplyToggleEverything()
        {
            try
            {
                // The DLL's Harmony patch container (ToggleEverythingPatches) turned out to be
                // internal, so it can't be named at compile time (CS0122). Loading the assembly
                // by name sidesteps that entirely — it's already resolvable at runtime since the
                // .csproj copies it right alongside this mod's own DLL.
                System.Reflection.Assembly toggleAssembly = System.Reflection.Assembly.Load("MegabonkToggleEverything");
                EnsureToggleEverythingLoggerInitialized(toggleAssembly);
                _toggleEverythingHarmony ??= new HarmonyApi(ToggleEverythingHarmonyId);
                _toggleEverythingHarmony.PatchAll(toggleAssembly);
                _toggleEverythingApplied = true;
                _logger.Msg("BetterBonk: Toggle Everything enabled.");
            }
            catch (Exception ex)
            {
                _logger.Warning(
                    $"BetterBonk: failed to enable Toggle Everything ({ex.Message}). " +
                    "Is MegabonkToggleEverything.dll present where this mod can load it?");
            }
        }

        // The DLL's own MelonMod entry point (MegabonkToggleEverything.ToggleEverything) never
        // runs, since we deliberately never let MelonLoader load it as a second mod (see the file
        // header) — only its Harmony patches get applied, directly, via PatchAll above. Every one
        // of those patches opens by calling a LogInfo() helper that reads a private static
        // "Logger" field, which its own OnInitializeMelon() would normally set from MelonMod's
        // LoggerInstance — since that never runs, Logger stays null and LogInfo() throws a
        // NullReferenceException as the very first line of every patch, aborting the rest of the
        // method before it does any real work. Confirmed live: this was silently breaking chest
        // opening and Shady Guy interactions entirely, since both roll their item through
        // InventoryUtility.GetRandomItemsShadyGuy / ItemUtility.GetRandomItemFromRarity, which
        // this DLL patches and which call LogInfo as their first line.
        //
        // This sets that field to a real MelonLogger.Instance via reflection, with no compile-time
        // reference to MelonLoader.dll — needed because this same file also compiles under
        // BepInEx, where MelonLoader.dll (and so this whole DLL, which hard-depends on it) isn't
        // loaded at all; there, the lookups below simply find nothing and this quietly no-ops.
        private static void EnsureToggleEverythingLoggerInitialized(System.Reflection.Assembly toggleAssembly)
        {
            try
            {
                Type toggleEverythingType = toggleAssembly.GetType("MegabonkToggleEverything.ToggleEverything");
                System.Reflection.FieldInfo loggerField = toggleEverythingType?.GetField(
                    "Logger", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (loggerField == null || loggerField.GetValue(null) != null)
                    return;

                System.Reflection.Assembly melonLoaderAssembly = null;
                foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.GetName().Name == "MelonLoader")
                    {
                        melonLoaderAssembly = asm;
                        break;
                    }
                }
                if (melonLoaderAssembly == null)
                    return;

                Type loggerInstanceType = melonLoaderAssembly.GetType("MelonLoader.MelonLogger+Instance");
                System.Reflection.ConstructorInfo ctor = loggerInstanceType?.GetConstructor(new[] { typeof(string) });
                if (ctor == null)
                    return;

                object loggerInstance = ctor.Invoke(new object[] { "MegabonkToggleEverything" });
                loggerField.SetValue(null, loggerInstance);
                _instance?._logger.Msg("BetterBonk: initialized Toggle Everything's logger (its own entry point never runs, so this does it instead).");
            }
            catch (Exception ex)
            {
                _instance?._logger.Warning($"BetterBonk: couldn't initialize Toggle Everything's logger ({ex.Message}); its patches may still error.");
            }
        }

        private void RemoveToggleEverything()
        {
            try
            {
                // Harmony 2's instance UnpatchAll(string) is obsolete-as-error; unpatching by id
                // is now a static method on Harmony itself.
                if (_toggleEverythingHarmony != null)
                    HarmonyApi.UnpatchID(ToggleEverythingHarmonyId);
                _toggleEverythingApplied = false;
                _logger.Msg("BetterBonk: Toggle Everything disabled.");
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to disable Toggle Everything ({ex.Message}).");
            }
        }

        // --- Presets: named snapshots of which achievements/items are currently toggled off ---
        //
        // Toggle Everything itself keeps no state of its own — it patches MyAchievements so the
        // player can flip each achievement's activation on/off from the game's own achievement
        // screen, but the actual on/off state IS the game's own persisted data:
        // SaveManager.Instance.progression.inactivated, a HashSet<string> of internal names
        // (confirmed by decompiling MegabonkToggleEverything.dll's MyAchievements_IsActivated_
        // Prefix, which treats membership in exactly this set as "toggled off", and
        // RunUnlockables_OnNewRunStarted_Postfix, which reads the same set to banish each
        // inactivated item/weapon at the start of a run). A "preset" here is just a saved
        // snapshot of that set's contents, so switching presets means replacing the live set
        // wholesale and asking the game to persist its own save immediately (SaveManager.
        // SaveProgression()) rather than waiting for the game's own autosave timing.
        private ToggleEverythingPresetStore _toggleEverythingPresetStore;
        private string _activeToggleEverythingPresetName;

        private void EnsureToggleEverythingPresetStore()
        {
            if (_toggleEverythingPresetStore != null)
                return;

            _toggleEverythingPresetStore = new ToggleEverythingPresetStore(_configStore.ConfigDirectory, _logger);
            _activeToggleEverythingPresetName = _toggleEverythingPresetStore.LoadActivePresetName();
            // Cosmetic-only correction, same as ModCore.cs's constructor does for the FilterConfig
            // preset system — see PresetStore's file header for why this is never re-validated
            // against the live inactivated set itself.
            if (!_toggleEverythingPresetStore.ListPresetNames().Contains(_activeToggleEverythingPresetName, StringComparer.OrdinalIgnoreCase))
                _activeToggleEverythingPresetName = ToggleEverythingPresetStore.DefaultPresetName;
        }

        private List<string> GetCurrentInactivatedSnapshot()
        {
            var snapshot = new List<string>();
            try
            {
                var inactivated = SaveManager.Instance?.progression?.inactivated;
                if (inactivated == null)
                {
                    _logger.Warning("BetterBonk: couldn't read the current Toggle Everything state (no save loaded yet).");
                    return snapshot;
                }

                foreach (string name in inactivated)
                    snapshot.Add(name);
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to read the current Toggle Everything state ({ex.Message}).");
            }
            return snapshot;
        }

        private void ApplyInactivatedSnapshot(List<string> names)
        {
            try
            {
                var inactivated = SaveManager.Instance?.progression?.inactivated;
                if (inactivated == null)
                {
                    _logger.Warning("BetterBonk: couldn't apply Toggle Everything preset (no save loaded yet).");
                    return;
                }

                inactivated.Clear();
                foreach (string name in names ?? new List<string>())
                    inactivated.Add(name);

                SaveManager.Instance.SaveProgression();
            }
            catch (Exception ex)
            {
                _logger.Warning($"BetterBonk: failed to apply Toggle Everything preset ({ex.Message}).");
            }
        }

        // Replaces the live inactivated set wholesale with the named preset's, then persists it
        // as the active one so the switch survives a restart. No-ops (besides the warning
        // ToggleEverythingPresetStore.Load already logs) if the preset can't be loaded.
        private void ApplyToggleEverythingPreset(string name)
        {
            EnsureToggleEverythingPresetStore();
            List<string> loaded = _toggleEverythingPresetStore.Load(name);
            if (loaded == null)
                return;

            ApplyInactivatedSnapshot(loaded);
            _activeToggleEverythingPresetName = name;
            _toggleEverythingPresetStore.SaveActivePresetName(_activeToggleEverythingPresetName);
            _logger.Msg($"BetterBonk: switched Toggle Everything to preset '{name}'.");
        }

        // Saves the CURRENT live toggle state as a new (or overwritten) named preset and makes
        // it the active one. Returns false (with a warning already logged) if the name is
        // unusable, leaving the picker's text box open for another attempt rather than silently
        // failing.
        private bool SaveCurrentAsNewToggleEverythingPreset(string rawName)
        {
            EnsureToggleEverythingPresetStore();
            string name = ToggleEverythingPresetStore.SanitizeName(rawName);
            if (!_toggleEverythingPresetStore.Save(name, GetCurrentInactivatedSnapshot()))
                return false;

            _activeToggleEverythingPresetName = name;
            _toggleEverythingPresetStore.SaveActivePresetName(_activeToggleEverythingPresetName);
            _logger.Msg($"BetterBonk: saved current Toggle Everything state as preset '{name}'.");
            return true;
        }

        // Overwrites the currently-active preset with the current live toggle state. The menu
        // only shows this option when the active preset isn't Default (see ModMenu.cs), but
        // ToggleEverythingPresetStore.Save would refuse a Default overwrite anyway.
        private void UpdateActiveToggleEverythingPreset()
        {
            EnsureToggleEverythingPresetStore();
            if (!_toggleEverythingPresetStore.Save(_activeToggleEverythingPresetName, GetCurrentInactivatedSnapshot()))
                return;

            _logger.Msg($"BetterBonk: updated Toggle Everything preset '{_activeToggleEverythingPresetName}'.");
        }

        // If the preset being deleted was the active one, fall back to Default rather than
        // leaving _activeToggleEverythingPresetName pointing at a name that no longer exists.
        private void DeleteToggleEverythingPreset(string name)
        {
            EnsureToggleEverythingPresetStore();
            if (ToggleEverythingPresetStore.IsDefault(name))
                return;

            _toggleEverythingPresetStore.Delete(name);
            if (string.Equals(_activeToggleEverythingPresetName, name, StringComparison.OrdinalIgnoreCase))
            {
                _activeToggleEverythingPresetName = ToggleEverythingPresetStore.DefaultPresetName;
                _toggleEverythingPresetStore.SaveActivePresetName(_activeToggleEverythingPresetName);
            }
            _logger.Msg($"BetterBonk: deleted Toggle Everything preset '{name}'.");
        }
    }
}
