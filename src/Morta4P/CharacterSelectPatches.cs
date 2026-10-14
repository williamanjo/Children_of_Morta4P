using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;
using Zyklus.UI.CharacterSelectMenu;

namespace Morta4P
{
    // The character select screen was written for exactly two players and assumes the
    // remote client is always P2. These patches make it use the real player number.
    internal static class CharacterSelectUtil
    {
        internal static bool TryGetLocalPlayerNumber(out PlayerNumber number)
        {
            var manager = PlayerManager.sSingleton;
            if (manager != null && manager.pLocalPlayerNumbers != null && manager.pLocalPlayerNumbers.Count > 0)
            {
                number = manager.pLocalPlayerNumbers[0];
                return true;
            }
            number = PlayerNumber.None;
            return false;
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.GetIsPlayerLocal))]
    internal static class CharacterSelect_GetIsPlayerLocal
    {
        private static void Postfix(CharacterSelect __instance, PlayerNumber player_number, ref bool __result)
        {
            if (__instance.pCurrentMode != CharacterSelect.Mode.Slave)
            {
                return;
            }
            var manager = PlayerManager.sSingleton;
            if (manager?.pLocalPlayerNumbers != null)
            {
                __result = manager.pLocalPlayerNumbers.Contains(player_number);
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.GetPlayerNumber))]
    internal static class CharacterSelect_GetPlayerNumber
    {
        private static void Postfix(CharacterSelect __instance, ref PlayerNumber __result)
        {
            if (__instance.pCurrentMode == CharacterSelect.Mode.Slave
                && CharacterSelectUtil.TryGetLocalPlayerNumber(out var local))
            {
                __result = local;
            }
        }
    }

    // The cancel button handler picks the deselecting player with
    // "current_mode_ != Mode.Slave ? P1 : P2", so a P3/P4 client would cancel P2's pick.
    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.MenuActiveUpdateOncePerFrame))]
    internal static class CharacterSelect_MenuActiveUpdateOncePerFrame
    {
        private static readonly System.Reflection.FieldInfo ModeField =
            AccessTools.Field(typeof(CharacterSelect), "current_mode_");

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int slave = (int)CharacterSelect.Mode.Slave;
            int p2 = (int)PlayerNumber.P2;
            for (int i = 0; i + 4 < codes.Count; i++)
            {
                // ldfld current_mode_ ; ldc.i4 Slave ; branch ; ldc.i4 P2 ...
                if (!codes[i].LoadsField(ModeField) || !codes[i + 1].LoadsConstant(slave) || !codes[i + 2].Branches(out _))
                {
                    continue;
                }
                for (int j = i + 3; j < i + 6 && j < codes.Count; j++)
                {
                    if (codes[j].LoadsConstant(p2))
                    {
                        codes[j] = new CodeInstruction(OpCodes.Call,
                            AccessTools.Method(typeof(CharacterSelect_MenuActiveUpdateOncePerFrame), nameof(SlavePlayerNumber)))
                            .MoveLabelsFrom(codes[j]);
                        return codes;
                    }
                }
            }
            Plugin.Log.LogError("MenuActiveUpdateOncePerFrame: P2 cancel literal not found, clients P3/P4 may cancel P2's pick");
            return codes;
        }

        private static PlayerNumber SlavePlayerNumber()
        {
            return CharacterSelectUtil.TryGetLocalPlayerNumber(out var local) ? local : PlayerNumber.P2;
        }
    }

    // Host may keep inviting while there is room.
    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.CanInvite))]
    internal static class CharacterSelect_CanInvite
    {
        private static void Postfix(CharacterSelect __instance, ref bool __result)
        {
            if (__result)
            {
                return;
            }
            var lobby = LobbyManager.sSingleton;
            var mode = __instance.GetMode(PlayerManager.sSingleton.GetActiveCapsule());
            if (mode == CharacterSelect.Mode.MasterOnlineCoop && lobby.pSlaveClientsCount < Plugin.MaxSlaves)
            {
                __result = true;
            }
        }
    }

    // Original only waits for P1 and P2.
    [HarmonyPatch(typeof(CharacterSelect), "CheckAllPlayersReady")]
    internal static class CharacterSelect_CheckAllPlayersReady
    {
        private static void Postfix(CharacterSelect __instance, ref bool __result)
        {
            if (!__result || __instance.pCurrentMode != CharacterSelect.Mode.MasterOnlineCoop)
            {
                return;
            }
            var capsule = PlayerManager.sSingleton.GetActiveCapsule();
            for (int i = 0; i < capsule.pPlayers.Count; i++)
            {
                if (capsule.pPlayers[i].pCharacter == PlayerCharacterEnum.NO_ONE)
                {
                    __result = false;
                    return;
                }
            }
        }
    }

    // With 3-4 players the save may not have enough characters unlocked to give everyone one.
    // Only the character select buttons are unlocked: LocalProfile.IsCharacterUnlocked stays
    // untouched, since the story uses it to trigger each character's unlock event.
    internal static class StarterCharacters
    {
        internal static readonly PlayerCharacterEnum[] Characters =
        {
            PlayerCharacterEnum.John,
            PlayerCharacterEnum.Linda,
            PlayerCharacterEnum.Mark,
            PlayerCharacterEnum.Kevin,
        };

        private static readonly AccessTools.FieldRef<CharacterSelect, CharacterButton[]> ButtonsField =
            AccessTools.FieldRefAccess<CharacterSelect, CharacterButton[]>("buttons_");

        internal static bool Active()
        {
            if (!Plugin.UnlockStarterCharacters.Value
                || LobbyManager.sSingleton.pLobbyMode != LobbyManager.LobbyMode.OnlineCoop)
            {
                return false;
            }
            return (PlayerManager.sSingleton?.GetActiveCapsule()?.pPlayerscount ?? 0) > 2;
        }

        internal static void Apply(CharacterSelect menu, bool initial)
        {
            if (!Active())
            {
                return;
            }
            CharacterButton[] buttons = ButtonsField(menu);
            if (buttons == null)
            {
                return;
            }
            foreach (var button in buttons)
            {
                if (button == null || System.Array.IndexOf(Characters, button.pCharacter) < 0)
                {
                    continue;
                }
                if (initial)
                {
                    button.SetInitialLockState(unlock: true);
                }
                else if (button.IsLocked())
                {
                    button.Unlock();
                }
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), "SetInitialCharactersLockStates")]
    internal static class CharacterSelect_SetInitialCharactersLockStates
    {
        private static void Postfix(CharacterSelect __instance)
        {
            try
            {
                StarterCharacters.Apply(__instance, initial: true);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Starter characters (initial): {e}");
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), "HandleCharactersLockStatus")]
    internal static class CharacterSelect_HandleCharactersLockStatus
    {
        private static void Postfix(CharacterSelect __instance)
        {
            try
            {
                StarterCharacters.Apply(__instance, initial: false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Starter characters: {e}");
            }
        }
    }
}
