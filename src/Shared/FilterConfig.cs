namespace FastResetUpdated.Shared
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

        // Small always-on-screen "Fast Reset: ON/OFF" label, since the toggle key alone gives
        // no feedback about which state the mod is currently in.
        public bool ShowStatusIndicator { get; set; } = true;

        // --- Core requisites (defaults reproduce the original mod's behaviour) ---
        public int MinCombinedShadyAndMoai { get; set; } = 8;
        public int MinLegendaryShadyCount { get; set; } = 1;
        public int MinMicrowaveCount { get; set; } = 2;

        // Highest rarity index a Microwave may have and still count as acceptable.
        // 0 = Common only, matching the original mod's "Basic Microwaves" requirement.
        // Rarity is read from the game's own EItemRarity enum (confirmed by decompiling
        // Assembly-CSharp.dll): 0 = Common, 1 = Rare, 2 = Epic, 3 = Legendary, 4 = Corrupted,
        // 5 = Quest. Stored here as a plain int rather than the enum type itself so this file
        // has no dependency on the game's assemblies.
        public int MaxAcceptableMicrowaveRarity { get; set; } = 0;

        // Rarity index that counts as "Legendary" for a Shady Guy.
        public int LegendaryRarityValue { get; set; } = 3;

        // The single per-run check fires once gameTimer enters this window (seconds).
        public float CheckWindowStartSeconds { get; set; } = 1.0f;
        public float CheckWindowEndSeconds { get; set; } = 2.0f;

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

        public static FilterConfig CreateDefault()
        {
            return new FilterConfig();
        }
    }
}
