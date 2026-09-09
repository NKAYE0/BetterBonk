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

        // --- Core requisites (defaults reproduce the original mod's behaviour) ---
        public int MinCombinedShadyAndMoai { get; set; } = 8;
        public int MinLegendaryShadyCount { get; set; } = 1;
        public int MinMicrowaveCount { get; set; } = 2;

        // Highest rarity index a Microwave may have and still count as acceptable.
        // 0 = Common only, matching the original mod's "Basic Microwaves" requirement.
        // Rarity is read from the game as an EItemRarity enum; we treat it as a plain int
        // (0 = Common, 1 = Uncommon, 2 = Rare, 3 = Legendary) because that is what the
        // original mod's own comparison (rarity == 3 for "Legendary") implies. If a future
        // game update adds more tiers, raise this value or the ones below — nothing else
        // needs to change.
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

        public static FilterConfig CreateDefault()
        {
            return new FilterConfig();
        }
    }
}
