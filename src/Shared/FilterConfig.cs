namespace BetterBonk.Shared
{
    // Every user-adjustable setting, saved to/loaded from a JSON file so the MelonLoader and
    // BepInEx builds share identical, portable settings. The default value of each property
    // below reproduces the original Fast Reset mod's hardcoded behaviour exactly, so installing
    // this with no changes made in the menu behaves the same as the original did.
    public sealed class FilterConfig
    {
        // --- General ---
        public bool ModEnabled { get; set; } = true;

        // KeyCode names (e.g. "F6"), stored as strings so they're easy to read/edit by hand
        // in the JSON file too. Parsed with Enum.TryParse<KeyCode> at use time.
        public string ToggleModKey { get; set; } = "F6";   // fully enables/disables auto-reset
        public string ToggleMenuKey { get; set; } = "F7";  // opens/closes the in-game settings window

        // Small always-on-screen "Quick Reset: ON/OFF" label, since the toggle key alone gives
        // no feedback about which state the mod is currently in.
        public bool ShowStatusIndicator { get; set; } = true;

        // A 0-100 score for how well the current run's spawns matched your configured
        // requisites, recomputed each time a new run is checked. Shown under the ON/OFF
        // indicator, independent of whether ShowStatusIndicator itself is on.
        public bool ShowMapScore { get; set; } = true;

        // The Map Score (see above) a run must reach to be accepted (paused) rather than
        // reset. Defaults to 100, which reproduces the original strict behaviour exactly:
        // every category ratio is capped at 1.0, so the average can only reach 100 when
        // every configured requisite is fully met. Lowering this (e.g. to 80) accepts a
        // run that's "good enough" overall even if one or more categories fall short.
        public int AcceptableMapScore { get; set; } = 100;

        // --- Core requisites (defaults reproduce the original mod's behaviour) ---
        // Shady Guys and Moai can be checked as one combined minimum (the default, matching the
        // original mod) or as two independent minimums — see MinShadyGuyCount/MinMoaiCount below.
        public bool UseSeparateShadyAndMoaiCounts { get; set; } = false;
        public int MinCombinedShadyAndMoai { get; set; } = 8;
        public int MinShadyGuyCount { get; set; } = 0;
        public int MinMoaiCount { get; set; } = 0;
        public int MinLegendaryShadyCount { get; set; } = 1;
        public int MinMicrowaveCount { get; set; } = 2;

        // Highest rarity index a Microwave may have and still count as acceptable.
        // 0 = Common only, matching the original mod's "Basic Microwaves" requirement.
        // Rarity is read from the game's own EItemRarity enum (confirmed by decompiling
        // Assembly-CSharp.dll): 0 = Common, 1 = Rare, 2 = Epic, 3 = Legendary, 4 = Corrupted,
        // 5 = Quest. Stored here as a plain int rather than the enum type itself so this file
        // has no dependency on the game's assemblies.
        public int MaxAcceptableMicrowaveRarity { get; set; } = 0;

        // Boss Curse = the game's InteractableBossSpawner (its own FX field is literally named
        // "bossCurseFx" in the game's assembly, confirming the name). Legendary Charge Shrine =
        // a ChargeShrine with isGolden true. Both default to 0 (no requirement) since they're
        // new asks and shouldn't change existing users' behaviour until explicitly raised.
        public int MinBossCurseCount { get; set; } = 0;
        public int MinLegendaryChargeShrineCount { get; set; } = 0;

        // Not user-configurable: "Legendary" is compared directly against the game's own
        // EItemRarity.Legendary enum value (see ModCore), and the per-run check window
        // (gameTimer 1.0s-2.0s, matching the original mod) is hardcoded in ModCore too — both
        // were user-adjustable in an earlier version of this file, but neither is something a
        // player has a reason to tune, so they were removed to keep the menu focused.

        // --- "Legendary Surge": enough Legendary Shady Guys relaxes the requisites above ---
        public bool EnableLegendarySurge { get; set; } = false;
        public int LegendarySurgeThreshold { get; set; } = 2;
        public int LegendarySurgeCombinedReduction { get; set; } = 3;
        public int LegendarySurgeMicrowaveReduction { get; set; } = 1;

        // --- Require a specific Legendary item to be on offer from a Shady Guy ---
        // Stored as the game's EItem enum name (e.g. "ZaWarudo") rather than the enum type
        // itself, for the same reason as the hotkey names above: keeps this file independent
        // of the game's assemblies, and human-readable/editable in the JSON file. Only ever
        // matched against items a Shady Guy is offering at Legendary rarity — see ModCore.
        public bool RequireSpecificLegendaryItem { get; set; } = false;
        public string RequiredLegendaryItemName { get; set; } = "";

        // --- Pot Breaking ---
        public bool AutoBreakPots { get; set; } = true;

        // --- Personal Leaderboard ---
        public bool PersonalLeaderboardEnabled { get; set; } = true;

        // Stored as the game's ECharacter enum name (e.g. "Knight"), same reasoning as
        // RequiredLegendaryItemName above. Empty string means "no filter, show every
        // character's scores".
        public string PersonalLeaderboardCharacterFilter { get; set; } = "";

        // --- Toggle Everything ---
        public bool ToggleEverythingEnabled { get; set; } = false;

        public static FilterConfig CreateDefault()
        {
            return new FilterConfig();
        }
    }
}
