using System.IO;
using BepInEx;
using BepInEx.Logging;
// NOTE: verified against the general BepInEx 6 IL2CPP plugin pattern, but not against your
// specific installed version (I only had the original mod's MelonLoader-side binary to check
// against, not a BepInEx one). If this namespace doesn't resolve, check whatever
// "BepInEx.Unity.IL2CPP.dll" you actually have under BepInExDir\core and adjust this using
// (and the BasePlugin base class below) to match — everything else in this project is loader-
// agnostic and won't need to change.
using BepInEx.Unity.IL2CPP;
using BetterBonk.Shared;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BetterBonk.BepInEx
{
    [BepInPlugin("nk.betterbonk", "BetterBonk", "3.0.0")]
    public sealed class BepInExEntry : BasePlugin
    {
        internal static ModCore Core { get; private set; }

        public override void Load()
        {
            IModLogger logger = new BepInExLogger(Log);
            string configDir = Path.Combine(Paths.ConfigPath, "BetterBonk");
            ConfigStore configStore = new ConfigStore(configDir, logger);

            Core = new ModCore(logger, configStore);

            // Register and attach the small forwarding behaviour class below (BetterBonkBehaviour,
            // still in a file named FastResetBehaviour.cs from before the rename — the filename
            // doesn't need to match the class name in C#, and leaving it saves churning a file
            // that would otherwise just be a byte-for-byte rename) so ModCore actually gets its
            // per-frame callbacks.
            ClassInjector.RegisterTypeInIl2Cpp<BetterBonkBehaviour>();
            GameObject carrier = new GameObject("BetterBonk");
            UnityEngine.Object.DontDestroyOnLoad(carrier);
            carrier.AddComponent<BetterBonkBehaviour>();

            Log.LogInfo("BetterBonk loaded!");
        }
    }
}
