using System;
using System.Linq;
using Il2Cpp;
using Il2CppAssets.Scripts.Inventory__Items__Pickups.Items;
using UnityEngine;

namespace FastResetUpdated.Shared
{
    // Core Fast Reset logic, compiled identically into both the MelonLoader and BepInEx builds
    // (see each project's .csproj — this file is included from ..\Shared, it is not its own
    // assembly). A loader entry point owns one instance of this class and calls OnUpdate() every
    // frame, and OnGUI() every IMGUI pass while the settings window should be able to draw.
    //
    // The requisite-checking logic in RunSpawnCheckIfDue/CheckSpawns is a direct, verified port
    // of the original Fast Reset mod (by mzzJuice) — decompiled to confirm the exact game hooks,
    // thresholds and timing window before writing this. The "Legendary Surge" rule and all the
    // configurable thresholds are new.
    public sealed partial class ModCore
    {
        private readonly IModLogger _logger;
        private readonly ConfigStore _configStore;
        private readonly PresetStore _presetStore;

        public FilterConfig Config { get; private set; }

        // Best-effort label of "which preset the current settings came from" — see PresetStore's
        // file-level comment for why this isn't re-validated against Config's actual contents.
        // Shared with ModMenu.cs/ModPresetPicker.cs (same partial class) for the preset UI.
        private string _activePresetName;

        // True once the current run has already been checked, so we only ever check (and
        // potentially pause or reset) a given run a single time.
        private bool _hasCheckedThisRun;

        // Tracks gm.isPlaying across frames purely to detect the not-playing -> playing edge —
        // see RunSpawnCheckIfDue for why this, and not just gm.isGameOver, is what re-arms
        // _hasCheckedThisRun for a new run.
        private bool _wasPlayingLastFrame;

        // 0-100, or -1 if no run has been scored yet (fresh boot, or between runs). Shared with
        // ModMenu.cs (same partial class) for the optional on-screen display.
        private int _currentMapScore = -1;

        // Previous-frame key state for edge-detecting a fresh press via Win32Input (see
        // PollHotkeys) rather than a held-down key re-triggering every frame.
        private bool _toggleModKeyWasDown;
        private bool _toggleMenuKeyWasDown;

        public ModCore(IModLogger logger, ConfigStore configStore)
        {
            _logger = logger;
            _configStore = configStore;
            Config = _configStore.Load();

            _presetStore = new PresetStore(_configStore.ConfigDirectory, logger);
            _activePresetName = _presetStore.LoadActivePresetName();
            // Cosmetic-only correction: if whatever was last marked "active" no longer exists
            // (its file was deleted outside the menu, or the marker is stale from a much older
            // version), fall back to Default rather than the menu forever showing a preset name
            // that isn't in the list it just built.
            if (!_presetStore.ListPresetNames().Contains(_activePresetName, StringComparer.OrdinalIgnoreCase))
                _activePresetName = PresetStore.DefaultPresetName;
        }

        public void SaveConfig()
        {
            _configStore.Save(Config);
        }

        public void ResetConfigToDefaults()
        {
            Config = FilterConfig.CreateDefault();
            SaveConfig();
            _activePresetName = PresetStore.DefaultPresetName;
            _presetStore.SaveActivePresetName(_activePresetName);
            _logger.Msg("FastReset+: settings reset to defaults.");
        }

        // Replaces the current settings wholesale with the named preset's (Config is a brand
        // new FilterConfig instance loaded from disk/defaults, not merged field-by-field), then
        // persists it as the main config too so the switch survives a restart. No-ops (besides
        // the warning PresetStore.Load already logs) if the preset can't be loaded, leaving
        // whatever was active untouched.
        private void ApplyPreset(string name)
        {
            FilterConfig loaded = _presetStore.Load(name);
            if (loaded == null)
                return;

            Config = loaded;
            SaveConfig();
            _activePresetName = name;
            _presetStore.SaveActivePresetName(_activePresetName);
            _logger.Msg($"FastReset+: switched to preset '{name}'.");
        }

        // Saves the CURRENT settings as a new (or overwritten) named preset and makes it the
        // active one. Returns false (with a warning already logged by PresetStore.Save, or by
        // the empty/"Default" check here) if the name is unusable, leaving the picker's text
        // box open for another attempt rather than silently failing.
        private bool SaveCurrentAsNewPreset(string rawName)
        {
            string name = PresetStore.SanitizeName(rawName);
            if (!_presetStore.Save(name, Config))
                return false;

            _activePresetName = name;
            _presetStore.SaveActivePresetName(_activePresetName);
            _logger.Msg($"FastReset+: saved current settings as preset '{name}'.");
            return true;
        }

        // Overwrites the currently-active preset with the current settings. The menu only shows
        // this option when the active preset isn't Default (see ModMenu.cs), but PresetStore.Save
        // would refuse a Default overwrite anyway if it were somehow called with it.
        private void UpdateActivePreset()
        {
            if (!_presetStore.Save(_activePresetName, Config))
                return;

            _logger.Msg($"FastReset+: updated preset '{_activePresetName}'.");
        }

        // If the preset being deleted was the active one, fall back to Default rather than
        // leaving _activePresetName pointing at a name that no longer exists on disk.
        private void DeletePreset(string name)
        {
            if (PresetStore.IsDefault(name))
                return;

            _presetStore.Delete(name);
            if (string.Equals(_activePresetName, name, StringComparison.OrdinalIgnoreCase))
            {
                _activePresetName = PresetStore.DefaultPresetName;
                _presetStore.SaveActivePresetName(_activePresetName);
            }
            _logger.Msg($"FastReset+: deleted preset '{name}'.");
        }

        // Call once per frame from the loader's Update callback.
        public void OnUpdate()
        {
            PollHotkeys();

            // Always called, not gated by Config.ModEnabled: RunSpawnCheckIfDue also owns
            // detecting when a run ends (gm.isGameOver) and re-arming _hasCheckedThisRun for the
            // next one. Gating this whole call on ModEnabled used to mean that if a run ended
            // while the mod was toggled off, that transition was never observed, and turning the
            // mod back on later found _hasCheckedThisRun already true — so it silently skipped
            // every run until a game-over happened to occur while enabled. The enabled check now
            // lives inside CheckSpawns itself, after scoring, right before the pause/reset action.
            RunSpawnCheckIfDue();
        }

        private void PollHotkeys()
        {
            // Read straight from Win32 rather than UnityEngine.Input — see Win32Input.cs for
            // why (Megabonk's Rewired-based input handling swallows Unity's own key polling).
            // Hotkeys are polled regardless of ModEnabled so the toggle key always works, even
            // while the mod is currently switched off.
            if (PollKeyJustPressed(Config.ToggleModKey, ref _toggleModKeyWasDown))
            {
                Config.ModEnabled = !Config.ModEnabled;
                SaveConfig();
                _logger.Msg($"FastReset+: mod {(Config.ModEnabled ? "enabled" : "disabled")}.");
            }

            if (PollKeyJustPressed(Config.ToggleMenuKey, ref _toggleMenuKeyWasDown))
                ToggleMenu();
        }

        // True only on the frame a configured key transitions from up to down, so a held key
        // doesn't re-trigger every frame.
        private static bool PollKeyJustPressed(string keyName, ref bool wasDownLastFrame)
        {
            if (!Win32Input.TryGetVirtualKey(keyName, out int virtualKeyCode))
            {
                wasDownLastFrame = false;
                return false;
            }

            bool isDown = Win32Input.IsKeyDown(virtualKeyCode);
            bool justPressed = isDown && !wasDownLastFrame;
            wasDownLastFrame = isDown;
            return justPressed;
        }

        // Matches the original mod's own timing exactly. Not user-configurable — there's no
        // real reason to tune this, and it was a source of confusion as an "Advanced" setting.
        private const float CheckWindowStartSeconds = 1.0f;
        private const float CheckWindowEndSeconds = 2.0f;

        // Mirrors the original mod's OnUpdate: only look at spawns once per run, and only
        // during a short window of gameTimer shortly after the run starts.
        private void RunSpawnCheckIfDue()
        {
            GameManager gm = GameManager.Instance;
            bool isPlayingNow = gm != null && gm.isPlaying;

            // The primary re-arm signal used to be gm.isGameOver alone, which turned out not to
            // fire reliably on every path back into a fresh run — dying resets it fine, but
            // backing out to the main menu and starting again did not, leaving
            // _hasCheckedThisRun stuck true and silently skipping every run after that. Tracking
            // the not-playing -> playing transition instead catches every way a new run can
            // start (dying, completing, quitting to the main menu, anything else) without
            // depending on a specific flag from the game that might not toggle when expected.
            if (isPlayingNow && !_wasPlayingLastFrame)
            {
                _hasCheckedThisRun = false;
                _currentMapScore = -1;
            }
            _wasPlayingLastFrame = isPlayingNow;

            if (!isPlayingNow)
                return;

            // Kept as a second, redundant re-arm path — harmless if the transition check above
            // already caught it, and a safety net for a mid-run "game over" state that isn't
            // itself a not-playing transition (e.g. a death screen shown while isPlaying is
            // still true).
            if (gm.isGameOver)
            {
                _hasCheckedThisRun = false;
                _currentMapScore = -1;
                return;
            }

            // Not gated by Config.ModEnabled: CheckSpawns always scores the run (for the
            // optional score display) and only skips the actual pause/reset action internally
            // when the mod is toggled off.
            if (_hasCheckedThisRun)
                return;

            bool inCheckWindow = gm.gameTimer > CheckWindowStartSeconds
                              && gm.gameTimer < CheckWindowEndSeconds;
            if (!inCheckWindow)
                return;

            _hasCheckedThisRun = true;
            CheckSpawns();
        }

        private void CheckSpawns()
        {
            // Deliberately typed as `var`, not an explicit array type: FindObjectsOfType<T>()
            // returns an IL2CPP interop array wrapper, not a plain C# T[] (confirmed by the
            // original mod's decompiled IL iterating it via GetEnumerator/MoveNext rather than
            // an indexed loop) — `var` compiles correctly either way without assuming which.
            var shadyGuys = UnityEngine.Object.FindObjectsOfType<InteractableShadyGuy>();
            int moaiCount = UnityEngine.Object.FindObjectsOfType<InteractableShrineMoai>().Length;
            var microwaves = UnityEngine.Object.FindObjectsOfType<InteractableMicrowave>();
            int bossCurseCount = UnityEngine.Object.FindObjectsOfType<InteractableBossSpawner>().Length;
            var chargeShrines = UnityEngine.Object.FindObjectsOfType<ChargeShrine>();

            // Only meaningful when Config.RequireSpecificLegendaryItem is on — parsed once
            // up-front rather than per Shady Guy. An empty/unparsable name (e.g. nothing chosen
            // yet in the picker) means the requirement can never be satisfied, which is the
            // correct behaviour: nothing selected shouldn't silently pass.
            bool requireSpecificItem = Config.RequireSpecificLegendaryItem;
            EItem requiredItem = default;
            bool hasRequiredItemName = requireSpecificItem &&
                Enum.TryParse(Config.RequiredLegendaryItemName, out requiredItem);

            int legendaryShadyCount = 0;
            bool requiredItemFound = false;
            foreach (InteractableShadyGuy guy in shadyGuys)
            {
                int rarity = (int)guy.rarity;
                _logger.Msg($"  ShadyGuy rarity: {rarity}");
                if (guy.rarity == EItemRarity.Legendary)
                    legendaryShadyCount++;

                if (requireSpecificItem && hasRequiredItemName && !requiredItemFound && guy.items != null)
                {
                    foreach (ItemData item in guy.items)
                    {
                        if (item.eItem == requiredItem && item.rarity == EItemRarity.Legendary)
                        {
                            requiredItemFound = true;
                            break;
                        }
                    }
                }
            }

            bool allMicrowavesAcceptableRarity = true;
            foreach (InteractableMicrowave microwave in microwaves)
            {
                int rarity = (int)microwave.rarity;
                _logger.Msg($"  Microwave rarity: {rarity}");
                if (rarity > Config.MaxAcceptableMicrowaveRarity)
                    allMicrowavesAcceptableRarity = false;
            }

            int legendaryChargeShrineCount = 0;
            foreach (ChargeShrine shrine in chargeShrines)
            {
                _logger.Msg($"  ChargeShrine isGolden: {shrine.isGolden}");
                if (shrine.isGolden)
                    legendaryChargeShrineCount++;
            }

            // Legendary Surge: enough Legendary Shady Guys relaxes the other requisites.
            bool surgeActive = Config.EnableLegendarySurge && legendaryShadyCount >= Config.LegendarySurgeThreshold;
            int effectiveMinMicrowave = Config.MinMicrowaveCount;
            if (surgeActive)
                effectiveMinMicrowave = Math.Max(0, effectiveMinMicrowave - Config.LegendarySurgeMicrowaveReduction);

            // Shady Guys + Moai can be tracked as one combined minimum (the original mod's
            // behaviour) or as two independent minimums. Legendary Surge's "combined reduction"
            // applies to whichever mode is active — in separate mode it's subtracted from each
            // of the two thresholds individually, not split between them.
            bool separateShadyAndMoai = Config.UseSeparateShadyAndMoaiCounts;
            int combinedCount = shadyGuys.Length + moaiCount;
            // Declared here (not inside the branches below) so the run-score calculation further
            // down can see whichever one actually applied.
            int effectiveMinShady = Config.MinShadyGuyCount;
            int effectiveMinMoai = Config.MinMoaiCount;
            int effectiveMinCombined = Config.MinCombinedShadyAndMoai;
            string combinedNote;
            if (separateShadyAndMoai)
            {
                if (surgeActive)
                {
                    effectiveMinShady = Math.Max(0, effectiveMinShady - Config.LegendarySurgeCombinedReduction);
                    effectiveMinMoai = Math.Max(0, effectiveMinMoai - Config.LegendarySurgeCombinedReduction);
                }
                combinedNote = surgeActive ? $"{effectiveMinShady} Shady Guys / {effectiveMinMoai} Moai" : string.Empty;
            }
            else
            {
                if (surgeActive)
                    effectiveMinCombined = Math.Max(0, effectiveMinCombined - Config.LegendarySurgeCombinedReduction);
                combinedNote = surgeActive ? $"{effectiveMinCombined} combined" : string.Empty;
            }

            string surgeNote = surgeActive
                ? $" [Legendary Surge active — needs {combinedNote} / {effectiveMinMicrowave} microwaves]"
                : string.Empty;
            string itemNote = requireSpecificItem
                ? $", Required item '{Config.RequiredLegendaryItemName}' found: {requiredItemFound}"
                : string.Empty;

            _logger.Msg(
                $"ShadyGuys: {shadyGuys.Length} (Legendary: {legendaryShadyCount}), Moai: {moaiCount}" +
                $"{(separateShadyAndMoai ? " [separate mode]" : $" (combined: {combinedCount})")}, " +
                $"Microwaves: {microwaves.Length} (rarity OK: {allMicrowavesAcceptableRarity}), " +
                $"BossCurses: {bossCurseCount}, ChargeShrines: {chargeShrines.Length} " +
                $"(Legendary/golden: {legendaryChargeShrineCount}){surgeNote}{itemNote}");

            // Computed and stored regardless of Config.ModEnabled — the score display is its own
            // toggle (Config.ShowMapScore), independent of whether auto-reset is currently active.
            _currentMapScore = ComputeMapScore(
                separateShadyAndMoai, shadyGuys.Length, moaiCount, effectiveMinShady, effectiveMinMoai,
                combinedCount, effectiveMinCombined, legendaryShadyCount,
                microwaves.Length, effectiveMinMicrowave, allMicrowavesAcceptableRarity,
                bossCurseCount, legendaryChargeShrineCount, requiredItemFound);

            // Drives the pause/reset decision directly off the score against the configured
            // threshold, replacing the old separate per-category AND. At the default
            // AcceptableMapScore of 100 this is exactly equivalent to that old strict check: every
            // category ratio ComputeMapScore adds is capped at 1.0 (see RatioForActiveCategory), so
            // the average can only reach 100 when every active category is individually fully met.
            // Lowering AcceptableMapScore accepts a run that's "good enough" overall even if one or
            // more categories fall short.
            bool good = _currentMapScore >= Config.AcceptableMapScore;

            if (!Config.ModEnabled)
            {
                _logger.Msg("FastReset+: mod disabled — spawns scored but no auto pause/reset.");
                return;
            }

            if (good)
            {
                _logger.Msg("Good spawns found! Pausing and turning mod off...");
                PauseUi pauseUi = UnityEngine.Object.FindObjectOfType<PauseUi>(true);
                if (pauseUi != null)
                    pauseUi.Pause();

                // Once a suitable map is accepted, turn the mod off rather than leaving it armed
                // for the rest of the run. Without this, anything that re-arms _hasCheckedThisRun
                // later in the run (e.g. gm.isPlaying briefly flipping false/true again around the
                // pause itself, or some other transition RunSpawnCheckIfDue treats as "a new run
                // started") could trigger another spawn check deep into the run and reset it —
                // discarding a run the player had already been given and had started playing.
                // With the mod off, RunSpawnCheckIfDue can still re-arm and CheckSpawns can still
                // run and score, but the pause/reset gate right above short-circuits before this
                // point, so nothing further happens until the player re-enables it themselves.
                Config.ModEnabled = false;
                SaveConfig();
            }
            else
            {
                _logger.Msg("Bad spawns — restarting run...");
                _hasCheckedThisRun = false;

                ResetRunUi resetUi = UnityEngine.Object.FindObjectOfType<ResetRunUi>(true);
                if (resetUi != null)
                {
                    _logger.Msg("Found ResetRunUi — forcing restart...");
                    // Simulates the reset button already having been held down long enough to
                    // fire, exactly as the original mod does, rather than reimplementing
                    // whatever the run-reset logic actually does internally.
                    resetUi.holding = true;
                    resetUi.startedHoldingTime = Time.time - resetUi.GetHoldTime();
                    resetUi.UpdateBar();
                }
                else
                {
                    _logger.Warning("ResetRunUi NOT found!");
                }
            }
        }

        // Scores this run (the "Map Score") 0-100 against the requisites you've actually
        // configured — a run that fully meets every one of them (at whatever Legendary Surge has
        // currently relaxed them to) scores 100. A requisite left at its "off" value (a Min of 0,
        // or the required-item toggle switched off) isn't counted at all, so leaving something
        // unconfigured doesn't hand the score a free 100% for it, but doesn't drag it down either.
        // This score is what the pause/reset decision is now driven by (see CheckSpawns, against
        // Config.AcceptableMapScore), rather than a separate per-category AND.
        private int ComputeMapScore(
            bool separateShadyAndMoai,
            int shadyGuyCount, int moaiCount, int effectiveMinShady, int effectiveMinMoai,
            int combinedCount, int effectiveMinCombined,
            int legendaryShadyCount,
            int microwaveCount, int effectiveMinMicrowave, bool allMicrowavesAcceptableRarity,
            int bossCurseCount, int legendaryChargeShrineCount,
            bool requiredItemFound)
        {
            float sum = 0f;
            int count = 0;
            // Logged in full below every time a score is computed, so it's always possible to
            // see exactly which categories were active and what each contributed — the score
            // itself has no way to show this breakdown on screen.
            var breakdown = new System.Text.StringBuilder();
            void AddCategory(string name, float ratio)
            {
                sum += ratio;
                count++;
                breakdown.Append($"{name}={ratio * 100f:0}% ");
            }

            if (separateShadyAndMoai)
            {
                if (Config.MinShadyGuyCount > 0)
                    AddCategory("ShadyGuys", RatioForActiveCategory(shadyGuyCount, effectiveMinShady));
                if (Config.MinMoaiCount > 0)
                    AddCategory("Moai", RatioForActiveCategory(moaiCount, effectiveMinMoai));
            }
            else if (Config.MinCombinedShadyAndMoai > 0)
            {
                AddCategory("Combined(Shady+Moai)", RatioForActiveCategory(combinedCount, effectiveMinCombined));
            }

            if (Config.MinLegendaryShadyCount > 0)
                AddCategory("LegendaryShady", RatioForActiveCategory(legendaryShadyCount, Config.MinLegendaryShadyCount));

            // Included whenever there's a count minimum OR any microwaves actually spawned this
            // run — the rarity cap (MaxAcceptableMicrowaveRarity) always applies regardless of
            // MinMicrowaveCount, so a legendary microwave with MinMicrowaveCount at 0 should still
            // pull the score down.
            if (Config.MinMicrowaveCount > 0 || microwaveCount > 0)
            {
                float countRatio = RatioForActiveCategory(microwaveCount, effectiveMinMicrowave);
                AddCategory("Microwaves", allMicrowavesAcceptableRarity ? countRatio : 0f);
            }

            if (Config.MinBossCurseCount > 0)
                AddCategory("BossCurses", RatioForActiveCategory(bossCurseCount, Config.MinBossCurseCount));

            if (Config.MinLegendaryChargeShrineCount > 0)
                AddCategory("LegendaryChargeShrines", RatioForActiveCategory(legendaryChargeShrineCount, Config.MinLegendaryChargeShrineCount));

            if (Config.RequireSpecificLegendaryItem)
                AddCategory("RequiredItem", requiredItemFound ? 1f : 0f);

            int score;
            if (count == 0)
            {
                score = 100; // nothing configured to score against — vacuously perfect, same as "good" being trivially true
                breakdown.Append("(nothing configured — vacuously 100%)");
            }
            else
            {
                float average = sum / count;
                score = Mathf.RoundToInt(Mathf.Clamp01(average) * 100f);
            }

            _logger.Msg($"FastReset+: score breakdown [{count} categories]: {breakdown} -> {score}/100");
            return score;
        }

        // A requisite Legendary Surge has relaxed down to (or below) 0 counts as fully met — it's
        // still a configured requisite, just currently waived, not one the player never cared
        // about (that case is filtered out by the caller before this is even reached).
        private static float RatioForActiveCategory(int actual, int effectiveTarget)
        {
            if (effectiveTarget <= 0)
                return 1f;
            return Math.Min(1f, (float)actual / effectiveTarget);
        }
    }
}
