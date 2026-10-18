using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Zyklus.Player;
using Zyklus.UI;
using Zyklus.UI.CharacterSelectMenu;

namespace Morta4P
{
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
}
