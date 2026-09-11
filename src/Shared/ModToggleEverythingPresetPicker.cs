using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterBonk.Shared
{
    // The Toggle Everything preset-switcher window: lists every saved preset (plus the
    // always-present "Default" baseline, meaning nothing toggled off) with Load/Delete buttons,
    // and a "Save current toggles as new preset..." flow that switches the same window into a
    // small name-entry mode. Deliberately a near-identical twin of ModPresetPicker.cs rather
    // than a shared/generalized window — it drives a completely separate data source
    // (ToggleEverythingPresetStore, snapshotting the game's own achievement toggle state) and
    // needs its own open/mode/rect/dragging state so it can never interfere with the FilterConfig
    // preset picker, but reuses that file's already-built GUIStyle fields (_presetPickerTitleStyle/
    // _presetPickerHintStyle) since those are purely cosmetic and not tied to either picker's data.
    public sealed partial class ModCore
    {
        private enum ToggleEverythingPresetPickerMode
        {
            Browse,
            SaveAs
        }

        private bool _toggleEverythingPresetPickerOpen;
        private ToggleEverythingPresetPickerMode _toggleEverythingPresetPickerMode = ToggleEverythingPresetPickerMode.Browse;
        private Rect _toggleEverythingPresetPickerRect = new Rect(560, 480, 320, 300);
        private bool _toggleEverythingPresetPickerDragging;
        private bool _toggleEverythingPresetPickerResizing;
        private string _newToggleEverythingPresetNameText = string.Empty;
        private Dictionary<int, bool> _toggleEverythingPresetNameKeyWasDown;

        private void OpenToggleEverythingPresetPicker(bool startInSaveAsMode = false)
        {
            EnsureToggleEverythingPresetStore();
            _toggleEverythingPresetPickerOpen = true;
            _toggleEverythingPresetPickerMode = startInSaveAsMode
                ? ToggleEverythingPresetPickerMode.SaveAs
                : ToggleEverythingPresetPickerMode.Browse;
            _newToggleEverythingPresetNameText = string.Empty;
            _toggleEverythingPresetNameKeyWasDown = null;
        }

        private void DrawToggleEverythingPresetPickerWindow()
        {
            if (!_toggleEverythingPresetPickerOpen)
                return;

            System.Action<int> drawPicker = DrawToggleEverythingPresetPickerContents;
            GUI.Window(GetHashCode() + 3, _toggleEverythingPresetPickerRect, drawPicker, string.Empty);
        }

        private void DrawToggleEverythingPresetPickerContents(int windowId)
        {
            EnsurePresetPickerStyles();
            if (_toggleEverythingPresetPickerMode == ToggleEverythingPresetPickerMode.SaveAs)
                PollNewToggleEverythingPresetNameInput();

            Rect titleBarRect = new Rect(0, 0, _toggleEverythingPresetPickerRect.width, Margin + TitleHeight);
            Rect gripRect = new Rect(
                _toggleEverythingPresetPickerRect.width - GripSize,
                _toggleEverythingPresetPickerRect.height - GripSize, GripSize, GripSize);
            HandleToggleEverythingPresetPickerDragAndResize(titleBarRect, gripRect);

            DrawSolidRect(new Rect(0, 0, _toggleEverythingPresetPickerRect.width, _toggleEverythingPresetPickerRect.height), BackgroundColor);

            float contentWidth = _toggleEverythingPresetPickerRect.width - Margin * 2;
            float y = Margin;

            string title = _toggleEverythingPresetPickerMode == ToggleEverythingPresetPickerMode.Browse
                ? "Toggle Everything Presets"
                : "Save As New Preset";
            GUI.Label(new Rect(Margin, y, contentWidth, TitleHeight), title, _presetPickerTitleStyle);
            y += TitleHeight;

            if (_toggleEverythingPresetPickerMode == ToggleEverythingPresetPickerMode.Browse)
                DrawToggleEverythingPresetBrowseContents(contentWidth, y);
            else
                DrawToggleEverythingPresetSaveAsContents(contentWidth, y);

            Rect closeRect = new Rect(Margin, _toggleEverythingPresetPickerRect.height - Margin - RowHeight, contentWidth, RowHeight);
            if (GUI.Button(closeRect, "Close"))
                _toggleEverythingPresetPickerOpen = false;

            DrawResizeGrip(gripRect);
        }

        private void DrawToggleEverythingPresetBrowseContents(float contentWidth, float y)
        {
            Rect newRect = new Rect(Margin, y, contentWidth, RowHeight);
            if (GUI.Button(newRect, "+ Save current toggles as new preset..."))
            {
                _toggleEverythingPresetPickerMode = ToggleEverythingPresetPickerMode.SaveAs;
                _newToggleEverythingPresetNameText = string.Empty;
                _toggleEverythingPresetNameKeyWasDown = null;
                return; // redraws in the new mode next frame
            }
            y += RowHeight + RowSpacing;

            float listBottom = _toggleEverythingPresetPickerRect.height - Margin - RowHeight - SectionGap;
            foreach (string name in _toggleEverythingPresetStore.ListPresetNames())
            {
                if (y + RowHeight > listBottom)
                {
                    GUI.Label(new Rect(Margin, y, contentWidth, RowHeight),
                        "More presets — resize to see them all", _presetPickerHintStyle);
                    break;
                }

                bool isActive = string.Equals(name, _activeToggleEverythingPresetName, StringComparison.OrdinalIgnoreCase);
                bool isDefault = ToggleEverythingPresetStore.IsDefault(name);

                float reservedWidth = isDefault
                    ? PresetActionButtonWidth + PresetButtonGap
                    : (PresetActionButtonWidth + PresetButtonGap) * 2;

                Rect nameRect = new Rect(Margin, y, contentWidth - reservedWidth, RowHeight);
                Rect loadRect = new Rect(nameRect.xMax + PresetButtonGap, y, PresetActionButtonWidth, RowHeight);

                GUI.Label(nameRect, isActive ? $"{name} (active)" : name, _presetPickerHintStyle);

                if (GUI.Button(loadRect, "Load"))
                {
                    ApplyToggleEverythingPreset(name);
                    _toggleEverythingPresetPickerOpen = false;
                    return;
                }

                if (!isDefault)
                {
                    Rect deleteRect = new Rect(loadRect.xMax + PresetButtonGap, y, PresetActionButtonWidth, RowHeight);
                    if (GUI.Button(deleteRect, "Delete"))
                    {
                        DeleteToggleEverythingPreset(name);
                        return; // the list just changed — redraw fresh next frame
                    }
                }

                y += RowHeight + RowSpacing;
            }
        }

        private void DrawToggleEverythingPresetSaveAsContents(float contentWidth, float y)
        {
            GUI.Label(new Rect(Margin, y, contentWidth, RowHeight), "Preset name:", _presetPickerHintStyle);
            y += RowHeight + RowSpacing;

            DrawToggleEverythingPresetNameBox(new Rect(Margin, y, contentWidth, RowHeight));
            y += RowHeight + RowSpacing;

            GUI.Label(new Rect(Margin, y, contentWidth, RowHeight),
                "Type to enter a name, Backspace to edit", _presetPickerHintStyle);
            y += RowHeight + RowSpacing;

            float buttonWidth = (contentWidth - PresetButtonGap) / 2f;
            Rect cancelRect = new Rect(Margin, y, buttonWidth, RowHeight);
            Rect saveRect = new Rect(cancelRect.xMax + PresetButtonGap, y, buttonWidth, RowHeight);

            if (GUI.Button(cancelRect, "Cancel"))
                _toggleEverythingPresetPickerMode = ToggleEverythingPresetPickerMode.Browse;

            if (GUI.Button(saveRect, "Save"))
            {
                if (SaveCurrentAsNewToggleEverythingPreset(_newToggleEverythingPresetNameText))
                {
                    _toggleEverythingPresetPickerMode = ToggleEverythingPresetPickerMode.Browse;
                    _toggleEverythingPresetPickerOpen = false;
                }
                // On failure (empty name, "Default", or a write error) the store has already
                // logged why — the box stays open so the name can be fixed and retried.
            }
        }

        // Same manual drag/resize approach as the other windows (see ModMenu.cs) — kept as
        // separate state so dragging any one of them never affects the others.
        private void HandleToggleEverythingPresetPickerDragAndResize(Rect titleBarRect, Rect gripRect)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown)
            {
                if (gripRect.Contains(e.mousePosition))
                {
                    _toggleEverythingPresetPickerResizing = true;
                    e.Use();
                }
                else if (titleBarRect.Contains(e.mousePosition))
                {
                    _toggleEverythingPresetPickerDragging = true;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_toggleEverythingPresetPickerResizing)
                {
                    _toggleEverythingPresetPickerRect.width = Mathf.Max(MinWindowWidth, _toggleEverythingPresetPickerRect.width + e.delta.x);
                    _toggleEverythingPresetPickerRect.height = Mathf.Max(MinWindowHeight, _toggleEverythingPresetPickerRect.height + e.delta.y);
                    e.Use();
                }
                else if (_toggleEverythingPresetPickerDragging)
                {
                    _toggleEverythingPresetPickerRect.x += e.delta.x;
                    _toggleEverythingPresetPickerRect.y += e.delta.y;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _toggleEverythingPresetPickerDragging = false;
                _toggleEverythingPresetPickerResizing = false;
            }
        }

        // A minimal hand-rolled text box, the same shape as ModPresetPicker.cs's name box (see
        // ModMenu.cs's file-level comment for why this exists instead of GUI.TextField), but
        // drawing its own separate _newToggleEverythingPresetNameText field.
        private void DrawToggleEverythingPresetNameBox(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.6f, 0.6f, 0.65f, 0.9f));
            DrawSolidRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), BackgroundColor);

            bool caretOn = (Time.frameCount / 20) % 2 == 0;
            string display = _newToggleEverythingPresetNameText + (caretOn ? "_" : string.Empty);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 12, rect.height), display);
        }

        private void PollNewToggleEverythingPresetNameInput()
        {
            if (_toggleEverythingPresetNameKeyWasDown == null)
            {
                _toggleEverythingPresetNameKeyWasDown = new Dictionary<int, bool>();
                foreach (int vk in PresetNameVirtualKeys())
                    _toggleEverythingPresetNameKeyWasDown[vk] = Win32Input.IsKeyDown(vk);
                return;
            }

            foreach (int vk in PresetNameVirtualKeys())
            {
                bool isDown = Win32Input.IsKeyDown(vk);
                bool wasDown = _toggleEverythingPresetNameKeyWasDown.TryGetValue(vk, out bool prev) && prev;
                _toggleEverythingPresetNameKeyWasDown[vk] = isDown;

                if (!isDown || wasDown)
                    continue;

                HandleToggleEverythingPresetNameKeyPress(vk);
            }
        }

        private void HandleToggleEverythingPresetNameKeyPress(int virtualKeyCode)
        {
            const int VK_BACK = 0x08;
            const int VK_SPACE = 0x20;
            const int maxNameLength = 40;

            if (virtualKeyCode == VK_BACK)
            {
                if (_newToggleEverythingPresetNameText.Length > 0)
                    _newToggleEverythingPresetNameText = _newToggleEverythingPresetNameText.Substring(0, _newToggleEverythingPresetNameText.Length - 1);
                return;
            }

            if (_newToggleEverythingPresetNameText.Length >= maxNameLength)
                return;

            if (virtualKeyCode == VK_SPACE)
            {
                _newToggleEverythingPresetNameText += " ";
                return;
            }

            // A-Z / 0-9 virtual-key codes are numerically identical to their ASCII character.
            _newToggleEverythingPresetNameText += ((char)virtualKeyCode).ToString();
        }
    }
}
