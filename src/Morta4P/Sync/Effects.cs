using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Altar.Networking;
using HarmonyLib;
using Zyklus.Network;
using Zyklus.Player;

namespace Morta4P
{
    // Character effects (weapon trails such as the red slash, ability VFX, ...) are remote
    // methods on the *NetActions components. Many only broadcast when the player belongs to
    // the host ("if (pIsMaster && local)" / "if (pIsMasterOwner)"). For a client's player the
    // host played them locally and sent nothing: with one client that was enough (the owner
    // plays its own), with more the other clients saw the animation without the effect.
    //
    // On the host with two or more clients, when such a method runs for a client's player and
    // the original sent nothing, the same remote call is sent to every client except the
    // owner. Methods that already broadcast are left alone.
    internal static class EffectRelay
    {
        // Incremented for every host broadcast (LobbyManager.QueueCommandOnSlaves).
        internal static int BroadcastCount;
    }

    [HarmonyPatch]
    internal static class NetActions_Relay
    {
        private static readonly AccessTools.FieldRef<PlayerNetActions, PlayerBase> PlayerField =
            AccessTools.FieldRefAccess<PlayerNetActions, PlayerBase>("player_");

        private static readonly Dictionary<MethodBase, byte> ids_ = new Dictionary<MethodBase, byte>();

        private static IEnumerable<MethodBase> TargetMethods()
        {
            var types = typeof(PlayerNetActions).Assembly.GetTypes()
                .Where(t => typeof(PlayerNetActions).IsAssignableFrom(t));
            foreach (Type type in types)
            {
                foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
                {
                    var attribute = method.GetCustomAttributes(typeof(RemoteMethod), false).FirstOrDefault() as RemoteMethod;
                    if (attribute == null || method.IsStatic || method.IsAbstract || method.GetParameters().Length > 5)
                    {
                        continue;
                    }
                    ids_[method] = attribute.pMethodID;
                    yield return method;
                }
            }
        }

        private static void Prefix(PlayerNetActions __instance, out int __state)
        {
            __state = -1;
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby == null || !lobby.pIsMaster || lobby.pSlaveClientsCount < 2)
            {
                return;
            }
            PlayerBase player = PlayerField(__instance);
            if (player == null || player.pLocalNumber.GetIsLocal() || player.pIsCompanion)
            {
                return;
            }
            __state = EffectRelay.BroadcastCount;
        }

        private static void Postfix(PlayerNetActions __instance, MethodBase __originalMethod, object[] __args, int __state)
        {
            if (__state < 0 || EffectRelay.BroadcastCount != __state || !ids_.TryGetValue(__originalMethod, out byte id))
            {
                return;
            }
            try
            {
                NetIdentityBase owner = OwnerSend.OwnerOf(PlayerField(__instance));
                IRMCBridge bridge = __instance.pRMCBridge;
                if (owner == null || bridge == null)
                {
                    return;
                }
                foreach (SlaveClient slave in LobbyManager.sSingleton.pSlaveClients.ToArray())
                {
                    if (slave == null || slave.pID == owner)
                    {
                        continue;
                    }
                    Send(bridge, slave.pSlaveIndex, __instance, id, __args);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Effect relay {__originalMethod.Name}: {e.Message}");
            }
        }

        private static void Send(IRMCBridge bridge, int slave, IRemoteMethodContainer component, byte id, object[] a)
        {
            const RMCHandler.ErrorCondition all = RMCHandler.ErrorCondition.All;
            switch (a.Length)
            {
                case 0: bridge.SendRMCToSlave(slave, all, component, id); break;
                case 1: bridge.SendRMCToSlave(slave, all, component, id, a[0]); break;
                case 2: bridge.SendRMCToSlave(slave, all, component, id, a[0], a[1]); break;
                case 3: bridge.SendRMCToSlave(slave, all, component, id, a[0], a[1], a[2]); break;
                case 4: bridge.SendRMCToSlave(slave, all, component, id, a[0], a[1], a[2], a[3]); break;
                case 5: bridge.SendRMCToSlave(slave, all, component, id, a[0], a[1], a[2], a[3], a[4]); break;
            }
        }
    }
}
