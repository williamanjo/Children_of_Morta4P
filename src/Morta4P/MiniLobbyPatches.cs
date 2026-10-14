using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using Zyklus;
using Zyklus.Managers;
using Zyklus.UI;

namespace Morta4P
{
    // The lobby widget (pause menu / slot menu) has a single MiniLobbyPlayer entry and
    // shows the first remote player only. Two more entries are cloned from it and stacked
    // under it, one per extra remote player.
    internal static class MiniLobbyWidget
    {
        private static readonly AccessTools.FieldRef<MiniLobby, MiniLobbyPlayer> OtherPlayerField =
            AccessTools.FieldRefAccess<MiniLobby, MiniLobbyPlayer>("other_player_");

        private static readonly AccessTools.FieldRef<MiniLobby, CanvasGroup> CanvasGroupField =
            AccessTools.FieldRefAccess<MiniLobby, CanvasGroup>("cg_");

        private const int Extras = 2;

        private static MiniLobby lobby_;
        private static readonly MiniLobbyPlayer[] extra_ = new MiniLobbyPlayer[Extras];
        private static readonly int[] extraIndex_ = { -1, -1 };
        private static int firstIndex_ = -1;
        private static bool preview_;

        // Last status key per player index, re-applied when an entry is (re)assigned.
        private static readonly Dictionary<int, string> statusKeys_ = new Dictionary<int, string>();

        private static bool Ensure(MiniLobby lobby)
        {
            if (lobby == null)
            {
                return false;
            }
            if (lobby_ == lobby && extra_[0] != null)
            {
                return true;
            }

            MiniLobbyPlayer original = OtherPlayerField(lobby);
            if (original == null)
            {
                return false;
            }
            lobby_ = lobby;
            firstIndex_ = -1;

            var source = (RectTransform)original.transform;
            bool autoLayout = source.parent != null && source.parent.GetComponent<LayoutGroup>() != null;
            for (int k = 0; k < Extras; k++)
            {
                var go = UnityEngine.Object.Instantiate(original.gameObject, source.parent);
                go.name = original.gameObject.name + "_Morta4P_" + (k + 2);
                extra_[k] = go.GetComponent<MiniLobbyPlayer>();
                extraIndex_[k] = -1;
                if (!autoLayout)
                {
                    float h = source.rect.height * source.localScale.y;
                    go.transform.localPosition = source.localPosition
                        + new Vector3(0f, -(h + Plugin.LobbySpacing.Value) * (k + 1), 0f);
                }
                extra_[k].Show(show: false, is_joystick: false);
            }
            Plugin.Log.LogInfo($"MiniLobby entry: pos={source.localPosition} size={source.rect.size} pivot={source.pivot} autoLayout={autoLayout}");
            return true;
        }

        // Runs after MiniLobby.OnActiveCapsuleChanged, which already filled the first entry.
        internal static void Refresh(MiniLobby lobby, PlayersCapsule capsule)
        {
            if (!Ensure(lobby))
            {
                return;
            }

            var remotes = new List<int>();
            for (int i = 0; i < capsule.pPlayers.Count; i++)
            {
                if (!capsule.pPlayers[i].pUser.CheckIsLocal())
                {
                    remotes.Add(i);
                }
            }

            firstIndex_ = remotes.Count > 0 ? remotes[0] : -1;
            if (firstIndex_ >= 0 && statusKeys_.TryGetValue(firstIndex_, out var firstKey))
            {
                OtherPlayerField(lobby).SetStatus(firstKey);
            }

            for (int k = 0; k < Extras; k++)
            {
                if (remotes.Count > k + 1)
                {
                    int index = remotes[k + 1];
                    extraIndex_[k] = index;
                    extra_[k].UpdateData(capsule.pPlayers[index]);
                    if (statusKeys_.TryGetValue(index, out var key))
                    {
                        extra_[k].SetStatus(key);
                    }
                }
                else
                {
                    extraIndex_[k] = -1;
                    extra_[k].Show(show: false, is_joystick: false);
                }
            }
        }

        internal static void HideExtras()
        {
            for (int k = 0; k < Extras; k++)
            {
                if (extra_[k] != null)
                {
                    extra_[k].Show(show: false, is_joystick: false);
                    extraIndex_[k] = -1;
                }
            }
        }

        internal static void CloseSubMenus()
        {
            foreach (var entry in extra_)
            {
                if (entry != null)
                {
                    entry.OpenSubMenu(open: false);
                }
            }
        }

        internal static void SetStatus(MiniLobby lobby, int playerIndex, string key)
        {
            statusKeys_[playerIndex] = key;
            if (!Ensure(lobby))
            {
                return;
            }
            if (playerIndex == firstIndex_ || firstIndex_ < 0)
            {
                OtherPlayerField(lobby).SetStatus(key);
                return;
            }
            for (int k = 0; k < Extras; k++)
            {
                if (extraIndex_[k] == playerIndex)
                {
                    extra_[k].SetStatus(key);
                }
            }
        }

        // Debug: fill every entry with your own profile to check the layout solo.
        internal static void TogglePreview()
        {
            MiniLobby lobby = MiniLobby.sSingleton;
            if (!Ensure(lobby))
            {
                return;
            }
            preview_ = !preview_;
            var capsule = PlayerManager.sSingleton?.GetActiveCapsule();
            if (preview_)
            {
                CanvasGroup cg = CanvasGroupField(lobby);
                if (cg == null || cg.alpha <= 0f || capsule == null || capsule.pPlayers.Count == 0)
                {
                    preview_ = false;
                    return;
                }
                OtherPlayerField(lobby).UpdateData(capsule.pPlayers[0]);
                foreach (var entry in extra_)
                {
                    entry.UpdateData(capsule.pPlayers[0]);
                }
                Plugin.Log.LogInfo("MiniLobby preview on");
            }
            else
            {
                if (firstIndex_ < 0)
                {
                    OtherPlayerField(lobby).Show(show: false, is_joystick: false);
                }
                HideExtras();
                if (capsule != null)
                {
                    Refresh(lobby, capsule);
                }
                Plugin.Log.LogInfo("MiniLobby preview off");
            }
        }
    }

    [HarmonyPatch(typeof(MiniLobby), "OnActiveCapsuleChanged")]
    internal static class MiniLobby_OnActiveCapsuleChanged
    {
        private static void Postfix(MiniLobby __instance, PlayersCapsule active_capsule)
        {
            try
            {
                MiniLobbyWidget.Refresh(__instance, active_capsule);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MiniLobby refresh: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(MiniLobby), "RemoveOtherPlayer")]
    internal static class MiniLobby_RemoveOtherPlayer
    {
        private static void Postfix()
        {
            try
            {
                MiniLobbyWidget.HideExtras();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MiniLobby hide: {e}");
            }
        }
    }

    [HarmonyPatch(typeof(MiniLobby), nameof(MiniLobby.CloseAllSubMenus))]
    internal static class MiniLobby_CloseAllSubMenus
    {
        private static void Postfix()
        {
            try
            {
                MiniLobbyWidget.CloseSubMenus();
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"MiniLobby submenus: {e}");
            }
        }
    }
}
