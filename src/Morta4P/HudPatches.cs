using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using Zyklus.Managers;
using Zyklus.Player;
using Zyklus.UI;

namespace Morta4P
{
    // Online, the big HUD only shows the local player. The partner is shown by a single
    // OnlinePlayerHUD singleton that NetworkHUDManager points at whichever remote player
    // initialized last. With up to three remote players, one OnlinePlayerHUD per remote
    // player is needed: slot 0 is the original, the others are clones stacked beside it.
    internal static class HudStack
    {
        private static readonly AccessTools.FieldRef<OnlinePlayerHUD, Canvas> CanvasField =
            AccessTools.FieldRefAccess<OnlinePlayerHUD, Canvas>("canvas_");

        private static readonly System.Reflection.MethodInfo SingletonSetter =
            AccessTools.PropertySetter(typeof(OnlinePlayerHUD), nameof(OnlinePlayerHUD.sSingleton));

        private const int Slots = 3;

        private static OnlinePlayerHUD original_;
        private static readonly OnlinePlayerHUD[] huds_ = new OnlinePlayerHUD[Slots];
        private static readonly PlayerBase[] owners_ = new PlayerBase[Slots];
        private static readonly bool[] preview_ = new bool[Slots];
        private static bool creating_;

        internal static bool IsCreatingClone => creating_;

        private static bool EnsureOriginal()
        {
            var current = OnlinePlayerHUD.sSingleton;
            if (current == null)
            {
                return false;
            }
            if (original_ != current && !creating_)
            {
                // New scene / new HUD prefab instance: forget the old clones.
                original_ = current;
                for (int i = 0; i < Slots; i++)
                {
                    huds_[i] = i == 0 ? current : null;
                    owners_[i] = null;
                    preview_[i] = false;
                }
                LogRect("OnlinePlayerHUD", GetLayoutRect(current));
            }
            return original_ != null;
        }

        private static OnlinePlayerHUD GetHud(int slot)
        {
            if (huds_[slot] != null)
            {
                return huds_[slot];
            }

            creating_ = true;
            try
            {
                var go = Object.Instantiate(original_.gameObject, original_.transform.parent);
                go.name = original_.gameObject.name + "_Morta4P_" + slot;
                huds_[slot] = go.GetComponent<OnlinePlayerHUD>();
            }
            finally
            {
                // Awake on the clone overwrote the singleton.
                SingletonSetter.Invoke(null, new object[] { original_ });
                creating_ = false;
            }
            Place(slot);
            huds_[slot].ShowHUD(original_.pIsVisible);
            return huds_[slot];
        }

        internal static void Relayout()
        {
            for (int i = 1; i < Slots; i++)
            {
                if (huds_[i] != null)
                {
                    Place(i);
                }
            }
        }

        private static void Place(int slot)
        {
            RectTransform source = GetLayoutRect(original_);
            RectTransform target = GetLayoutRect(huds_[slot]);
            if (source == null || target == null)
            {
                return;
            }

            float h = source.rect.height * source.localScale.y;
            float w = source.rect.width * source.localScale.x;
            float gap = Plugin.HudSpacing.Value;
            Vector3 offset;
            switch (ResolveDirection(source))
            {
                case HudDirection.Up:
                    offset = new Vector3(0f, h + gap, 0f);
                    break;
                case HudDirection.Right:
                    offset = new Vector3(w + gap, 0f, 0f);
                    break;
                case HudDirection.Left:
                    offset = new Vector3(-(w + gap), 0f, 0f);
                    break;
                default:
                    offset = new Vector3(0f, -(h + gap), 0f);
                    break;
            }
            target.localPosition = source.localPosition + offset * slot;
        }

        private static HudDirection ResolveDirection(RectTransform rect)
        {
            var direction = Plugin.HudStackDirection.Value;
            if (direction != HudDirection.Auto)
            {
                return direction;
            }
            Canvas canvas = rect.GetComponentInParent<Canvas>();
            Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(cam, rect.position);
            return screen.y > Screen.height * 0.5f ? HudDirection.Down : HudDirection.Up;
        }

        private static readonly string[] PartFields = { "avatar_", "user_name_", "hp_bar_", "status_" };

        // The HUD lives under a full-screen rect ("Main"), so the thing to move is the
        // smallest rect that contains all of its visible parts (avatar, name, bars).
        private static RectTransform GetLayoutRect(OnlinePlayerHUD hud)
        {
            var parts = new List<Transform>();
            foreach (var name in PartFields)
            {
                if (Traverse.Create(hud).Field(name).GetValue() is Component c && c != null)
                {
                    parts.Add(c.transform);
                }
            }
            if (parts.Count == 0)
            {
                return hud.transform as RectTransform;
            }

            Transform common = parts[0];
            while (common != null && !parts.TrueForAll(p => p.IsChildOf(common)))
            {
                common = common.parent;
            }
            if (common == null || !common.IsChildOf(hud.transform))
            {
                return hud.transform as RectTransform;
            }

            // If the parts are spread over several sibling rects, step up while the rect is
            // still clearly smaller than the screen-sized root.
            var root = hud.transform as RectTransform;
            var rect = common as RectTransform;
            while (rect != null && rect.parent is RectTransform parent && parent != root
                   && parent.rect.width < root.rect.width * 0.6f && parent.rect.height < root.rect.height * 0.6f)
            {
                rect = parent;
            }
            return rect ?? root;
        }

        private static void LogRect(string label, RectTransform rt)
        {
            if (rt != null)
            {
                Plugin.Log.LogInfo($"{label}: rect={rt.name} pos={rt.localPosition} size={rt.rect.size} pivot={rt.pivot} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} scale={rt.localScale}");
            }
        }

        private static int FindSlot(PlayerBase player)
        {
            for (int i = 0; i < Slots; i++)
            {
                if (owners_[i] == player)
                {
                    return i;
                }
            }
            return -1;
        }

        internal static void Init(PlayerBase player)
        {
            if (!EnsureOriginal())
            {
                return;
            }
            int slot = FindSlot(player);
            if (slot < 0)
            {
                for (int i = 0; i < Slots && slot < 0; i++)
                {
                    if (owners_[i] == null || preview_[i])
                    {
                        if (preview_[i])
                        {
                            GetHud(i).ClearHUDEntity();
                            preview_[i] = false;
                        }
                        slot = i;
                    }
                }
            }
            if (slot < 0)
            {
                Plugin.Log.LogWarning($"No free online HUD slot for {player.name}");
                return;
            }

            owners_[slot] = player;
            GetHud(slot).SetHUDEntity(MakeEntity(player));
            Plugin.Log.LogInfo($"Online HUD slot {slot} -> {player.pAvatarData.pCharacter}");
        }

        internal static void Release(PlayerBase player)
        {
            if (!EnsureOriginal())
            {
                return;
            }
            int slot = FindSlot(player);
            if (slot < 0)
            {
                return;
            }
            GetHud(slot).ClearHUDEntity();
            owners_[slot] = null;
            preview_[slot] = false;
        }

        internal static void PropagateVisibility(OnlinePlayerHUD source, bool show)
        {
            if (creating_ || source != original_)
            {
                return;
            }
            for (int i = 1; i < Slots; i++)
            {
                if (huds_[i] != null)
                {
                    huds_[i].ShowHUD(show);
                }
            }
        }

        // Debug: fill the empty slots with the local player to check placement solo.
        internal static void TogglePreview()
        {
            if (!EnsureOriginal())
            {
                Plugin.Log.LogInfo("HUD preview: no OnlinePlayerHUD in this scene");
                return;
            }
            bool any = false;
            for (int i = 0; i < Slots; i++)
            {
                any |= preview_[i];
            }

            if (any)
            {
                for (int i = 0; i < Slots; i++)
                {
                    if (preview_[i])
                    {
                        GetHud(i).ClearHUDEntity();
                        owners_[i] = null;
                        preview_[i] = false;
                    }
                }
                Plugin.Log.LogInfo("HUD preview off");
                return;
            }

            var locals = PlayerManager.sSingleton?.GetLocalPlayers();
            if (locals == null || locals.Count == 0)
            {
                Plugin.Log.LogInfo("HUD preview: no local player (start a run first)");
                return;
            }
            for (int i = 0; i < Slots; i++)
            {
                if (owners_[i] == null)
                {
                    var hud = GetHud(i);
                    hud.SetHUDEntity(MakeEntity(locals[0]));
                    hud.ShowHUD(true);
                    preview_[i] = true;
                }
            }
            Plugin.Log.LogInfo("HUD preview on");
        }

        private static HUDEntity MakeEntity(PlayerBase player)
        {
            return new HUDEntity
            {
                local_number_ = player.pAvatarData.pLocalNumberOnOwner,
                user_ = player.pAvatarData.pUser,
                character_ = player.pAvatarData.pCharacter,
                player_ = player
            };
        }
    }

    public enum HudDirection
    {
        Auto,
        Down,
        Up,
        Right,
        Left
    }

    [HarmonyPatch(typeof(NetworkHUDManager), nameof(NetworkHUDManager.InitNonLocalPlayerHUD))]
    internal static class NetworkHUDManager_InitNonLocalPlayerHUD
    {
        private static bool Prefix(PlayerBase player)
        {
            HudStack.Init(player);
            return false;
        }
    }

    [HarmonyPatch(typeof(NetworkHUDManager), nameof(NetworkHUDManager.ReleaseNonLocalPlayerHUD))]
    internal static class NetworkHUDManager_ReleaseNonLocalPlayerHUD
    {
        private static bool Prefix(PlayerBase player)
        {
            HudStack.Release(player);
            return false;
        }
    }

    [HarmonyPatch(typeof(OnlinePlayerHUD), nameof(OnlinePlayerHUD.ShowHUD))]
    internal static class OnlinePlayerHUD_ShowHUD
    {
        private static void Postfix(OnlinePlayerHUD __instance, bool show)
        {
            HudStack.PropagateVisibility(__instance, show);
        }
    }
}
