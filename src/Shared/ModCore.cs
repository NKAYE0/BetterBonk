using System;
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

        public FilterConfig Config { get; private set; }

        // True once the current run has already been checked, so we only ever check (and
        // potentially pause or reset) a given run a single time.
        private bool _hasCheckedThisRun;

        // Previous-frame key state for edge-detecting a fresh press via Win32Input (see
        // PollHotkeys) rather than a held-down key re-triggering every frame.
        private bool _toggleModKeyWasDown;
        private bool _toggleMenuKeyWasDown;

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
            _logger.Msg("FastReset+: settings reset to defaults.");
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
                if (rarity == Config.LegendaryRarityValue)
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
            string itemNote = requireSpecificItem
                ? $", Required item '{Config.RequiredLegendaryItemName}' found: {requiredItemFound}"
                : string.Empty;

            _logger.Msg(
                $"ShadyGuys: {shadyGuys.Length} (Legendary: {legendaryShadyCount}), Moai: {moaiCount}, " +
                $"Microwaves: {microwaves.Length} (rarity OK: {allMicrowavesAcceptableRarity}){surgeNote}{itemNote}");

            bool requiredItemOk = !requireSpecificItem || requiredItemFound;
            bool good = combinedOk && legendaryOk && microwaveOk && requiredItemOk;

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
