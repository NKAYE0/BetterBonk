using System.Collections.Generic;
using UnityEngine;

namespace FastResetUpdated.Shared
{
    // The in-game settings window, split out from ModCore.cs purely for readability.
    // Everything here reads/writes ModCore.Config directly and calls SaveConfig() explicitly
    // via the Save button — nothing here auto-saves on every keystroke, so half-typed numbers
    // never get written to disk.
    //
    // Deliberately built with raw GUI.* absolute-Rect calls, not GUILayout's automatic layout
    // system, and with no GUI.TextField anywhere: confirmed via Latest.log that both
    // GUILayout.Space and GUI.TextField (via GUI.DoTextField/TextEditor) throw
    // System.NotSupportedException("Method unstripping failed") on this game's build — its
    // IL2CPP build stripped those methods' managed bodies since Megabonk's own code never
    // calls them (only the specific members Megabonk itself uses, like Toggle/Button/Label,
    // survived). Numeric fields are therefore +/- steppers (GUI.Button, already proven safe)
    // rather than typed text fields, and the item search box (see ModItemPicker.cs) reads
    // keystrokes via the same Win32 polling already used for hotkeys, instead of any built-in
    // Unity text-editing widget.
    public sealed partial class ModCore
    {
        private enum RebindTarget
        {
            None,
            ToggleModKey,
            ToggleMenuKey
        }

        private const float Margin = 14f;
        private const float TitleHeight = 26f;
        private const float RowHeight = 24f;
        private const float RowSpacing = 6f;
        private const float SectionGap = 14f;
        private const float LabelColumnWidth = 230f;
        private const float StepperButtonWidth = 26f;
        private const float RebindButtonWidth = 100f;
        private const float GripSize = 16f;
        private const float MinWindowWidth = 360f;
        private const float MinWindowHeight = 260f;

        private bool _menuOpen;
        private Rect _windowRect = new Rect(40, 20, 480, 480);
        private bool _dragging;
        private bool _resizing;
        private float _rowCursorY;
        private RebindTarget _rebinding = RebindTarget.None;
        private Dictionary<string, bool> _rebindKeyWasDown;
        private bool _showAdvanced;

        private GUIStyle _indicatorOnStyle;
        private GUIStyle _indicatorOffStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _valueStyle;
        private static Texture2D _backgroundTexture;
        private static Texture2D _gripTexture;

        public bool MenuOpen => _menuOpen;

        public void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            _rebinding = RebindTarget.None;
            _rebindKeyWasDown = null;
        }

        // Call once per IMGUI pass (i.e. from the loader's OnGUI callback).
        public void OnGUI()
        {
            DrawStatusIndicator();
            DrawItemPickerWindow();

            if (!_menuOpen)
                return;

            // GUI.WindowFunction isn't a real .NET delegate under IL2CPP interop, so a method
            // group can't convert to it directly — it has to become a real System.Action<int>
            // first. Title is "" deliberately: this build's default GUI skin has no window
            // background texture at all, so DrawWindow paints its own background/title instead
            // of relying on GUI.Window's built-in chrome. The return value is intentionally
            // discarded — window position and size are owned entirely by our own drag/resize
            // handling inside DrawWindow (see HandleWindowDragAndResize), not by GUI.Window.
            System.Action<int> drawWindow = DrawWindow;
            GUI.Window(GetHashCode(), _windowRect, drawWindow, string.Empty);
        }

        // Small "Fast Reset: ON/OFF" label, drawn regardless of whether the settings window is
        // open, so the toggle hotkey has some feedback beyond a log line the player likely
        // never looks at. GUIStyle can only be constructed once GUI.skin exists, i.e. from
        // inside an OnGUI call, so these are built lazily on first use rather than at field
        // initialization.
        private void DrawStatusIndicator()
        {
            if (!Config.ShowStatusIndicator)
                return;

            if (_indicatorOnStyle == null)
            {
                _indicatorOnStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _indicatorOnStyle.normal.textColor = Color.green;

                _indicatorOffStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _indicatorOffStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
            }

            bool enabled = Config.ModEnabled;
            GUI.Label(new Rect(12, 12, 220, 24), enabled ? "Fast Reset: ON" : "Fast Reset: OFF",
                enabled ? _indicatorOnStyle : _indicatorOffStyle);
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
                return;

            _titleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _titleStyle.normal.textColor = Color.white;

            _sectionStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _sectionStyle.normal.textColor = new Color(0.8f, 0.85f, 1f);

            _valueStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter };
        }

        private static Texture2D GetBackgroundTexture()
        {
            if (_backgroundTexture == null)
            {
                _backgroundTexture = new Texture2D(1, 1);
                _backgroundTexture.SetPixel(0, 0, new Color(0.05f, 0.05f, 0.07f, 0.97f));
                _backgroundTexture.Apply();
            }
            return _backgroundTexture;
        }

        private static Texture2D GetGripTexture()
        {
            if (_gripTexture == null)
            {
                _gripTexture = new Texture2D(1, 1);
                _gripTexture.SetPixel(0, 0, new Color(0.6f, 0.6f, 0.65f, 0.9f));
                _gripTexture.Apply();
            }
            return _gripTexture;
        }

        private void DrawWindow(int windowId)
        {
            HandleRebindCapture();
            EnsureStyles();

            Rect titleBarRect = new Rect(0, 0, _windowRect.width, Margin + TitleHeight);
            Rect gripRect = new Rect(_windowRect.width - GripSize, _windowRect.height - GripSize, GripSize, GripSize);
            HandleWindowDragAndResize(titleBarRect, gripRect);

            GUI.DrawTexture(new Rect(0, 0, _windowRect.width, _windowRect.height), GetBackgroundTexture());

            float contentWidth = _windowRect.width - Margin * 2;
            _rowCursorY = Margin;

            GUI.Label(new Rect(Margin, _rowCursorY, contentWidth, TitleHeight), "FastReset+", _titleStyle);
            _rowCursorY += TitleHeight;

            Config.ModEnabled = GUI.Toggle(NextRow(contentWidth), Config.ModEnabled,
                $"Mod enabled  (toggle key: {Config.ToggleModKey})");
            Config.ShowStatusIndicator = GUI.Toggle(NextRow(contentWidth), Config.ShowStatusIndicator,
                "Show on-screen ON/OFF indicator");
            DrawRebindRow(NextRow(contentWidth), "Toggle mod key", Config.ToggleModKey, RebindTarget.ToggleModKey);
            DrawRebindRow(NextRow(contentWidth), "Open menu key", Config.ToggleMenuKey, RebindTarget.ToggleMenuKey);

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(contentWidth), "Requisites  (hold Shift for a bigger step)", _sectionStyle);
            Config.MinCombinedShadyAndMoai = IntStepper(NextRow(contentWidth), "Min combined Shady Guys + Moai",
                Config.MinCombinedShadyAndMoai, 1, 5, 0, 999);
            Config.MinLegendaryShadyCount = IntStepper(NextRow(contentWidth), "Min Legendary Shady Guys",
                Config.MinLegendaryShadyCount, 1, 3, 0, 999);
            Config.MinMicrowaveCount = IntStepper(NextRow(contentWidth), "Min Microwaves",
                Config.MinMicrowaveCount, 1, 3, 0, 999);
            Config.MaxAcceptableMicrowaveRarity = IntStepper(NextRow(contentWidth),
                $"Max Microwave rarity ({RarityLabels.NameFor(Config.MaxAcceptableMicrowaveRarity)})",
                Config.MaxAcceptableMicrowaveRarity, 1, 1, 0, RarityLabels.Names.Length - 1);

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(contentWidth), "Legendary Surge", _sectionStyle);
            Config.EnableLegendarySurge = GUI.Toggle(NextRow(contentWidth), Config.EnableLegendarySurge,
                "Enough Legendary Shady Guys relaxes the requisites above");
            if (Config.EnableLegendarySurge)
            {
                Config.LegendarySurgeThreshold = IntStepper(NextRow(contentWidth), "Legendary Shady Guys needed",
                    Config.LegendarySurgeThreshold, 1, 3, 1, 999);
                Config.LegendarySurgeCombinedReduction = IntStepper(NextRow(contentWidth), "Reduce combined requirement by",
                    Config.LegendarySurgeCombinedReduction, 1, 3, 0, 999);
                Config.LegendarySurgeMicrowaveReduction = IntStepper(NextRow(contentWidth), "Reduce microwave requirement by",
                    Config.LegendarySurgeMicrowaveReduction, 1, 2, 0, 999);
            }

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(contentWidth), "Required Item", _sectionStyle);
            Config.RequireSpecificLegendaryItem = GUI.Toggle(NextRow(contentWidth), Config.RequireSpecificLegendaryItem,
                "Require a specific Legendary item from a Shady Guy");
            if (Config.RequireSpecificLegendaryItem)
            {
                Rect itemRow = NextRow(contentWidth);
                Rect labelRect = new Rect(itemRow.x, itemRow.y, itemRow.width - RebindButtonWidth - 8, itemRow.height);
                Rect buttonRect = new Rect(itemRow.xMax - RebindButtonWidth, itemRow.y, RebindButtonWidth, itemRow.height);
                string itemLabel = string.IsNullOrEmpty(Config.RequiredLegendaryItemName)
                    ? "Item: (none selected)"
                    : $"Item: {Config.RequiredLegendaryItemName}";
                GUI.Label(labelRect, itemLabel);
                if (GUI.Button(buttonRect, "Choose..."))
                    OpenItemPicker();
            }

            _rowCursorY += SectionGap;
            _showAdvanced = GUI.Toggle(NextRow(contentWidth), _showAdvanced, "Advanced");
            if (_showAdvanced)
            {
                Config.LegendaryRarityValue = IntStepper(NextRow(contentWidth), "Rarity value counted as \"Legendary\"",
                    Config.LegendaryRarityValue, 1, 1, 0, RarityLabels.Names.Length - 1);
                Config.CheckWindowStartSeconds = FloatStepper(NextRow(contentWidth), "Check window start (s)",
                    Config.CheckWindowStartSeconds, 0.1f, 0.5f, 0f, 60f);
                Config.CheckWindowEndSeconds = FloatStepper(NextRow(contentWidth), "Check window end (s)",
                    Config.CheckWindowEndSeconds, 0.1f, 0.5f, 0f, 60f);
            }

            _rowCursorY += SectionGap;
            Rect buttonsRow = NextRow(contentWidth);
            float buttonWidth = (buttonsRow.width - 16) / 3f;
            Rect saveRect = new Rect(buttonsRow.x, buttonsRow.y, buttonWidth, buttonsRow.height);
            Rect resetRect = new Rect(saveRect.xMax + 8, buttonsRow.y, buttonWidth, buttonsRow.height);
            Rect closeRect = new Rect(resetRect.xMax + 8, buttonsRow.y, buttonWidth, buttonsRow.height);
            if (GUI.Button(saveRect, "Save"))
                SaveConfig();
            if (GUI.Button(resetRect, "Reset to Defaults"))
                ResetConfigToDefaults();
            if (GUI.Button(closeRect, "Close"))
                _menuOpen = false;

            GUI.DrawTexture(gripRect, GetGripTexture());
        }

        // Moving and resizing are both handled by hand (mutating _windowRect directly) rather
        // than via GUI.Window's built-in title-bar dragging, since we never verified that path
        // works on this build and IMGUI has no built-in resize at all. Event.current gives
        // window-local mouse coordinates and per-event deltas here, which is the same
        // mechanism GUI.Button/Toggle already rely on for their own hit-testing (confirmed
        // working — clicks register correctly), so this reuses an already-proven pathway
        // rather than introducing an entirely new one.
        private void HandleWindowDragAndResize(Rect titleBarRect, Rect gripRect)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown)
            {
                if (gripRect.Contains(e.mousePosition))
                {
                    _resizing = true;
                    e.Use();
                }
                else if (titleBarRect.Contains(e.mousePosition))
                {
                    _dragging = true;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_resizing)
                {
                    _windowRect.width = Mathf.Max(MinWindowWidth, _windowRect.width + e.delta.x);
                    _windowRect.height = Mathf.Max(MinWindowHeight, _windowRect.height + e.delta.y);
                    e.Use();
                }
                else if (_dragging)
                {
                    _windowRect.x += e.delta.x;
                    _windowRect.y += e.delta.y;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _dragging = false;
                _resizing = false;
            }
        }

        private Rect NextRow(float width)
        {
            Rect row = new Rect(Margin, _rowCursorY, width, RowHeight);
            _rowCursorY += RowHeight + RowSpacing;
            return row;
        }

        private void DrawRebindRow(Rect row, string label, string currentKey, RebindTarget target)
        {
            Rect labelRect = new Rect(row.x, row.y, row.width - RebindButtonWidth - 8, row.height);
            Rect buttonRect = new Rect(row.xMax - RebindButtonWidth, row.y, RebindButtonWidth, row.height);

            GUI.Label(labelRect, $"{label}: {currentKey}");
            string buttonLabel = _rebinding == target ? "Press a key..." : "Rebind";
            if (GUI.Button(buttonRect, buttonLabel))
                _rebinding = target;
        }

        // While a rebind is pending, the next supported key to transition from up to down is
        // captured as the new binding. Uses the same Win32-based polling as the toggle
        // hotkeys (see Win32Input.cs) rather than Unity's Event/Input system, for the same
        // reason: Megabonk's Rewired-based input handling appears to swallow Unity's own key
        // detection entirely for gameplay hotkeys, and there's no reason to assume the same
        // Win32 polling isn't simplest/most consistent choice here too.
        private void HandleRebindCapture()
        {
            if (_rebinding == RebindTarget.None)
                return;

            if (_rebindKeyWasDown == null)
            {
                // First frame of a rebind just records which keys are already held (e.g. the
                // "Rebind" button click itself shouldn't be mistaken for the new key), rather
                // than immediately capturing whatever happens to be down.
                _rebindKeyWasDown = new Dictionary<string, bool>();
                foreach (string name in Win32Input.SupportedKeyNames)
                    _rebindKeyWasDown[name] = IsKeyDown(name);
                return;
            }

            foreach (string name in Win32Input.SupportedKeyNames)
            {
                bool isDown = IsKeyDown(name);
                bool wasDown = _rebindKeyWasDown.TryGetValue(name, out bool prev) && prev;
                _rebindKeyWasDown[name] = isDown;

                if (!isDown || wasDown)
                    continue;

                if (_rebinding == RebindTarget.ToggleModKey)
                    Config.ToggleModKey = name;
                else if (_rebinding == RebindTarget.ToggleMenuKey)
                    Config.ToggleMenuKey = name;

                _rebinding = RebindTarget.None;
                _rebindKeyWasDown = null;
                return;
            }
        }

        private static bool IsKeyDown(string keyName)
        {
            return Win32Input.TryGetVirtualKey(keyName, out int vk) && Win32Input.IsKeyDown(vk);
        }

        // +/- stepper in place of a text field — see the file-level comment for why: this
        // build's GUI.TextField is confirmed non-functional. Holding Shift takes the larger of
        // the two given steps.
        private int IntStepper(Rect row, string label, int value, int smallStep, int largeStep, int min, int max)
        {
            Rect labelRect = new Rect(row.x, row.y, LabelColumnWidth, row.height);
            float stepperWidth = row.width - LabelColumnWidth;
            float valueWidth = stepperWidth - StepperButtonWidth * 2 - 8;

            Rect minusRect = new Rect(row.xMax - stepperWidth, row.y, StepperButtonWidth, row.height);
            Rect valueRect = new Rect(minusRect.xMax + 4, row.y, valueWidth, row.height);
            Rect plusRect = new Rect(valueRect.xMax + 4, row.y, StepperButtonWidth, row.height);

            GUI.Label(labelRect, label);
            GUI.Label(valueRect, value.ToString(), _valueStyle);

            int step = Win32Input.IsShiftDown() ? largeStep : smallStep;
            if (GUI.Button(minusRect, "-"))
                value = Mathf.Clamp(value - step, min, max);
            if (GUI.Button(plusRect, "+"))
                value = Mathf.Clamp(value + step, min, max);
            return value;
        }

        private float FloatStepper(Rect row, string label, float value, float smallStep, float largeStep, float min, float max)
        {
            Rect labelRect = new Rect(row.x, row.y, LabelColumnWidth, row.height);
            float stepperWidth = row.width - LabelColumnWidth;
            float valueWidth = stepperWidth - StepperButtonWidth * 2 - 8;

            Rect minusRect = new Rect(row.xMax - stepperWidth, row.y, StepperButtonWidth, row.height);
            Rect valueRect = new Rect(minusRect.xMax + 4, row.y, valueWidth, row.height);
            Rect plusRect = new Rect(valueRect.xMax + 4, row.y, StepperButtonWidth, row.height);

            GUI.Label(labelRect, label);
            GUI.Label(valueRect, value.ToString("0.0#"), _valueStyle);

            float step = Win32Input.IsShiftDown() ? largeStep : smallStep;
            if (GUI.Button(minusRect, "-"))
                value = Mathf.Clamp(value - step, min, max);
            if (GUI.Button(plusRect, "+"))
                value = Mathf.Clamp(value + step, min, max);
            return value;
        }
    }
}
