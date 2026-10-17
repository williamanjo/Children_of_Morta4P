using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Altar.Networking;
using HarmonyLib;
using Zyklus;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;

namespace Morta4P
{
    // Projectiles of a client's player (Linda's arrows, ...) are fired with
    // local_prediction = !pIsMasterOwner: the owner spawns its own copy and the host spawns
    // a local-only copy to resolve hits. Nothing was sent over the network, so other clients
    // never saw them.
    //
    // On the host with two or more clients, those shots are spawned networked, and the
    // network spawn skips the owner, who already shows its predicted copy.
    internal static class ProjectileOwner
    {
        [ThreadStatic]
        internal static NetIdentityBase Excluded;
    }

    [HarmonyPatch]
    internal static class RangeWeaponComponent_Shoot
    {
        private static readonly AccessTools.FieldRef<RangeWeaponComponent, PlayerBase> PlayerField =
            AccessTools.FieldRefAccess<RangeWeaponComponent, PlayerBase>("player_");

        private static IEnumerable<MethodBase> TargetMethods() =>
            AccessTools.GetDeclaredMethods(typeof(RangeWeaponComponent))
                .Where(m => !m.IsStatic && m.GetParameters().Any(p => p.Name == "local_prediction" && p.ParameterType == typeof(bool)));

        private static void Prefix(RangeWeaponComponent __instance, ref bool local_prediction, ref bool __state)
        {
            __state = false;
            if (!local_prediction)
            {
                return;
            }
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
            NetIdentityBase owner = OwnerSend.OwnerOf(player);
            if (owner == null)
            {
                return;
            }
            local_prediction = false;
            ProjectileOwner.Excluded = owner;
            __state = true;
        }

        private static Exception Finalizer(Exception __exception, bool __state)
        {
            if (__state)
            {
                ProjectileOwner.Excluded = null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.QueueCommandOnSlaves), new[] { typeof(NetCommand) })]
    internal static class LobbyManager_QueueCommandOnSlaves
    {
        private static bool Prefix(LobbyManager __instance, NetCommand command)
        {
            EffectRelay.BroadcastCount++;
            NetIdentityBase excluded = ProjectileOwner.Excluded;
            if (excluded == null)
            {
                return true;
            }
            foreach (SlaveClient slave in __instance.pSlaveClients.ToArray())
            {
                if (slave != null && slave.pID != excluded)
                {
                    slave.pGenericCommandService.QueueWaitForSendCommand(command);
                }
            }
            return false;
        }
    }
}
