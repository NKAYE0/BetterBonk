using System;
using Il2Cpp;
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

        public FilterConfig Config { get; private set; }

        // True once the current run has already been checked, so we only ever check (and
        // potentially pause or reset) a given run a single time.
        private bool _hasCheckedThisRun;

        public ModCore(IModLogger logger, ConfigStore configStore)
        {
            _logger = logger;
            _configStore = configStore;
            Config = _configStore.Load();
        }

        public void SaveConfig()
        {
            _configStore.Save(Config);
        }

        public void ResetConfigToDefaults()
        {
            Config = FilterConfig.CreateDefault();
            SaveConfig();
            _logger.Msg("Fast Reset Updated: settings reset to defaults.");
        }

        // Call once per frame from the loader's Update callback.
        public void OnUpdate()
        {
            PollHotkeys();

            if (Config.ModEnabled)
                RunSpawnCheckIfDue();
        }

        private void PollHotkeys()
        {
            // Hotkeys are polled regardless of ModEnabled so the toggle key always works,
            // even while the mod is currently switched off.
            if (TryParseKey(Config.ToggleModKey, out KeyCode toggleKey) && Input.GetKeyDown(toggleKey))
            {
                Config.ModEnabled = !Config.ModEnabled;
                SaveConfig();
                _logger.Msg($"Fast Reset Updated: mod {(Config.ModEnabled ? "enabled" : "disabled")}.");
            }

            if (TryParseKey(Config.ToggleMenuKey, out KeyCode menuKey) && Input.GetKeyDown(menuKey))
                ToggleMenu();
        }

        private static bool TryParseKey(string keyName, out KeyCode key)
        {
            if (!string.IsNullOrEmpty(keyName) && Enum.TryParse(keyName, ignoreCase: true, result: out key))
                return true;

            key = default;
            return false;
        }

        // Mirrors the original mod's OnUpdate: only look at spawns once per run, and only
        // during a short window of gameTimer shortly after the run starts.
        private void RunSpawnCheckIfDue()
        {
            GameManager gm = GameManager.Instance;

            if (gm == null || !gm.isPlaying)
                return;

            if (gm.isGameOver)
            {
                _hasCheckedThisRun = false;
                return;
            }

            if (_hasCheckedThisRun)
                return;

            bool inCheckWindow = gm.gameTimer > Config.CheckWindowStartSeconds
                              && gm.gameTimer < Config.CheckWindowEndSeconds;
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

            int legendaryShadyCount = 0;
            foreach (InteractableShadyGuy guy in shadyGuys)
            {
                int rarity = (int)guy.rarity;
                _logger.Msg($"  ShadyGuy rarity: {rarity}");
                if (rarity == Config.LegendaryRarityValue)
                    legendaryShadyCount++;
            }

            bool allMicrowavesAcceptableRarity = true;
            foreach (InteractableMicrowave microwave in microwaves)
            {
                int rarity = (int)microwave.rarity;
                _logger.Msg($"  Microwave rarity: {rarity}");
                if (rarity > Config.MaxAcceptableMicrowaveRarity)
                    allMicrowavesAcceptableRarity = false;
            }

            // Legendary Surge: enough Legendary Shady Guys relaxes the other two requisites.
            bool surgeActive = Config.EnableLegendarySurge && legendaryShadyCount >= Config.LegendarySurgeThreshold;
            int effectiveMinCombined = Config.MinCombinedShadyAndMoai;
            int effectiveMinMicrowave = Config.MinMicrowaveCount;
            if (surgeActive)
            {
                effectiveMinCombined = Math.Max(0, effectiveMinCombined - Config.LegendarySurgeCombinedReduction);
                effectiveMinMicrowave = Math.Max(0, effectiveMinMicrowave - Config.LegendarySurgeMicrowaveReduction);
            }

            int combinedCount = shadyGuys.Length + moaiCount;
            bool combinedOk = combinedCount >= effectiveMinCombined;
            bool legendaryOk = legendaryShadyCount >= Config.MinLegendaryShadyCount;
            bool microwaveCountOk = microwaves.Length >= effectiveMinMicrowave;
            bool microwaveOk = microwaveCountOk && allMicrowavesAcceptableRarity;

            string surgeNote = surgeActive
                ? $" [Legendary Surge active — needs {effectiveMinCombined} combined / {effectiveMinMicrowave} microwaves]"
                : string.Empty;

            _logger.Msg(
                $"ShadyGuys: {shadyGuys.Length} (Legendary: {legendaryShadyCount}), Moai: {moaiCount}, " +
                $"Microwaves: {microwaves.Length} (rarity OK: {allMicrowavesAcceptableRarity}){surgeNote}");

            bool good = combinedOk && legendaryOk && microwaveOk;

            if (good)
            {
                _logger.Msg("Good spawns found! Pausing...");
                PauseUi pauseUi = UnityEngine.Object.FindObjectOfType<PauseUi>(true);
                if (pauseUi != null)
                    pauseUi.Pause();
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
    }
}
