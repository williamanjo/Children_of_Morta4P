using System;
using System.Collections.Generic;
using Altar.Networking;
using HarmonyLib;
using Talents;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;

namespace Morta4P
{
    // A client's relic upgrade was sent to every client; the others added a foreign relic to
    // their upgrade menu (or threw with the menu closed). Send it to the buyer only.
    [HarmonyPatch(typeof(ItemUpgraderMenu), "UpgradeItem")]
    internal static class ItemUpgraderMenu_UpgradeItem
    {
        private static void Prefix(PlayerNumber player_number) => OwnerSend.Target = OwnerSend.OwnerOf(player_number);

        private static Exception Finalizer(Exception __exception)
        {
            OwnerSend.Target = null;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OwnerSend.RedirectFirstBroadcast(instructions, 5, "ItemUpgraderMenu.UpgradeItem");
    }

    // "X gained a skill point" for a client's character popped up on every client.
    [HarmonyPatch(typeof(PlayerBase), "PushSkillGainPopUp")]
    internal static class PlayerBase_PushSkillGainPopUp
    {
        private static void Prefix(PlayerBase __instance)
        {
            try
            {
                OwnerSend.Target = PlayerManager.sSingleton.GetPlayerClientIdentity(PlayerManager.sSingleton.GetPlayerIndex(__instance));
            }
            catch (Exception)
            {
                OwnerSend.Target = null;
            }
        }

        private static Exception Finalizer(Exception __exception)
        {
            OwnerSend.Target = null;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            OwnerSend.RedirectFirstBroadcast(instructions, 4, "PlayerBase.PushSkillGainPopUp");
    }

    // A client's talent reached the host only; the other clients never added it to that
    // client's player.
    [HarmonyPatch(typeof(PlayerTalentManager), nameof(PlayerTalentManager.LearnTalent), new[] { typeof(string) })]
    internal static class PlayerTalentManager_LearnTalent
    {
        private static void Postfix(PlayerTalentManager __instance, string guid)
        {
            try
            {
                Client from = LobbyStatus.CurrentSender;
                if (from?.pID == null || !LobbyManager.sSingleton.pIsMaster)
                {
                    return;
                }
                foreach (NetIdentityBase slave in OwnerSend.OtherSlaves(from.pID))
                {
                    __instance.pRMCBridge.SendRMCToSlave(slave, RMCHandler.ErrorCondition.All, __instance, 2, guid);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Talent relay: {e}");
            }
        }
    }
}
