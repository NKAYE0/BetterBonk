using System;
using System.IO;
using FastResetUpdated.Shared;
using MelonLoader;

// MelonGame(null, null) matches the original Fast Reset mod exactly (verified by decompiling
// it) — i.e. it doesn't restrict itself to a specific game internally, so this keeps the same
// "no restriction" behaviour rather than guessing at Megabonk's internal MelonGame identifier.
[assembly: MelonInfo(typeof(FastResetUpdated.MelonLoader.MelonEntry), "Fast Reset Updated", "2.0.0", "NK")]
[assembly: MelonGame(null, null)]

namespace FastResetUpdated.MelonLoader
{
    // MelonLoader entry point. This is the priority build per the project brief — keep this
    // one working first if a future game update breaks something.
    public sealed class MelonEntry : MelonMod
    {
        private ModCore _core;

        public override void OnInitializeMelon()
        {
            IModLogger logger = new MelonLoaderLogger(LoggerInstance);

            // Deliberately not using MelonLoader's own UserDataDirectory-style helper here:
            // its exact name/namespace has moved between MelonLoader versions and I couldn't
            // verify which one this install actually has. AppDomain.CurrentDomain.BaseDirectory
            // is the game's own executable folder regardless of MelonLoader version or how it
            // loaded this assembly, so it needs no loader-specific API at all.
            string gameDir = AppDomain.CurrentDomain.BaseDirectory;
            string configDir = Path.Combine(gameDir, "UserData", "FastResetUpdated");
            ConfigStore configStore = new ConfigStore(configDir, logger);

            _core = new ModCore(logger, configStore);
            LoggerInstance.Msg("Fast Reset Updated loaded!");
        }

        public override void OnUpdate()
        {
            _core?.OnUpdate();
        }

        public override void OnGUI()
        {
            _core?.OnGUI();
        }
    }
}
