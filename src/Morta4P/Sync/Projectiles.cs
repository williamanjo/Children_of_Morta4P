using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Altar.Networking;
using HarmonyLib;
using Zyklus;
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
    // network spawn skips the owner, who already shows its predicted copy. Later commands
    // for that object (hit RMCs, its release) skip the owner too: the owner never had it, and
    // each one logged an error there. Lucy's rapid primary fire made that a stream of errors
    // and the owner lagged.
    internal static class ProjectileOwner
    {
        [ThreadStatic]
        internal static NetIdentityBase Excluded;

        // Network instance id of each projectile spawned without its owner -> that owner.
        private static readonly Dictionary<ushort, NetIdentityBase> instances_ = new Dictionary<ushort, NetIdentityBase>();

        // The client a host broadcast must skip, or null.
        internal static NetIdentityBase SkipFor(NetCommand command)
        {
            switch (command)
            {
                case InstantiateNetCommand spawn:
                    if (Excluded != null)
                    {
                        instances_[spawn.pNetInstanceID] = Excluded;
                    }
                    else
                    {
                        instances_.Remove(spawn.pNetInstanceID); // id reused by an object everyone gets
                    }
                    return Excluded;
                case RMCNetCommand rmc:
                    return OwnerOf(rmc.pReceiverAddress) ?? Excluded;
                case ReleaseNetCommand release:
                    NetIdentityBase owner = OwnerOf(release.pAddress);
                    if (owner != null)
                    {
                        instances_.Remove(release.pAddress.pNetGOInstanceID);
                    }
                    return owner ?? Excluded;
                default:
                    return Excluded;
            }
        }

        private static NetIdentityBase OwnerOf(NetGOAddress address) =>
            address != null && address.pMode == NetGOAddress.Mode.Dynamic && instances_.TryGetValue(address.pNetGOInstanceID, out NetIdentityBase owner)
                ? owner
                : null;

        internal static void Clear() => instances_.Clear();
    }

    [HarmonyPatch(typeof(NetManager), nameof(NetManager.ResetInstanceID))]
    internal static class NetManager_ResetInstanceID
    {
        private static void Postfix() => ProjectileOwner.Clear();
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
}
