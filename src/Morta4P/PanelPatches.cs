using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zyklus.Managers;
using Zyklus.Player;
using Zyklus.UI;
using Zyklus.UI.CharacterSelectMenu;

namespace Morta4P
{
    // The character select prefab has two CharacterPanels (P1 left, P2 right) and every
    // CharacterButton has two "P1"/"P2" markers. Both are indexed directly by player index,
    // so P3/P4 would throw. The arrays are extended to four entries with clones, and when
    // more than two players are in the lobby each column is split: P1 above P3, P2 above P4.
    internal static class PanelLayout
    {
        private static readonly AccessTools.FieldRef<CharacterSelect, CharacterPanel[]> PanelsField =
            AccessTools.FieldRefAccess<CharacterSelect, CharacterPanel[]>("character_panels_");

        private sealed class MenuState
        {
            public CharacterSelect Menu;
            public RectTransform[] Originals = new RectTransform[2];
            public Vector3[] OriginalPositions = new Vector3[2];
            public Vector3[] OriginalScales = new Vector3[2];
            public CharacterPanel[] Clones = new CharacterPanel[2];
            public bool[] CloneShown = new bool[2];
            public TMP_Text[] CloneLabels = new TMP_Text[2];
            public bool Split;
        }

        private static readonly List<MenuState> Menus = new List<MenuState>();

        internal static bool Preview;

        private static readonly HashSet<CharacterSelect> Checked = new HashSet<CharacterSelect>();

        // Buttons can run Clear/Select before the menu's own Awake, so this is called lazily
        // from every patch that may touch P3/P4. Returns true when four panels exist.
        internal static bool Ensure(CharacterSelect menu)
        {
            try
            {
                return EnsureCore(menu);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"P3/P4 panels for {menu?.name}: {e}");
                return false;
            }
        }

        private static bool EnsureCore(CharacterSelect menu)
        {
            if (menu == null)
            {
                return false;
            }
            CharacterPanel[] panels = PanelsField(menu);
            if (panels != null && panels.Length >= 4)
            {
                return true;
            }
            if (!Checked.Add(menu))
            {
                return false;
            }
            if (panels == null || panels.Length != 2 || panels[0] == null || panels[1] == null)
            {
                Plugin.Log.LogWarning($"CharacterSelect {menu.name}: unexpected panel layout, P3/P4 panels disabled");
                return false;
            }

            var state = new MenuState { Menu = menu };
            var extended = new CharacterPanel[4];
            for (int i = 0; i < 2; i++)
            {
                extended[i] = panels[i];
                var rt = (RectTransform)panels[i].transform;
                state.Originals[i] = rt;
                state.OriginalPositions[i] = rt.localPosition;
                state.OriginalScales[i] = rt.localScale;

                var clone = Object.Instantiate(panels[i].gameObject, rt.parent);
                clone.name = panels[i].gameObject.name + "_Morta4P_P" + (i + 3);
                clone.SetActive(false);
                state.Clones[i] = clone.GetComponent<CharacterPanel>();
                extended[i + 2] = state.Clones[i];

                Plugin.Log.LogInfo($"{menu.name} panel P{i + 1}: pos={rt.localPosition} size={rt.rect.size} pivot={rt.pivot} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} scale={rt.localScale}");
            }
            PanelsField(menu) = extended;
            Menus.Add(state);
            return true;
        }

        private static readonly List<GameObject> previewMarkers_ = new List<GameObject>();

        // Preview: show a P3 and a P4 marker on two buttons of each visible menu.
        private static void UpdateMarkerPreview()
        {
            if (!Preview)
            {
                foreach (var marker in previewMarkers_)
                {
                    if (marker != null)
                    {
                        marker.SetActive(false);
                    }
                }
                previewMarkers_.Clear();
                return;
            }
            if (previewMarkers_.Count > 0)
            {
                return;
            }
            var used = new Dictionary<CharacterSelect, int>();
            foreach (var button in ButtonMarkers.Buttons)
            {
                var menu = button != null ? ButtonMarkers.GetMenu(button) : null;
                if (menu == null || !menu.pIsShowing)
                {
                    continue;
                }
                used.TryGetValue(menu, out int count);
                if (count >= 2)
                {
                    continue;
                }
                var marker = ButtonMarkers.GetMarker(button, 2 + count);
                if (marker != null)
                {
                    marker.SetActive(true);
                    previewMarkers_.Add(marker);
                    used[menu] = count + 1;
                }
            }
        }

        // Runs every frame from the plugin's MonoBehaviour.
        internal static void Update()
        {
            UpdateMarkerPreview();
            for (int m = Menus.Count - 1; m >= 0; m--)
            {
                var state = Menus[m];
                if (state.Menu == null)
                {
                    Menus.RemoveAt(m);
                    continue;
                }

                int players = 0;
                if (state.Menu.IsCoop() && state.Menu.pCurrentMode != CharacterSelect.Mode.MasterLocalCoop)
                {
                    players = PlayerManager.sSingleton?.GetActiveCapsule()?.pPlayerscount ?? 0;
                }
                if (Preview && state.Menu.pIsShowing)
                {
                    players = 4;
                }

                ApplySplit(state, players > 2);
                for (int i = 0; i < 2; i++)
                {
                    bool show = players > i + 2;
                    var clone = state.Clones[i];
                    if (show && !state.CloneShown[i])
                    {
                        clone.Reset();
                    }
                    if (clone.gameObject.activeSelf != show)
                    {
                        clone.gameObject.SetActive(show);
                    }
                    if (show)
                    {
                        FixLabel(state, i);
                    }
                    state.CloneShown[i] = show;
                }
            }
        }

        // The cloned panels keep the "Player 1"/"Player 2" header of their source. Rewrite the
        // trailing digit every frame, since localization can refresh the text at any time.
        private static void FixLabel(MenuState state, int cloneIndex)
        {
            char source = (char)('1' + cloneIndex);
            char target = (char)('3' + cloneIndex);
            TMP_Text label = state.CloneLabels[cloneIndex];
            if (label == null)
            {
                foreach (var text in state.Clones[cloneIndex].GetComponentsInChildren<TMP_Text>(true))
                {
                    string value = text.text?.TrimEnd();
                    if (!string.IsNullOrEmpty(value) && value.Length > 1 && value[value.Length - 1] == source
                        && !char.IsDigit(value[value.Length - 2]))
                    {
                        label = text;
                        break;
                    }
                }
                if (label == null)
                {
                    return;
                }
                state.CloneLabels[cloneIndex] = label;
                label.color = cloneIndex == 0 ? Plugin.P3Color : Plugin.P4Color;
            }

            // Runs every frame while the panel is shown: check without allocating.
            string raw = label.text;
            int last = raw == null ? -1 : raw.Length - 1;
            while (last >= 0 && char.IsWhiteSpace(raw[last]))
            {
                last--;
            }
            if (last < 0 || raw[last] == target)
            {
                return;
            }
            string current = raw.Substring(0, last + 1);
            string fixedText = current.Substring(0, current.Length - 1) + target;
            if (label is FText ftext)
            {
                ftext.clean_text = fixedText;
            }
            label.text = fixedText;
        }

        private static void ApplySplit(MenuState state, bool split)
        {
            if (state.Split == split && !Plugin.LayoutDirty)
            {
                return;
            }
            if (split && !state.Split)
            {
                // Capture where the game has the panels now, so un-splitting restores it.
                for (int i = 0; i < 2; i++)
                {
                    state.OriginalPositions[i] = state.Originals[i].localPosition;
                    state.OriginalScales[i] = state.Originals[i].localScale;
                }
            }
            state.Split = split;

            float s = Plugin.PanelScale.Value;
            for (int i = 0; i < 2; i++)
            {
                RectTransform original = state.Originals[i];
                var cloneRt = (RectTransform)state.Clones[i].transform;
                if (!split)
                {
                    original.localScale = state.OriginalScales[i];
                    original.localPosition = state.OriginalPositions[i];
                    continue;
                }

                Vector3 baseScale = state.OriginalScales[i];
                float h = original.rect.height * baseScale.y;
                float pivotY = original.pivot.y;
                Vector3 pos = state.OriginalPositions[i];

                // Keep the original's top edge where it was; put the clone right under it.
                float top = pos.y + (1f - pivotY) * h;
                var top_panel = new Vector3(pos.x, top - (1f - pivotY) * h * s, pos.z);
                original.localScale = baseScale * s;
                original.localPosition = top_panel;

                cloneRt.localScale = baseScale * s;
                cloneRt.localPosition = new Vector3(pos.x, top_panel.y - h * s - Plugin.PanelSpacing.Value, pos.z);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), "Awake")]
    internal static class CharacterSelect_Awake
    {
        private static void Postfix(CharacterSelect __instance)
        {
            PanelLayout.Ensure(__instance);
        }
    }

    // Each button has one "P1"/"P2" marker per player; add P3/P4 by cloning the P2 marker.
    internal static class ButtonMarkers
    {
        private static readonly AccessTools.FieldRef<CharacterButton, GameObject[]> TextsField =
            AccessTools.FieldRefAccess<CharacterButton, GameObject[]>("player_number_texts_");

        private static readonly AccessTools.FieldRef<CharacterButton, CharacterSelect> MenuField =
            AccessTools.FieldRefAccess<CharacterButton, CharacterSelect>("menu_");

        private static readonly HashSet<CharacterButton> Checked = new HashSet<CharacterButton>();

        // True when both the menu panels and this button's markers cover P3/P4.
        internal static bool Ensure(CharacterButton button)
        {
            try
            {
                return EnsureCore(button);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"P3/P4 markers for {button?.name}: {e}");
                return false;
            }
        }

        private static bool EnsureCore(CharacterButton button)
        {
            bool panels = PanelLayout.Ensure(MenuField(button));
            GameObject[] texts = TextsField(button);
            if (texts != null && texts.Length >= 4)
            {
                return panels;
            }
            if (!Checked.Add(button) || texts == null || texts.Length < 2 || texts[0] == null || texts[1] == null)
            {
                return false;
            }

            var extended = new GameObject[4];
            texts.CopyTo(extended, 0);
            Vector3 step = texts[1].transform.localPosition - texts[0].transform.localPosition;
            Sprite p1Sprite = texts[0].GetComponent<Image>()?.sprite;
            for (int i = texts.Length; i < 4; i++)
            {
                // Clone the P1 marker: the generated sprite is drawn from its 27x32 layout.
                var clone = Object.Instantiate(texts[0], texts[0].transform.parent);
                clone.name = texts[0].name + "_P" + (i + 1);
                clone.transform.localPosition = texts[1].transform.localPosition + step * (i - 1);
                clone.SetActive(false);

                Sprite sprite = null;
                try
                {
                    sprite = MarkerSprites.Get(i - 2, p1Sprite);
                }
                catch (System.Exception e)
                {
                    Plugin.Log.LogError($"P{i + 1} marker sprite: {e}");
                }

                var image = clone.GetComponent<Image>();
                if (sprite != null && image != null)
                {
                    image.sprite = sprite;
                }
                else
                {
                    MakeTextMarker(clone, "P" + (i + 1), i == 2 ? Plugin.P3Color : Plugin.P4Color, FindFont(MenuField(button)));
                }
                extended[i] = clone;
            }
            TextsField(button) = extended;
            Buttons.Add(button);
            return panels;
        }

        internal static readonly List<CharacterButton> Buttons = new List<CharacterButton>();

        internal static GameObject GetMarker(CharacterButton button, int index)
        {
            GameObject[] texts = TextsField(button);
            return texts != null && index < texts.Length ? texts[index] : null;
        }

        internal static CharacterSelect GetMenu(CharacterButton button) => MenuField(button);

        private static TMP_FontAsset font_;

        // Use the pixel font the menu already uses for its own labels.
        private static TMP_FontAsset FindFont(CharacterSelect menu)
        {
            if (font_ != null)
            {
                return font_;
            }
            if (menu != null)
            {
                foreach (var text in menu.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.font != null)
                    {
                        font_ = text.font;
                        Plugin.Log.LogInfo($"P3/P4 marker font: {font_.name} (from {text.name})");
                        break;
                    }
                }
            }
            return font_;
        }

        // The P1/P2 markers are images with the label drawn in the sprite ("Player 01",
        // "Player 2"). For P3/P4 the sprite is hidden and a text label is drawn instead.
        private static void MakeTextMarker(GameObject marker, string label, Color color, TMP_FontAsset font)
        {
            foreach (var image in marker.GetComponents<Image>())
            {
                image.enabled = false;
            }

            var go = new GameObject("Label_" + label, typeof(RectTransform));
            go.layer = marker.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(marker.transform, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(-6f, 0f);
            rt.offsetMax = new Vector2(6f, 0f);

            var text = go.AddComponent<TextMeshProUGUI>();
            if (font != null)
            {
                text.font = font;
            }
            text.text = label;
            text.color = color;
            text.alignment = TextAlignmentOptions.Center;
            text.enableWordWrapping = false;
            text.enableAutoSizing = true;
            text.fontSizeMin = 6f;
            text.fontSizeMax = 40f;
            text.raycastTarget = false;
        }
    }

    [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.UI_Select))]
    internal static class CharacterButton_UI_Select
    {
        private static void Prefix(CharacterButton __instance) => ButtonMarkers.Ensure(__instance);
    }

    [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.UI_Highlight))]
    internal static class CharacterButton_UI_Highlight
    {
        private static void Prefix(CharacterButton __instance) => ButtonMarkers.Ensure(__instance);
    }

    // Original Clear() only resets P1 and P2.
    [HarmonyPatch(typeof(CharacterButton), nameof(CharacterButton.Clear))]
    internal static class CharacterButton_Clear
    {
        private static void Postfix(CharacterButton __instance)
        {
            if (!ButtonMarkers.Ensure(__instance))
            {
                return;
            }
            try
            {
                foreach (var number in new[] { PlayerNumber.P3, PlayerNumber.P4 })
                {
                    __instance.ForceSelect(number, select: false, animate: false, force: true, ignore_legends: true);
                    __instance.ForceHighlight(number, highlight: false, change_highlight_stroke: true, force: true);
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"CharacterButton.Clear P3/P4: {e}");
            }
        }
    }

    // The egg / cure-fatigue widgets exist only for two players.
    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.UpdateCureFatigueVisual))]
    internal static class CharacterSelect_UpdateCureFatigueVisual
    {
        private static bool Prefix(PlayerNumber player) => player.ToPlayerIndex() < 2;
    }

    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.SetCureFatigueTarget))]
    internal static class CharacterSelect_SetCureFatigueTarget
    {
        private static bool Prefix(PlayerNumber player) => player.ToPlayerIndex() < 2;
    }
}
