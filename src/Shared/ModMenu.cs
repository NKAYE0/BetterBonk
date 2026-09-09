using UnityEngine;

namespace FastResetUpdated.Shared
{
    // The in-game settings window (IMGUI), split out from ModCore.cs purely for readability.
    // Everything here reads/writes ModCore.Config directly and calls SaveConfig() explicitly
    // via the Save button — nothing here auto-saves on every keystroke, so half-typed numbers
    // never get written to disk.
    public sealed partial class ModCore
    {
        private enum RebindTarget
        {
            None,
            ToggleModKey,
            ToggleMenuKey
        }

        private bool _menuOpen;
        private Rect _windowRect = new Rect(40, 40, 400, 10);
        private RebindTarget _rebinding = RebindTarget.None;
        private bool _showAdvanced;

        public bool MenuOpen => _menuOpen;

        public void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            _rebinding = RebindTarget.None;
        }

        // Call once per IMGUI pass (i.e. from the loader's OnGUI callback).
        public void OnGUI()
        {
            if (!_menuOpen)
                return;

            // GUI.WindowFunction isn't a real .NET delegate under IL2CPP interop (it extends
            // Il2CppSystem.MulticastDelegate, not System.MulticastDelegate), so `new
            // GUI.WindowFunction(DrawWindow)` doesn't compile. UnityEngine.IMGUIModule.dll does
            // have `implicit operator WindowFunction(System.Action<int>)`, but a method-group
            // conversion can't be combined with a user-defined conversion in a single implicit
            // step (they're different conversion categories in the C# spec) — so the method
            // group has to become a real System.Action<int> as its own step first, and only
            // *that* implicitly converts to WindowFunction.
            System.Action<int> drawWindow = DrawWindow;
            _windowRect = GUILayout.Window(GetHashCode(), _windowRect, drawWindow, "Fast Reset Updated");
        }

        private void DrawWindow(int windowId)
        {
            HandleRebindCapture();

            GUILayout.BeginVertical();

            Config.ModEnabled = GUILayout.Toggle(Config.ModEnabled, $"Mod enabled  (toggle key: {Config.ToggleModKey})");
            DrawRebindRow("Toggle mod key", Config.ToggleModKey, RebindTarget.ToggleModKey);
            DrawRebindRow("Open menu key", Config.ToggleMenuKey, RebindTarget.ToggleMenuKey);

            GUILayout.Space(8);
            GUILayout.Label("Requisites", GUI.skin.box);
            Config.MinCombinedShadyAndMoai = IntField("Min combined Shady Guys + Moai", Config.MinCombinedShadyAndMoai);
            Config.MinLegendaryShadyCount = IntField("Min Legendary Shady Guys", Config.MinLegendaryShadyCount);
            Config.MinMicrowaveCount = IntField("Min Microwaves", Config.MinMicrowaveCount);
            Config.MaxAcceptableMicrowaveRarity = IntField(
                $"Max Microwave rarity ({RarityLabels.NameFor(Config.MaxAcceptableMicrowaveRarity)})",
                Config.MaxAcceptableMicrowaveRarity);

            GUILayout.Space(8);
            GUILayout.Label("Legendary Surge", GUI.skin.box);
            Config.EnableLegendarySurge = GUILayout.Toggle(
                Config.EnableLegendarySurge,
                "Enough Legendary Shady Guys relaxes the requisites above");
            if (Config.EnableLegendarySurge)
            {
                Config.LegendarySurgeThreshold = IntField("Legendary Shady Guys needed", Config.LegendarySurgeThreshold);
                Config.LegendarySurgeCombinedReduction = IntField("Reduce combined requirement by", Config.LegendarySurgeCombinedReduction);
                Config.LegendarySurgeMicrowaveReduction = IntField("Reduce microwave requirement by", Config.LegendarySurgeMicrowaveReduction);
            }

            GUILayout.Space(8);
            _showAdvanced = GUILayout.Toggle(_showAdvanced, "Advanced");
            if (_showAdvanced)
            {
                Config.LegendaryRarityValue = IntField("Rarity value counted as \"Legendary\"", Config.LegendaryRarityValue);
                Config.CheckWindowStartSeconds = FloatField("Check window start (s)", Config.CheckWindowStartSeconds);
                Config.CheckWindowEndSeconds = FloatField("Check window end (s)", Config.CheckWindowEndSeconds);
            }

            GUILayout.Space(10);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Save"))
                SaveConfig();
            if (GUILayout.Button("Reset to Defaults"))
                ResetConfigToDefaults();
            if (GUILayout.Button("Close"))
                _menuOpen = false;
            GUILayout.EndHorizontal();

            GUILayout.EndVertical();
            GUI.DragWindow();
        }

        private void DrawRebindRow(string label, string currentKey, RebindTarget target)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label($"{label}: {currentKey}", GUILayout.Width(240));
            string buttonLabel = _rebinding == target ? "Press a key..." : "Rebind";
            if (GUILayout.Button(buttonLabel, GUILayout.Width(110)))
                _rebinding = target;
            GUILayout.EndHorizontal();
        }

        // While a rebind is pending, the very next key-down event this window receives is
        // captured as the new binding instead of being treated as normal input.
        private void HandleRebindCapture()
        {
            if (_rebinding == RebindTarget.None)
                return;

            Event current = Event.current;
            if (current == null || current.type != EventType.KeyDown)
                return;

            string keyName = current.keyCode.ToString();
            if (_rebinding == RebindTarget.ToggleModKey)
                Config.ToggleModKey = keyName;
            else if (_rebinding == RebindTarget.ToggleMenuKey)
                Config.ToggleMenuKey = keyName;

            _rebinding = RebindTarget.None;
            current.Use();
        }

        private static int IntField(string label, int value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(280));
            string text = GUILayout.TextField(value.ToString(), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return int.TryParse(text, out int parsed) ? parsed : value;
        }

        private static float FloatField(string label, float value)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, GUILayout.Width(280));
            string text = GUILayout.TextField(value.ToString("0.0#"), GUILayout.Width(50));
            GUILayout.EndHorizontal();
            return float.TryParse(text, out float parsed) ? parsed : value;
        }
    }
}
