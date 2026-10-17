using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using HarmonyLib;
using Talents;
using Zyklus;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;

namespace Morta4P
{
    // Messages the host sent to "the client" (a broadcast, fine with one client) that belong
    // to one client, and client messages the host never forwarded to the other clients.

    internal static class OwnerSend
    {
        // Target of the broadcast being redirected; set by each patch's prefix.
        [ThreadStatic]
        internal static NetIdentityBase Target;

        private static MethodInfo Broadcast(int parameters) =>
            AccessTools.GetDeclaredMethods(typeof(IRMCBridgeExtensions)).First(m =>
                m.Name == nameof(IRMCBridgeExtensions.SendRMCToSlaves)
                && m.GetParameters().Length == parameters
                && m.GetParameters()[1].ParameterType == typeof(RMCHandler.ErrorCondition));

        // Replaces the first SendRMCToSlaves(bridge, condition, component, id[, p0]) call with a
        // send to Target only.
        internal static IEnumerable<CodeInstruction> RedirectFirstBroadcast(IEnumerable<CodeInstruction> instructions, int parameters, string method)
        {
            MethodInfo broadcast = Broadcast(parameters);
            MethodInfo replacement = AccessTools.Method(typeof(OwnerSend), parameters == 4 ? nameof(SendToTarget0) : nameof(SendToTarget1));
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(broadcast))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call, replacement).MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError($"{method}: broadcast not found");
            }
        }

        private static void SendToTarget0(IRMCBridge bridge, RMCHandler.ErrorCondition condition, IRemoteMethodContainer component, byte id)
        {
            if (Target != null)
            {
                bridge.SendRMCToSlave(Target, condition, component, id);
            }
            else
            {
                bridge.SendRMCToSlaves(condition, component, id);
            }
        }

        private static void SendToTarget1(IRMCBridge bridge, RMCHandler.ErrorCondition condition, IRemoteMethodContainer component, byte id, object p0)
        {
            if (Target != null)
            {
                bridge.SendRMCToSlave(Target, condition, component, id, p0);
            }
            else
            {
                bridge.SendRMCToSlaves(condition, component, id, p0);
            }
        }

        internal static NetIdentityBase OwnerOf(PlayerNumber number)
        {
            try
            {
                return PlayerManager.sSingleton.GetPlayerClientIdentity(number);
            }
            catch (Exception)
            {
                return null;
            }
        }

        internal static IEnumerable<NetIdentityBase> OtherSlaves(NetIdentityBase except)
        {
            foreach (NetIdentityBase slave in LobbyManager.sSingleton.pSlaveClientsIdentity.ToArray())
            {
                if (slave != null && !slave.Equals(except))
                {
                    yield return slave;
                }
            }
        }
    }

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

    // A client's cursor in character select reached the host but not the other clients.
    [HarmonyPatch(typeof(CharacterSelect), nameof(CharacterSelect.NET_Highlight_Button))]
    internal static class CharacterSelect_NET_Highlight_Button
    {
        private static void Postfix(CharacterSelect __instance, PlayerNumber sender, int button_index, bool highlight,
            bool change_highlight_stroke, bool force)
        {
            try
            {
                Client from = LobbyStatus.CurrentSender;
                if (from?.pID == null || !LobbyManager.sSingleton.pIsMaster || __instance.IsPlayerLocal(sender))
                {
                    return;
                }
                foreach (NetIdentityBase slave in OwnerSend.OtherSlaves(from.pID))
                {
                    __instance.pRMCBridge.SendRMCToSlave(slave, RMCHandler.ErrorCondition.All, __instance, 3,
                        sender, button_index, highlight, change_highlight_stroke, force);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Highlight relay: {e}");
            }
        }
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

    // One flag tracked "the client's controller is disconnected"; with several clients one
    // reconnecting cleared the pause while another was still disconnected.
    internal static class ControllerState
    {
        internal static readonly HashSet<NetIdentityBase> Disconnected = new HashSet<NetIdentityBase>();
    }

    [HarmonyPatch(typeof(General), nameof(General.SlaveControllerDisconnected))]
    internal static class General_SlaveControllerDisconnected
    {
        private static bool Prefix()
        {
            NetIdentityBase id = LobbyStatus.CurrentSender?.pID;
            if (id == null || !LobbyManager.sSingleton.pIsMaster)
            {
                return true;
            }
            bool first = ControllerState.Disconnected.Count == 0;
            ControllerState.Disconnected.Add(id);
            return first;
        }
    }

    [HarmonyPatch(typeof(General), nameof(General.SlaveControllerReconnected))]
    internal static class General_SlaveControllerReconnected
    {
        private static bool Prefix()
        {
            NetIdentityBase id = LobbyStatus.CurrentSender?.pID;
            if (id == null || !LobbyManager.sSingleton.pIsMaster)
            {
                return true;
            }
            ControllerState.Disconnected.Remove(id);
            return ControllerState.Disconnected.Count == 0;
        }
    }

    // Family stats answering one client's save/endless fetch went to every client; a client
    // in the middle of its own fetch could complete it with someone else's answer.
    [HarmonyPatch(typeof(ProfileClientService), nameof(ProfileClientService.SetFamilyStatsFromHost))]
    internal static class ProfileClientService_SetFamilyStatsFromHost
    {
        private static bool Prefix(ProfileClientService __instance, object host_family_stats)
        {
            if (!LobbyManager.sSingleton.pIsMaster || __instance.pSlave == null
                || (!ProfileClientService_FetchHostSaveData.Running && LobbyStatus.CurrentSender == null))
            {
                return true;
            }
            __instance.pRMCBridge.SendRMCToSlave(__instance.pSlave.pSlaveIndex, RMCHandler.ErrorCondition.All, __instance, 29, host_family_stats);
            return false;
        }
    }
}
