namespace FastResetUpdated.Shared
{
    // Human-readable labels for the rarity integers used throughout the menu. These are display
    // strings only — every actual comparison in ModCore uses the plain integers from FilterConfig,
    // so if a game update turns out to use different rarity tiers than assumed here, only the
    // labels shown to the player would need updating.
    public static class RarityLabels
    {
        public static readonly string[] Names = { "Common", "Uncommon", "Rare", "Legendary" };

        public static string NameFor(int rarity)
        {
            return rarity >= 0 && rarity < Names.Length ? Names[rarity] : $"Rarity {rarity}";
        }
    }
}
