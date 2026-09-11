using System;
using UnityEngine;

namespace BetterBonk.BepInEx
{
    // Under BepInEx IL2CPP, BasePlugin is a plain object, not a Unity component, so it never
    // receives Update()/OnGUI() on its own. This tiny MonoBehaviour is registered with the
    // IL2CPP interop layer and added to a scene object purely to forward those two callbacks
    // into the shared ModCore — it contains no logic of its own.
    public sealed class BetterBonkBehaviour : MonoBehaviour
    {
        // Required by Il2CppInterop for any type it registers as a native Unity component.
        public BetterBonkBehaviour(IntPtr ptr) : base(ptr)
        {
        }

        private void Update()
        {
            BepInExEntry.Core?.OnUpdate();
        }

        private void OnGUI()
        {
            BepInExEntry.Core?.OnGUI();
        }
    }
}
