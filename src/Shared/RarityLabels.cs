namespace FastResetUpdated.Shared
{
    // Human-readable labels for the rarity integers used throughout the menu. These are display
    // strings only — every actual comparison in ModCore uses the plain integers from FilterConfig,
    // so if a game update turns out to use different rarity tiers than assumed here, only the
    // labels shown to the player would need updating.
    //
    // Confirmed against the game's actual EItemRarity enum (decompiled from Assembly-CSharp.dll):
    // Common=0, Rare=1, Epic=2, Legendary=3, Corrupted=4, Quest=5. (An earlier version of this
    // file guessed "Uncommon" instead of "Rare"/"Epic" before that enum had been checked — this
    // is the corrected, verified list.)
    public static class RarityLabels
    {
        public static readonly string[] Names = { "Common", "Rare", "Epic", "Legendary", "Corrupted", "Quest" };

        public static string NameFor(int rarity)
        {
            return rarity >= 0 && rarity < Names.Length ? Names[rarity] : $"Rarity {rarity}";
        }
    }
}
