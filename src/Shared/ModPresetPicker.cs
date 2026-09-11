using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterBonk.Shared
{
    // The preset-switcher window: lists every saved preset (plus the always-present "Default"
    // baseline) with Load/Delete buttons, and a "Save current settings as new preset..." flow
    // that switches the same window into a small name-entry mode. Split out from
    // ModMenu.cs/ModCore.cs for the same readability reason as ModItemPicker.cs, and built the
    // same way — raw GUI.* Rects, no GUILayout, no GUI.TextField (see ModMenu.cs's file-level
    // comment for why neither is safe on this build).
    //
    // The name-entry box reuses the same hand-rolled text-input approach as the item picker's
    // search box (Win32 key polling), but with its own separate field/dictionary state rather
    // than sharing ModItemPicker's, so the two pickers can never interfere with each other.
    public sealed partial class ModCore
    {
        private enum PresetPickerMode
        {
            Browse,
            SaveAs
        }

        private const float PresetActionButtonWidth = 60f;
        private const float PresetButtonGap = 6f;

        private bool _presetPickerOpen;
        private PresetPickerMode _presetPickerMode = PresetPickerMode.Browse;
        private Rect _presetPickerRect = new Rect(540, 460, 320, 300);
        private bool _presetPickerDragging;
        private bool _presetPickerResizing;
        private string _newPresetNameText = string.Empty;
        private Dictionary<int, bool> _presetNameKeyWasDown;
        private GUIStyle _presetPickerTitleStyle;
        private GUIStyle _presetPickerHintStyle;

        private void OpenPresetPicker(bool startInSaveAsMode = false)
        {
            _presetPickerOpen = true;
            _presetPickerMode = startInSaveAsMode ? PresetPickerMode.SaveAs : PresetPickerMode.Browse;
            _newPresetNameText = string.Empty;
            _presetNameKeyWasDown = null;
        }

        private void DrawPresetPickerWindow()
        {
            if (!_presetPickerOpen)
                return;

            System.Action<int> drawPicker = DrawPresetPickerContents;
            GUI.Window(GetHashCode() + 2, _presetPickerRect, drawPicker, string.Empty);
        }

        private void DrawPresetPickerContents(int windowId)
        {
            EnsurePresetPickerStyles();
            if (_presetPickerMode == PresetPickerMode.SaveAs)
                PollNewPresetNameInput();

            Rect titleBarRect = new Rect(0, 0, _presetPickerRect.width, Margin + TitleHeight);
            Rect gripRect = new Rect(_presetPickerRect.width - GripSize, _presetPickerRect.height - GripSize, GripSize, GripSize);
            HandlePresetPickerDragAndResize(titleBarRect, gripRect);

            DrawSolidRect(new Rect(0, 0, _presetPickerRect.width, _presetPickerRect.height), BackgroundColor);

            float contentWidth = _presetPickerRect.width - Margin * 2;
            float y = Margin;

            string title = _presetPickerMode == PresetPickerMode.Browse ? "Presets" : "Save As New Preset";
            GUI.Label(new Rect(Margin, y, contentWidth, TitleHeight), title, _presetPickerTitleStyle);
            y += TitleHeight;

            if (_presetPickerMode == PresetPickerMode.Browse)
                DrawPresetBrowseContents(contentWidth, y);
            else
                DrawPresetSaveAsContents(contentWidth, y);

            Rect closeRect = new Rect(Margin, _presetPickerRect.height - Margin - RowHeight, contentWidth, RowHeight);
            if (GUI.Button(closeRect, "Close"))
                _presetPickerOpen = false;

            DrawResizeGrip(gripRect);
        }

        private void DrawPresetBrowseContents(float contentWidth, float y)
        {
            Rect newRect = new Rect(Margin, y, contentWidth, RowHeight);
            if (GUI.Button(newRect, "+ Save current settings as new preset..."))
            {
                _presetPickerMode = PresetPickerMode.SaveAs;
                _newPresetNameText = string.Empty;
                _presetNameKeyWasDown = null;
                return; // redraws in the new mode next frame
            }
            y += RowHeight + RowSpacing;

            float listBottom = _presetPickerRect.height - Margin - RowHeight - SectionGap;
            foreach (string name in _presetStore.ListPresetNames())
            {
                if (y + RowHeight > listBottom)
                {
                    GUI.Label(new Rect(Margin, y, contentWidth, RowHeight),
                        "More presets — resize to see them all", _presetPickerHintStyle);
                    break;
                }

                bool isActive = string.Equals(name, _activePresetName, StringComparison.OrdinalIgnoreCase);
                bool isDefault = PresetStore.IsDefault(name);

                float reservedWidth = isDefault
                    ? PresetActionButtonWidth + PresetButtonGap
                    : (PresetActionButtonWidth + PresetButtonGap) * 2;

                Rect nameRect = new Rect(Margin, y, contentWidth - reservedWidth, RowHeight);
                Rect loadRect = new Rect(nameRect.xMax + PresetButtonGap, y, PresetActionButtonWidth, RowHeight);

                GUI.Label(nameRect, isActive ? $"{name} (active)" : name, _presetPickerHintStyle);

                if (GUI.Button(loadRect, "Load"))
                {
                    ApplyPreset(name);
                    _presetPickerOpen = false;
                    return;
                }

                if (!isDefault)
                {
                    Rect deleteRect = new Rect(loadRect.xMax + PresetButtonGap, y, PresetActionButtonWidth, RowHeight);
                    if (GUI.Button(deleteRect, "Delete"))
                    {
                        DeletePreset(name);
                        return; // the list just changed — redraw fresh next frame
                    }
                }

                y += RowHeight + RowSpacing;
            }
        }

        private void DrawPresetSaveAsContents(float contentWidth, float y)
        {
            GUI.Label(new Rect(Margin, y, contentWidth, RowHeight), "Preset name:", _presetPickerHintStyle);
            y += RowHeight + RowSpacing;

            DrawPresetNameBox(new Rect(Margin, y, contentWidth, RowHeight));
            y += RowHeight + RowSpacing;

            GUI.Label(new Rect(Margin, y, contentWidth, RowHeight),
                "Type to enter a name, Backspace to edit", _presetPickerHintStyle);
            y += RowHeight + RowSpacing;

            float buttonWidth = (contentWidth - PresetButtonGap) / 2f;
            Rect cancelRect = new Rect(Margin, y, buttonWidth, RowHeight);
            Rect saveRect = new Rect(cancelRect.xMax + PresetButtonGap, y, buttonWidth, RowHeight);

            if (GUI.Button(cancelRect, "Cancel"))
                _presetPickerMode = PresetPickerMode.Browse;

            if (GUI.Button(saveRect, "Save"))
            {
                if (SaveCurrentAsNewPreset(_newPresetNameText))
                {
                    _presetPickerMode = PresetPickerMode.Browse;
                    _presetPickerOpen = false;
                }
                // On failure (empty name, "Default", or a write error) SaveCurrentAsNewPreset has
                // already logged why — the box stays open so the name can be fixed and retried.
            }
        }

        private void EnsurePresetPickerStyles()
        {
            if (_presetPickerTitleStyle != null)
                return;

            _presetPickerTitleStyle = new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _presetPickerTitleStyle.normal.textColor = Color.white;

            _presetPickerHintStyle = new GUIStyle(GUI.skin.label) { wordWrap = false, clipping = TextClipping.Clip };
            ClearBackgrounds(_presetPickerHintStyle);
        }

        // Same manual drag/resize approach as the other two windows (see ModMenu.cs) — kept as
        // separate state so dragging any one of the three windows never affects the others.
        private void HandlePresetPickerDragAndResize(Rect titleBarRect, Rect gripRect)
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDown)
            {
                if (gripRect.Contains(e.mousePosition))
                {
                    _presetPickerResizing = true;
                    e.Use();
                }
                else if (titleBarRect.Contains(e.mousePosition))
                {
                    _presetPickerDragging = true;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseDrag)
            {
                if (_presetPickerResizing)
                {
                    _presetPickerRect.width = Mathf.Max(MinWindowWidth, _presetPickerRect.width + e.delta.x);
                    _presetPickerRect.height = Mathf.Max(MinWindowHeight, _presetPickerRect.height + e.delta.y);
                    e.Use();
                }
                else if (_presetPickerDragging)
                {
                    _presetPickerRect.x += e.delta.x;
                    _presetPickerRect.y += e.delta.y;
                    e.Use();
                }
            }
            else if (e.type == EventType.MouseUp)
            {
                _presetPickerDragging = false;
                _presetPickerResizing = false;
            }
        }

        // A minimal hand-rolled text box, the same shape as ModItemPicker.cs's search box (see
        // its file-level comment for why this exists instead of GUI.TextField), but drawing its
        // own separate _newPresetNameText field.
        private void DrawPresetNameBox(Rect rect)
        {
            DrawSolidRect(rect, new Color(0.6f, 0.6f, 0.65f, 0.9f));
            DrawSolidRect(new Rect(rect.x + 1, rect.y + 1, rect.width - 2, rect.height - 2), BackgroundColor);

            bool caretOn = (Time.frameCount / 20) % 2 == 0;
            string display = _newPresetNameText + (caretOn ? "_" : string.Empty);
            GUI.Label(new Rect(rect.x + 6, rect.y, rect.width - 12, rect.height), display);
        }

        private void PollNewPresetNameInput()
        {
            if (_presetNameKeyWasDown == null)
            {
                _presetNameKeyWasDown = new Dictionary<int, bool>();
                foreach (int vk in PresetNameVirtualKeys())
                    _presetNameKeyWasDown[vk] = Win32Input.IsKeyDown(vk);
                return;
            }

            foreach (int vk in PresetNameVirtualKeys())
            {
                bool isDown = Win32Input.IsKeyDown(vk);
                bool wasDown = _presetNameKeyWasDown.TryGetValue(vk, out bool prev) && prev;
                _presetNameKeyWasDown[vk] = isDown;

                if (!isDown || wasDown)
                    continue;

                HandlePresetNameKeyPress(vk);
            }
        }

        private void HandlePresetNameKeyPress(int virtualKeyCode)
        {
            const int VK_BACK = 0x08;
            const int VK_SPACE = 0x20;
            const int maxNameLength = 40;

            if (virtualKeyCode == VK_BACK)
            {
                if (_newPresetNameText.Length > 0)
                    _newPresetNameText = _newPresetNameText.Substring(0, _newPresetNameText.Length - 1);
                return;
            }

            if (_newPresetNameText.Length >= maxNameLength)
                return;

            if (virtualKeyCode == VK_SPACE)
            {
                _newPresetNameText += " ";
                return;
            }

            // A-Z / 0-9 virtual-key codes are numerically identical to their ASCII character.
            _newPresetNameText += ((char)virtualKeyCode).ToString();
        }

        private static IEnumerable<int> PresetNameVirtualKeys()
        {
            for (int vk = 0x41; vk <= 0x5A; vk++) yield return vk; // A-Z
            for (int vk = 0x30; vk <= 0x39; vk++) yield return vk; // 0-9
            yield return 0x20; // Space
            yield return 0x08; // Backspace
        }
    }
}
