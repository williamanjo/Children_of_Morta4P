using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;

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
            MethodInfo replacement = AccessTools.Method(typeof(OwnerSend), "SendToTarget" + (parameters - 4));
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

        private static void SendToTarget2(IRMCBridge bridge, RMCHandler.ErrorCondition condition, IRemoteMethodContainer component, byte id, object p0, object p1)
        {
            if (Target != null)
            {
                bridge.SendRMCToSlave(Target, condition, component, id, p0, p1);
            }
            else
            {
                bridge.SendRMCToSlaves(condition, component, id, p0, p1);
            }
        }

        private static void SendToTarget3(IRMCBridge bridge, RMCHandler.ErrorCondition condition, IRemoteMethodContainer component, byte id, object p0, object p1, object p2)
        {
            if (Target != null)
            {
                bridge.SendRMCToSlave(Target, condition, component, id, p0, p1, p2);
            }
            else
            {
                bridge.SendRMCToSlaves(condition, component, id, p0, p1, p2);
            }
        }

        internal static NetIdentityBase OwnerOf(PlayerBase player)
        {
            try
            {
                return player == null ? null : PlayerManager.sSingleton.GetPlayerClientIdentity(PlayerManager.sSingleton.GetPlayerIndex(player));
            }
            catch (Exception)
            {
                return null;
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

    internal static class OtherSlaves
    {
        internal static void ForEach(Action<ProfileClientService> action)
        {
            if (!FirstSlaveIL.IsMasterOnline())
            {
                return;
            }
            List<SlaveClient> slaves = LobbyManager.sSingleton.pSlaveClients;
            for (int i = 1; i < slaves.Count; i++)
            {
                try
                {
                    if (slaves[i]?.pProfileService != null)
                    {
                        action(slaves[i].pProfileService);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Profile sync to client {i}: {e}");
                }
            }
        }
    }

    internal static class FirstSlaveIL
    {
        // LobbyManager.sSingleton is a static field.
        private static readonly FieldInfo LobbySingleton = AccessTools.Field(typeof(LobbyManager), nameof(LobbyManager.sSingleton));

        private static readonly MethodInfo SlaveClients =
            AccessTools.PropertyGetter(typeof(LobbyManager), nameof(LobbyManager.pSlaveClients));

        private static readonly MethodInfo GetItem =
            AccessTools.Method(typeof(List<SlaveClient>), "get_Item");

        private static readonly MethodInfo ServiceSlave =
            AccessTools.PropertyGetter(typeof(ProfileClientService), nameof(ProfileClientService.pSlave));

        // Rewrites "LobbyManager.sSingleton.pSlaveClients[0]" into "this.pSlave".
        internal static IEnumerable<CodeInstruction> UseOwnSlave(IEnumerable<CodeInstruction> instructions, string method)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i + 3 < codes.Count; i++)
            {
                if (codes[i].LoadsField(LobbySingleton) && codes[i + 1].Calls(SlaveClients)
                    && codes[i + 2].LoadsConstant(0) && codes[i + 3].Calls(GetItem))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(codes[i]);
                    codes[i] = load;
                    codes[i + 1] = new CodeInstruction(OpCodes.Call, ServiceSlave).MoveLabelsFrom(codes[i + 1]);
                    codes[i + 2] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(codes[i + 2]);
                    codes[i + 3] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(codes[i + 3]);
                    replaced++;
                }
            }
            if (replaced == 0)
            {
                Plugin.Log.LogError($"{method}: pSlaveClients[0] not found");
            }
            return codes;
        }

        internal static bool IsMasterOnline() =>
            LobbyManager.sSingleton != null && LobbyManager.sSingleton.pIsMaster && LobbyManager.sSingleton.pSlaveClientsCount > 0;
    }
}
