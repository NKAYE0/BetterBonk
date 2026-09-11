using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterBonk.Shared
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
    // Unity text-editing widget. Moving/resizing and the scrollbar all use Event.current mouse
    // handling (the same mechanism GUI.Button/Toggle already rely on for their own hit-testing)
    // rather than GUI.Window's built-in title-bar dragging or GUI.BeginScrollView, since those
    // were never independently verified on this build and this reuses an already-proven path.
    public sealed partial class ModCore
    {
        private enum RebindTarget
        {
            None,
            ToggleModKey,
            ToggleMenuKey
        }

        // BetterBonk's menu pages — Quick Reset is first/default per the brief, the other three
        // are the mod's other features, one tab each. Adding a feature means adding one entry
        // here plus a Draw*Tab/Compute*ContentHeight pair below; nothing else in this file needs
        // to change.
        private enum MenuTab
        {
            QuickReset,
            PotBreaking,
            Leaderboard,
            ToggleEverything
        }

        private static readonly (MenuTab Tab, string Label)[] TabDefinitions =
        {
            (MenuTab.QuickReset, "Quick Reset"),
            (MenuTab.PotBreaking, "Pot Breaking"),
            (MenuTab.Leaderboard, "Leaderboard"),
            (MenuTab.ToggleEverything, "Toggle Everything")
        };

        private MenuTab _activeTab = MenuTab.QuickReset;

        private const float Margin = 14f;
        private const float TitleHeight = 26f;
        private const float TitleTextWidth = 140f;
        private const float RowHeight = 24f;
        private const float RowSpacing = 6f;
        private const float SectionGap = 14f;
        private const float LabelColumnWidth = 230f;
        private const float StepperButtonWidth = 26f;
        private const float RebindButtonWidth = 100f;
        private const float GripSize = 13f;
        private const float ScrollbarWidth = 10f;
        // Wide enough for the title plus all four tab buttons without crowding — the old 360px
        // minimum predates the tab bar and left too little room for it.
        private const float MinWindowWidth = 560f;
        private const float MinWindowHeight = 220f;

        private bool _menuOpen;
        // Placeholder until the first time the menu is opened — see EnsureDefaultWindowSize,
        // which resizes this to fill most of the screen at that point instead of leaving it
        // this small. Kept as a real, sane Rect here (rather than default/zero) purely so the
        // window is never briefly drawn at a degenerate size if something ever draws before
        // that runs.
        private Rect _windowRect = new Rect(40, 20, 480, 420);
        private bool _windowSizeInitialized;
        private bool _dragging;
        private bool _resizing;
        private float _rowCursorY;
        private float _scrollOffset;
        private bool _scrollbarDragging;
        private RebindTarget _rebinding = RebindTarget.None;
        private Dictionary<string, bool> _rebindKeyWasDown;

        private GUIStyle _indicatorOnStyle;
        private GUIStyle _indicatorOffStyle;
        private GUIStyle _scoreStyle;
        private GUIStyle _stepperLabelStyle;
        private GUIStyle _titleStyle;
        private GUIStyle _sectionStyle;
        private GUIStyle _valueStyle;
        private GUIStyle _activeTabStyle;
        private GUIStyle _inactiveTabStyle;
        private static Texture2D _solidTexture;

        public bool MenuOpen => _menuOpen;

        public void ToggleMenu()
        {
            _menuOpen = !_menuOpen;
            if (_menuOpen)
                EnsureDefaultWindowSize();
            _rebinding = RebindTarget.None;
            _rebindKeyWasDown = null;
        }

        // Sizes the window to fill most of the screen the first time it's ever opened, so the
        // player isn't stuck manually resizing/scrolling through a small default just to see
        // the whole Presets section at once. Only runs once per mod session (per _windowSizeInitialized) —
        // resizing or moving the window afterwards is respected for as long as it stays open,
        // exactly as before; this only changes what the very first size happens to be. Computed
        // here (when the menu is actually being opened) rather than in the field initializer
        // above, since Screen.width/height reflect the game's actual current resolution and
        // this runs well after the game window exists, whereas a field initializer runs during
        // construction, before that's guaranteed.
        private void EnsureDefaultWindowSize()
        {
            if (_windowSizeInitialized)
                return;
            _windowSizeInitialized = true;

            const float margin = 40f;
            _windowRect.width = Mathf.Max(MinWindowWidth, Screen.width - margin * 2);
            _windowRect.height = Mathf.Max(MinWindowHeight, Screen.height - margin * 2);
            _windowRect.x = margin;
            _windowRect.y = margin;
        }

        // Call once per IMGUI pass (i.e. from the loader's OnGUI callback).
        public void OnGUI()
        {
            DrawStatusIndicator();
            DrawItemPickerWindow();
            DrawPresetPickerWindow();
            DrawToggleEverythingPresetPickerWindow();

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

        // Small "Quick Reset: ON/OFF" label, drawn regardless of whether the settings window is
        // open, so the toggle hotkey has some feedback beyond a log line the player likely
        // never looks at. GUIStyle can only be constructed once GUI.skin exists, i.e. from
        // inside an OnGUI call, so these are built lazily on first use rather than at field
        // initialization.
        private void DrawStatusIndicator()
        {
            if (_indicatorOnStyle == null)
            {
                // Built off GUI.skin.label for its font/size, but every background is explicitly
                // cleared: if the game's currently-active GUI skin has any box/background image
                // assigned to its label style, GUIStyle's copy constructor would otherwise carry
                // it straight into ours, which is what was showing as a solid bar behind the text.
                _indicatorOnStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _indicatorOnStyle.normal.textColor = Color.green;
                ClearBackgrounds(_indicatorOnStyle);

                _indicatorOffStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                _indicatorOffStyle.normal.textColor = new Color(1f, 0.35f, 0.35f);
                ClearBackgrounds(_indicatorOffStyle);

                _scoreStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
                ClearBackgrounds(_scoreStyle);
            }

            if (Config.ShowStatusIndicator)
            {
                bool enabled = Config.ModEnabled;
                GUI.Label(new Rect(12, 12, 220, 24), enabled ? "Quick Reset: ON" : "Quick Reset: OFF",
                    enabled ? _indicatorOnStyle : _indicatorOffStyle);
            }

            // Independent toggle from ShowStatusIndicator above — sits right under it when both
            // are on, or takes its spot at the top when the ON/OFF indicator itself is hidden.
            if (Config.ShowMapScore)
            {
                float y = Config.ShowStatusIndicator ? 36 : 12;
                string text = _currentMapScore < 0 ? "Map Score: --" : $"Map Score: {_currentMapScore}";
                _scoreStyle.normal.textColor = _currentMapScore < 0
                    ? new Color(0.75f, 0.75f, 0.75f)
                    : MapScoreColor(_currentMapScore);
                GUI.Label(new Rect(12, y, 220, 24), text, _scoreStyle);
            }
        }

        // 0-50% red, 50-65% orange, 65-80% yellow, 80%+ green — matches the bands requested for
        // the map-score display.
        private static Color MapScoreColor(int score)
        {
            if (score < 50) return new Color(1f, 0.3f, 0.3f);
            if (score < 65) return new Color(1f, 0.6f, 0.15f);
            if (score < 80) return new Color(0.95f, 0.9f, 0.25f);
            return new Color(0.35f, 0.9f, 0.35f);
        }

        private static void ClearBackgrounds(GUIStyle style)
        {
            style.normal.background = null;
            style.hover.background = null;
            style.active.background = null;
            style.focused.background = null;
            style.onNormal.background = null;
            style.onHover.background = null;
            style.onActive.background = null;
            style.onFocused.background = null;
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

            _activeTabStyle = new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, clipping = TextClipping.Clip };
            _activeTabStyle.normal.textColor = Color.white;

            _inactiveTabStyle = new GUIStyle(GUI.skin.button) { clipping = TextClipping.Clip };
            _inactiveTabStyle.normal.textColor = new Color(0.75f, 0.75f, 0.78f);
        }

        // Drawn to the right of the title, one button per MenuTab. Switching tabs resets scroll
        // so a page never opens scrolled to wherever a different, possibly much longer, page
        // happened to be left.
        private void DrawTabBar(Rect rect)
        {
            const float gap = 4f;
            float tabWidth = (rect.width - gap * (TabDefinitions.Length - 1)) / TabDefinitions.Length;

            for (int i = 0; i < TabDefinitions.Length; i++)
            {
                (MenuTab tab, string label) = TabDefinitions[i];
                Rect tabRect = new Rect(rect.x + i * (tabWidth + gap), rect.y, tabWidth, rect.height);
                bool isActive = _activeTab == tab;
                if (GUI.Button(tabRect, label, isActive ? _activeTabStyle : _inactiveTabStyle) && !isActive)
                {
                    _activeTab = tab;
                    _scrollOffset = 0f;
                }
            }
        }

        // A single reusable white 1x1 texture, tinted per draw via GUI.color — used for every
        // solid-fill shape in both windows (background, borders, scrollbar, resize grip) rather
        // than allocating a separate texture per color.
        private static Texture2D GetSolidTexture()
        {
            if (_solidTexture == null)
            {
                _solidTexture = new Texture2D(1, 1);
                _solidTexture.SetPixel(0, 0, Color.white);
                _solidTexture.Apply();
            }
            return _solidTexture;
        }

        private static void DrawSolidRect(Rect rect, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(rect, GetSolidTexture());
            GUI.color = previous;
        }

        private static readonly Color BackgroundColor = new Color(0.05f, 0.05f, 0.07f, 0.97f);

        private void DrawWindow(int windowId)
        {
            HandleRebindCapture();
            EnsureStyles();

            Rect titleBarRect = new Rect(0, 0, _windowRect.width, Margin + TitleHeight);
            Rect gripRect = new Rect(_windowRect.width - GripSize, _windowRect.height - GripSize, GripSize, GripSize);

            DrawSolidRect(new Rect(0, 0, _windowRect.width, _windowRect.height), BackgroundColor);

            float contentWidth = _windowRect.width - Margin * 2;
            GUI.Label(new Rect(Margin, Margin, TitleTextWidth, TitleHeight), "BetterBonk Menu", _titleStyle);

            Rect tabBarRect = new Rect(Margin + TitleTextWidth, Margin, contentWidth - TitleTextWidth, TitleHeight);
            DrawTabBar(tabBarRect);

            float viewportTop = Margin + TitleHeight;
            float footerHeight = Margin + RowHeight + SectionGap;
            Rect viewport = new Rect(Margin, viewportTop, contentWidth - ScrollbarWidth - 6,
                _windowRect.height - viewportTop - footerHeight);

            float contentHeight = ComputeContentHeight();
            float maxScroll = Mathf.Max(0, contentHeight - viewport.height);
            _scrollOffset = Mathf.Clamp(_scrollOffset, 0, maxScroll);

            HandleScrollWheel(viewport, maxScroll);

            GUI.BeginGroup(viewport);
            _rowCursorY = -_scrollOffset;
            DrawScrollableContent(viewport.width);
            GUI.EndGroup();

            Rect scrollbarTrack = new Rect(viewport.xMax + 6, viewport.y, ScrollbarWidth, viewport.height);
            DrawScrollbar(scrollbarTrack, contentHeight, viewport.height, maxScroll);

            Rect buttonsRow = new Rect(Margin, _windowRect.height - Margin - RowHeight, contentWidth, RowHeight);
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

            // Drag/resize is handled last so the resize grip visually sits on top of everything
            // else, and so a click there isn't also consumed by whatever's underneath it.
            HandleWindowDragAndResize(titleBarRect, gripRect);
            DrawResizeGrip(gripRect);
        }

        // Everything that scrolls: toggles, rebind rows, requisites, Legendary Surge, and the
        // required-item row. Drawn inside a GUI.BeginGroup (see DrawWindow), so all coordinates
        // here are relative to the viewport's top-left, not the window's.
        private void DrawScrollableContent(float width)
        {
            switch (_activeTab)
            {
                case MenuTab.QuickReset:
                    DrawQuickResetTab(width);
                    break;
                case MenuTab.PotBreaking:
                    DrawPotBreakingTab(width);
                    break;
                case MenuTab.Leaderboard:
                    DrawLeaderboardTab(width);
                    break;
                case MenuTab.ToggleEverything:
                    DrawToggleEverythingTab(width);
                    break;
            }
        }

        private void DrawQuickResetTab(float width)
        {
            Config.ModEnabled = GUI.Toggle(NextRow(width), Config.ModEnabled,
                $"Mod enabled  (toggle key: {Config.ToggleModKey})");
            Config.ShowStatusIndicator = GUI.Toggle(NextRow(width), Config.ShowStatusIndicator,
                "Show on-screen ON/OFF indicator");
            Config.ShowMapScore = GUI.Toggle(NextRow(width), Config.ShowMapScore,
                "Show map score (0-100, updates each new run)");
            Config.AcceptableMapScore = IntStepper(NextRow(width), "Acceptable map score",
                Config.AcceptableMapScore, 5, 25, 0, 100);
            DrawRebindRow(NextRow(width), "Toggle mod key", Config.ToggleModKey, RebindTarget.ToggleModKey);
            DrawRebindRow(NextRow(width), "Open menu key", Config.ToggleMenuKey, RebindTarget.ToggleMenuKey);

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(width), "Requisites  (hold Shift for a bigger step)", _sectionStyle);
            Config.UseSeparateShadyAndMoaiCounts = GUI.Toggle(NextRow(width), Config.UseSeparateShadyAndMoaiCounts,
                "Track Shady Guys and Moai separately");
            if (Config.UseSeparateShadyAndMoaiCounts)
            {
                Config.MinShadyGuyCount = IntStepper(NextRow(width), "Min Shady Guys",
                    Config.MinShadyGuyCount, 1, 3, 0, 999);
                Config.MinMoaiCount = IntStepper(NextRow(width), "Min Moai Shrines",
                    Config.MinMoaiCount, 1, 3, 0, 999);
            }
            else
            {
                Config.MinCombinedShadyAndMoai = IntStepper(NextRow(width), "Min combined Shady Guys + Moai",
                    Config.MinCombinedShadyAndMoai, 1, 5, 0, 999);
            }
            Config.MinLegendaryShadyCount = IntStepper(NextRow(width), "Min Legendary Shady Guys",
                Config.MinLegendaryShadyCount, 1, 3, 0, 999);
            Config.MinMicrowaveCount = IntStepper(NextRow(width), "Min Microwaves",
                Config.MinMicrowaveCount, 1, 3, 0, 999);
            Config.MaxAcceptableMicrowaveRarity = IntStepper(NextRow(width),
                $"Max Microwave rarity ({RarityLabels.NameFor(Config.MaxAcceptableMicrowaveRarity)})",
                Config.MaxAcceptableMicrowaveRarity, 1, 1, 0, RarityLabels.Names.Length - 1);
            Config.MinBossCurseCount = IntStepper(NextRow(width), "Min Boss Curses",
                Config.MinBossCurseCount, 1, 3, 0, 999);
            Config.MinLegendaryChargeShrineCount = IntStepper(NextRow(width), "Min Legendary Charge Shrines",
                Config.MinLegendaryChargeShrineCount, 1, 3, 0, 999);

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(width), "Legendary Surge", _sectionStyle);
            Config.EnableLegendarySurge = GUI.Toggle(NextRow(width), Config.EnableLegendarySurge,
                "Enough Legendary Shady Guys relaxes the requisites above");
            if (Config.EnableLegendarySurge)
            {
                Config.LegendarySurgeThreshold = IntStepper(NextRow(width), "Legendary Shady Guys needed",
                    Config.LegendarySurgeThreshold, 1, 3, 1, 999);
                string combinedReductionLabel = Config.UseSeparateShadyAndMoaiCounts
                    ? "Shady/Moai reduction (each)"
                    : "Reduce combined requirement by";
                Config.LegendarySurgeCombinedReduction = IntStepper(NextRow(width), combinedReductionLabel,
                    Config.LegendarySurgeCombinedReduction, 1, 3, 0, 999);
                Config.LegendarySurgeMicrowaveReduction = IntStepper(NextRow(width), "Reduce microwave requirement by",
                    Config.LegendarySurgeMicrowaveReduction, 1, 2, 0, 999);
            }

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(width), "Required Item", _sectionStyle);
            Config.RequireSpecificLegendaryItem = GUI.Toggle(NextRow(width), Config.RequireSpecificLegendaryItem,
                "Require a specific Legendary item from a Shady Guy");
            if (Config.RequireSpecificLegendaryItem)
            {
                Rect itemRow = NextRow(width);
                const float clearWidth = 56f;
                const float buttonGap = 6f;
                Rect labelRect = new Rect(itemRow.x, itemRow.y,
                    itemRow.width - RebindButtonWidth - clearWidth - buttonGap * 2, itemRow.height);
                Rect chooseRect = new Rect(itemRow.xMax - RebindButtonWidth, itemRow.y, RebindButtonWidth, itemRow.height);
                Rect clearRect = new Rect(chooseRect.x - buttonGap - clearWidth, itemRow.y, clearWidth, itemRow.height);
                string itemLabel = string.IsNullOrEmpty(Config.RequiredLegendaryItemName)
                    ? "Item: (none selected)"
                    : $"Item: {Config.RequiredLegendaryItemName}";
                GUI.Label(labelRect, itemLabel, _stepperLabelStyle);
                if (GUI.Button(clearRect, "Clear"))
                {
                    Config.RequiredLegendaryItemName = string.Empty;
                    SaveConfig();
                }
                if (GUI.Button(chooseRect, "Choose..."))
                    OpenItemPicker();
            }

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(width), "Presets", _sectionStyle);

            Rect activeRow = NextRow(width);
            Rect switchRect = new Rect(activeRow.xMax - RebindButtonWidth, activeRow.y, RebindButtonWidth, activeRow.height);
            Rect activeLabelRect = new Rect(activeRow.x, activeRow.y, activeRow.width - RebindButtonWidth - 8, activeRow.height);
            GUI.Label(activeLabelRect, $"Active preset: {_activePresetName}", _stepperLabelStyle);
            if (GUI.Button(switchRect, "Switch..."))
                OpenPresetPicker();

            Rect saveRow = NextRow(width);
            if (PresetStore.IsDefault(_activePresetName))
            {
                if (GUI.Button(saveRow, "Save current settings as new preset..."))
                    OpenPresetPicker(startInSaveAsMode: true);
            }
            else
            {
                float halfWidth = (saveRow.width - 8) / 2f;
                Rect updateRect = new Rect(saveRow.x, saveRow.y, halfWidth, saveRow.height);
                Rect saveAsRect = new Rect(updateRect.xMax + 8, saveRow.y, halfWidth, saveRow.height);
                if (GUI.Button(updateRect, $"Update '{_activePresetName}'"))
                    UpdateActivePreset();
                if (GUI.Button(saveAsRect, "Save As New..."))
                    OpenPresetPicker(startInSaveAsMode: true);
            }
        }

        private float ComputeContentHeight()
        {
            return _activeTab switch
            {
                MenuTab.QuickReset => ComputeQuickResetContentHeight(),
                MenuTab.PotBreaking => ComputePotBreakingContentHeight(),
                MenuTab.Leaderboard => ComputeLeaderboardContentHeight(),
                MenuTab.ToggleEverything => ComputeToggleEverythingContentHeight(),
                _ => 0f
            };
        }

        // Mirrors DrawQuickResetTab's row layout exactly — kept as a separate analytical pass
        // (rather than measuring the real draw call) since IMGUI has no "measure without
        // drawing" primitive; if a row is ever added to one, it must be added to the other.
        private float ComputeQuickResetContentHeight()
        {
            int rows = 6; // mod toggle, indicator toggle, map-score toggle, acceptable-score stepper, 2 rebind rows
            // "Requisites" label + separate-mode toggle + (combined: 1 row, separate: 2 rows) +
            // 5 fixed fields (Legendary Shady, Microwaves, Microwave rarity, Boss Curses, Legendary Charge Shrines)
            rows += 1 + 1 + (Config.UseSeparateShadyAndMoaiCounts ? 2 : 1) + 5;
            rows += 1 + 1; // "Legendary Surge" label + enable toggle
            if (Config.EnableLegendarySurge)
                rows += 3;
            rows += 1 + 1; // "Required Item" label + enable toggle
            if (Config.RequireSpecificLegendaryItem)
                rows += 1;
            rows += 1 + 1 + 1; // "Presets" label + active/switch row + save-buttons row

            float rowsHeight = rows * (RowHeight + RowSpacing);
            float gaps = SectionGap * 4;
            return rowsHeight + gaps;
        }

        private void DrawPotBreakingTab(float width)
        {
            GUI.Label(NextRow(width), "Pot Breaking", _sectionStyle);
            Config.AutoBreakPots = GUI.Toggle(NextRow(width), Config.AutoBreakPots,
                "Automatically break pots (only pots — anything spawned next to one is left alone)");
        }

        private float ComputePotBreakingContentHeight()
        {
            int rows = 2; // section label + toggle
            return rows * (RowHeight + RowSpacing);
        }

        private void DrawLeaderboardTab(float width)
        {
            GUI.Label(NextRow(width), "Personal Leaderboard", _sectionStyle);
            Config.PersonalLeaderboardEnabled = GUI.Toggle(NextRow(width), Config.PersonalLeaderboardEnabled,
                "Add a personal-best tab to the leaderboard screen");
            if (Config.PersonalLeaderboardEnabled)
                DrawCharacterFilterRow(NextRow(width));
        }

        private float ComputeLeaderboardContentHeight()
        {
            int rows = 2; // section label + enable toggle
            if (Config.PersonalLeaderboardEnabled)
                rows += 1;
            return rows * (RowHeight + RowSpacing);
        }

        private void DrawToggleEverythingTab(float width)
        {
            GUI.Label(NextRow(width), "Toggle Everything", _sectionStyle);
            Config.ToggleEverythingEnabled = GUI.Toggle(NextRow(width), Config.ToggleEverythingEnabled,
                "Unlock every achievement-gated item/upgrade and let you toggle each on or off");

            if (!Config.ToggleEverythingEnabled)
                return;

            EnsureToggleEverythingPresetStore();

            _rowCursorY += SectionGap;
            GUI.Label(NextRow(width), "Presets", _sectionStyle);
            GUI.Label(NextRow(width),
                "Saves/loads which achievements are currently toggled off on the game's own achievement screen.",
                _stepperLabelStyle);

            Rect activeRow = NextRow(width);
            Rect switchRect = new Rect(activeRow.xMax - RebindButtonWidth, activeRow.y, RebindButtonWidth, activeRow.height);
            Rect activeLabelRect = new Rect(activeRow.x, activeRow.y, activeRow.width - RebindButtonWidth - 8, activeRow.height);
            GUI.Label(activeLabelRect, $"Active preset: {_activeToggleEverythingPresetName}", _stepperLabelStyle);
            if (GUI.Button(switchRect, "Switch..."))
                OpenToggleEverythingPresetPicker();

            Rect saveRow = NextRow(width);
            if (ToggleEverythingPresetStore.IsDefault(_activeToggleEverythingPresetName))
            {
                if (GUI.Button(saveRow, "Save current toggles as new preset..."))
                    OpenToggleEverythingPresetPicker(startInSaveAsMode: true);
            }
            else
            {
                float halfWidth = (saveRow.width - 8) / 2f;
                Rect updateRect = new Rect(saveRow.x, saveRow.y, halfWidth, saveRow.height);
                Rect saveAsRect = new Rect(updateRect.xMax + 8, saveRow.y, halfWidth, saveRow.height);
                if (GUI.Button(updateRect, $"Update '{_activeToggleEverythingPresetName}'"))
                    UpdateActiveToggleEverythingPreset();
                if (GUI.Button(saveAsRect, "Save As New..."))
                    OpenToggleEverythingPresetPicker(startInSaveAsMode: true);
            }
        }

        private float ComputeToggleEverythingContentHeight()
        {
            int rows = 2; // section label + toggle
            float gaps = 0f;
            if (Config.ToggleEverythingEnabled)
            {
                rows += 1 + 1 + 1 + 1; // "Presets" label + hint line + active/switch row + save-buttons row
                gaps += SectionGap;
            }
            return rows * (RowHeight + RowSpacing) + gaps;
        }

        // "All" plus every value of the game's own ECharacter enum, read via reflection rather
        // than hardcoded so this never drifts out of sync with the game's actual character list.
        private static string[] _characterFilterOptions;

        private static string[] CharacterFilterOptions()
        {
            if (_characterFilterOptions == null)
            {
                string[] names = Enum.GetNames(typeof(Il2Cpp.ECharacter));
                _characterFilterOptions = new string[names.Length + 1];
                _characterFilterOptions[0] = "All";
                Array.Copy(names, 0, _characterFilterOptions, 1, names.Length);
            }
            return _characterFilterOptions;
        }

        // Same +/- stepper shape as IntStepper, cycling through CharacterFilterOptions() instead
        // of a numeric range — see the file-level comment for why a text field isn't an option.
        private void DrawCharacterFilterRow(Rect row)
        {
            EnsureStepperLabelStyle();

            string[] options = CharacterFilterOptions();
            string current = string.IsNullOrEmpty(Config.PersonalLeaderboardCharacterFilter)
                ? "All"
                : Config.PersonalLeaderboardCharacterFilter;
            int index = Array.IndexOf(options, current);
            if (index < 0)
                index = 0;

            Rect labelRect = new Rect(row.x, row.y, LabelColumnWidth, row.height);
            float stepperWidth = row.width - LabelColumnWidth;
            float valueWidth = stepperWidth - StepperButtonWidth * 2 - 8;
            Rect minusRect = new Rect(row.xMax - stepperWidth, row.y, StepperButtonWidth, row.height);
            Rect valueRect = new Rect(minusRect.xMax + 4, row.y, valueWidth, row.height);
            Rect plusRect = new Rect(valueRect.xMax + 4, row.y, StepperButtonWidth, row.height);

            GUI.Label(labelRect, "Character filter", _stepperLabelStyle);
            GUI.Label(valueRect, options[index], _valueStyle);
            if (GUI.Button(minusRect, "-"))
                index = (index - 1 + options.Length) % options.Length;
            if (GUI.Button(plusRect, "+"))
                index = (index + 1) % options.Length;

            Config.PersonalLeaderboardCharacterFilter = options[index] == "All" ? string.Empty : options[index];
        }

        private void HandleScrollWheel(Rect viewportInWindowSpace, float maxScroll)
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.ScrollWheel)
                return;
            if (!viewportInWindowSpace.Contains(e.mousePosition))
                return;

            _scrollOffset = Mathf.Clamp(_scrollOffset + e.delta.y * 20f, 0, maxScroll);
            e.Use();
        }

        private void DrawScrollbar(Rect trackRect, float contentHeight, float viewportHeight, float maxScroll)
        {
            DrawSolidRect(trackRect, new Color(1f, 1f, 1f, 0.06f));

            if (contentHeight <= viewportHeight)
                return;

            float thumbHeightRatio = Mathf.Clamp01(viewportHeight / contentHeight);
            float thumbHeight = Mathf.Max(20f, trackRect.height * thumbHeightRatio);
            float scrollRatio = maxScroll > 0f ? _scrollOffset / maxScroll : 0f;
            float thumbY = trackRect.y + scrollRatio * (trackRect.height - thumbHeight);
            Rect thumbRect = new Rect(trackRect.x, thumbY, trackRect.width, thumbHeight);

            DrawSolidRect(thumbRect, _scrollbarDragging ? new Color(1f, 1f, 1f, 0.55f) : new Color(1f, 1f, 1f, 0.35f));

            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown && thumbRect.Contains(e.mousePosition))
            {
                _scrollbarDragging = true;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _scrollbarDragging)
            {
                float trackRange = trackRect.height - thumbHeight;
                if (trackRange > 0.01f)
                {
                    float deltaRatio = e.delta.y / trackRange;
                    _scrollOffset = Mathf.Clamp(_scrollOffset + deltaRatio * maxScroll, 0, maxScroll);
                }
                e.Use();
            }
            else if (e.type == EventType.MouseUp)
            {
                _scrollbarDragging = false;
            }
        }

        // Classic three-row diagonal dot grid (the same shape Windows/macOS use for a
        // window's resize handle), built from six small solid-color rects rather than a custom
        // texture — reuses the same already-proven GUI.DrawTexture + GUI.color path as
        // everything else instead of introducing per-pixel texture editing.
        private static void DrawResizeGrip(Rect gripRect)
        {
            Color dot = new Color(0.85f, 0.85f, 0.9f, 0.9f);
            const float dotSize = 2f;
            const float spacing = 4f;
            const float margin = 3f;

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col <= row; col++)
                {
                    float x = gripRect.xMax - margin - col * spacing - dotSize;
                    float y = gripRect.yMax - margin - row * spacing - dotSize;
                    DrawSolidRect(new Rect(x, y, dotSize, dotSize), dot);
                }
            }
        }

        // Moving and resizing are both handled by hand (mutating _windowRect directly) rather
        // than via GUI.Window's built-in title-bar dragging, since IMGUI has no built-in resize
        // at all and this reuses the same Event.current mechanism GUI.Button/Toggle already use
        // for their own hit-testing.
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
            Rect row = new Rect(0, _rowCursorY, width, RowHeight);
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
        // detection entirely for gameplay hotkeys.
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
        private void EnsureStepperLabelStyle()
        {
            if (_stepperLabelStyle != null)
                return;

            // wordWrap off + Clip: a label text too long for LabelColumnWidth is cut off
            // instead of wrapping onto a second line and bleeding into the row below (this
            // is exactly what happened with the Legendary Surge reduction label). Clipping a
            // label is a much less noticeable failure than a broken layout, and this makes
            // the whole class of bug impossible rather than just fixing today's instance.
            _stepperLabelStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip };
            ClearBackgrounds(_stepperLabelStyle);
        }

        private int IntStepper(Rect row, string label, int value, int smallStep, int largeStep, int min, int max)
        {
            EnsureStepperLabelStyle();

            Rect labelRect = new Rect(row.x, row.y, LabelColumnWidth, row.height);
            float stepperWidth = row.width - LabelColumnWidth;
            float valueWidth = stepperWidth - StepperButtonWidth * 2 - 8;

            Rect minusRect = new Rect(row.xMax - stepperWidth, row.y, StepperButtonWidth, row.height);
            Rect valueRect = new Rect(minusRect.xMax + 4, row.y, valueWidth, row.height);
            Rect plusRect = new Rect(valueRect.xMax + 4, row.y, StepperButtonWidth, row.height);

            GUI.Label(labelRect, label, _stepperLabelStyle);
            GUI.Label(valueRect, value.ToString(), _valueStyle);

            int step = Win32Input.IsShiftDown() ? largeStep : smallStep;
            if (GUI.Button(minusRect, "-"))
                value = Mathf.Clamp(value - step, min, max);
            if (GUI.Button(plusRect, "+"))
                value = Mathf.Clamp(value + step, min, max);
            return value;
        }
    }
}
