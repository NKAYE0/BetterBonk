using System;
using System.Collections.Generic;
using System.Linq;
using Il2CppAssets.Scripts.Inventory__Items__Pickups.Items;
using UnityEngine;

namespace BetterBonk.Shared
{
    // Searchable picker for the "Required Item" setting, split out from ModMenu.cs. The game's
    // assembly has no static item-to-rarity table (rarity is assigned per Legendary-tier
    // ItemData instance at runtime), so this list is hard-coded rather than derived from the
    // EItem enum by reflection — it's the set of items the user confirmed, in-game, as
    // Legendary-tier, cross-checked against a community item list and matched to the game's
    // real EItem enum member names by decompiling Assembly-CSharp.dll. A couple of entries
    // (SpeedBoi, JoesDagger) were matched from icon appearance rather than an on-screen label
    // and are worth a second look if a chosen item never seems to satisfy the requirement; one
    // item from the reference list (GoldenRing) wasn't visible in the user's in-game screenshot
    // at all, so its Legendary status is unconfirmed and it's left out pending that. If the
    // filtered list ever needs to be regenerated after a game update, LegendaryItemNames is the
    // one place to edit — each entry is validated against the live EItem enum before use, so a
    // renamed or removed item is silently dropped rather than crashing.
    //
    // The search box is a small hand-rolled text input, not GUI.TextField: see ModMenu.cs's
    // file-level comment — this build's GUI.TextField throws every frame. Keystrokes are read
    // via the same Win32 polling already used for hotkeys and rebinding.
    public sealed partial class ModCore
    {
        private static readonly string[] LegendaryItemNames =
        {
            "Anvil", "Bonker", "Chonkplate", "Dragonfire", "GiantFork", "GlovePower", "HolyBook",
            "JoesDagger", "LightningOrb", "OverpoweredLamp", "Pot", "SpeedBoi", "SpicyMeatball",
            "SuckyMagnet", "WizardsHat", "ZaWarudo",
        };

        private static string[] _allItemNames;

        private bool _itemPickerOpen;
        private Rect _itemPickerRect = new Rect(540, 20, 320, 420);
        private bool _itemPickerDragging;
        private bool _itemPickerResizing;
        private string _itemSearchText = string.Empty;
        private Dictionary<int, bool> _searchKeyWasDown;
        private GUIStyle _itemPickerTitleStyle;
        private GUIStyle _hintStyle;

        private static string[] AllItemNames
        {
            get
            {
                if (_allItemNames == null)
                {
                    _allItemNames = LegendaryItemNames
                        .Where(n => Enum.IsDefined(typeof(EItem), n))
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                }
                return _allItemNames;
            }
        }

        private void OpenItemPicker()
        {
            _itemPickerOpen = true;
            _itemSearchText = string.Empty;
            _searchKeyWasDown = null;
        }

        private void DrawItemPickerWindow()
        {
            if (!_itemPickerOpen)
                return;

            System.Action<int> drawPicker = DrawItemPickerContents;
            GUI.Window(GetHashCode() + 1, _itemPickerRect, drawPicker, string.Empty);
        }

        private void DrawItemPickerContents(int windowId)
        {
            EnsureItemPickerStyles();
            PollSearchTextInput();

            Rect titleBarRect = new Rect(0, 0, _itemPickerRect.width, Margin + TitleHeight);
            Rect gripRect = new Rect(_itemPickerRect.width - GripSize, _itemPickerRect.height - GripSize, GripSize, GripSize);
            HandleItemPickerDragAndResize(titleBarRect, gripRect);

            DrawSolidRect(new Rect(0, 0, _itemPickerRect.width, _itemPickerRect.height), BackgroundColor);

            float contentWidth = _itemPickerRect.width - Margin * 2;
            float y = Margin;

            GUI.Label(new Rect(Margin, y, contentWidth, TitleHeight), "Select Legendary Item", _itemPickerTitleStyle);
            y += TitleHeight;

            Rect searchRect = new Rect(Margin, y, contentWidth, RowHeight);
            DrawSearchBox(searchRect);
            y += RowHeight + RowSpacing;

            GUI.Label(new Rect(Margin, y, contentWidth, RowHeight), "Type to search, Backspace to edit", _hintStyle);
            y += RowHeight + RowSpacing;

            IEnumerable<string> matches = string.IsNullOrEmpty(_itemSearchText)
                ? AllItemNames
                : AllItemNames.Where(n => n.IndexOf(_itemSearchText, StringComparison.OrdinalIgnoreCase) >= 0);

            float listBottom = _itemPickerRect.height - Margin - RowHeight - SectionGap;
            int shown = 0;
            bool truncated = false;
            foreach (string name in matches)
            {
                if (y + RowHeight > listBottom)
                {
                    truncated = true;
                    break;
                }

                if (GUI.Button(new Rect(Margin, y, contentWidth, RowHeight), name))
                {
                    Config.RequiredLegendaryItemName = name;
                    SaveConfig();
                    _itemPickerOpen = false;
                }
                y += RowHeight + RowSpacing;
                shown++;
            }

            if (shown == 0)
            {
                GUI.Label(new Rect(Margin, y, contentWidth, RowHeight), "No matches");
            }
            else if (truncated)
            {
                GUI.Label(new Rect(Margin, y, contentWidth, RowHeight), "More matches — keep typing to narrow", _hintStyle);
            }

            Rect closeRect = new Rect(Margin, _itemPickerRect.height - Margin - RowHeight, contentWidth, RowHeight);
            if (GUI.Button(closeRect, "Close"))
                _itemPickerOpen = false;

            DrawResizeGrip(gripRect);
        }

        private void EnsureItemPickerStyles()
        {
            if (_itemPickerTitleStyle != null)
                return;

            _itemPickerTitleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _itemPickerTitleStyle.normal.textColor = Color.white;

            _hintStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Italic };
            _hintStyle.normal.textColor = new Color(0.7f, 0.7f, 0.75f);
        }

        // Same manual drag/resize approach as the main settings window (see ModMenu.cs) — kept
        // as separate state so dragging one window never affects the other.
        private void HandleItemPickerDragAndResize(Rect titleBarRect, Rect gripRect)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown)
            {
                if (gripRect.Contains(e.mousePosition))
                {
                    _itemPickerResizing = true;
                    e.Use();
                }
                else if (titleBarRect.Contains(e.mousePosition))
                {
                    _itemPickerDragging = true;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_itemPickerResizing)
                {
                    _itemPickerRect.width = Mathf.Max(MinWindowWidth, _itemPickerRect.width + e.delta.x);
                    _itemPickerRect.height = Mathf.Max(MinWindowHeight, _itemPickerRect.height + e.delta.y);
                    e.Use();
                }
                else if (_itemPickerDragging)
                {
                    _itemPickerRect.x += e.delta.x;
                    _itemPickerRect.y += e.delta.y;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _itemPickerDragging = false;
                _itemPickerResizing = false;
            }
        }

        // A minimal hand-rolled text box: draws the typed text (with a blinking-ish caret) in a
        // bordered field, and PollSearchTextInput below does the actual character capture.
        private void DrawSearchBox(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.6f, 0.6f, 0.65f, 0.9f));
            DrawSolidRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), BackgroundColor);

            bool caretOn = (Time.frameCount / 20) % 2 == 0;
            string display = _itemSearchText + (caretOn ? "_" : string.Empty);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 12, rect.height), display);
        }

        private void PollSearchTextInput()
        {
            if (_searchKeyWasDown == null)
            {
                _searchKeyWasDown = new Dictionary<int, bool>();
                foreach (int vk in SearchableVirtualKeys())
                    _searchKeyWasDown[vk] = Win32Input.IsKeyDown(vk);
                return;
            }

            foreach (int vk in SearchableVirtualKeys())
            {
                bool isDown = Win32Input.IsKeyDown(vk);
                bool wasDown = _searchKeyWasDown.TryGetValue(vk, out bool prev) && prev;
                _searchKeyWasDown[vk] = isDown;

                if (!isDown || wasDown)
                    continue;

                HandleSearchKeyPress(vk);
            }
        }

        private void HandleSearchKeyPress(int virtualKeyCode)
        {
            const int VK_BACK = 0x08;
            const int VK_SPACE = 0x20;

            if (virtualKeyCode == VK_BACK)
            {
                if (_itemSearchText.Length > 0)
                    _itemSearchText = _itemSearchText.Substring(0, _itemSearchText.Length - 1);
                return;
            }

            if (virtualKeyCode == VK_SPACE)
            {
                _itemSearchText += " ";
                return;
            }

            // A-Z / 0-9 virtual-key codes are numerically identical to their ASCII character.
            _itemSearchText += ((char)virtualKeyCode).ToString();
        }

        private static IEnumerable<int> SearchableVirtualKeys()
        {
            for (int vk = 0x41; vk <= 0x5A; vk++) yield return vk; // A-Z
            for (int vk = 0x30; vk <= 0x39; vk++) yield return vk; // 0-9
            yield return 0x20; // Space
            yield return 0x08; // Backspace
        }
    }
}
