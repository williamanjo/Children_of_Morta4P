using Altar.Networking;
using HarmonyLib;
using Zyklus.Network;

namespace Morta4P
{
    // Host broadcasts (LobbyManager.QueueCommandOnSlaves and its variants). Every send is
    // counted for the effect relay, and the broadcast skips clients that must not get a
    // command: the owner of a networked projectile, or everyone but the owner of a client
    // connection that is being released.

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.QueueCommandOnSlaves), new[] { typeof(NetCommand) })]
    internal static class LobbyManager_QueueCommandOnSlaves
    {
        private static bool Prefix(LobbyManager __instance, NetCommand command)
        {
            EffectRelay.BroadcastCount++;
            if (command is ReleaseNetCommand release && ClientAddress.Is(release.pAddress))
            {
                // Releasing a client's connection object (a client left): only that client has
                // it. The address loses its identity once the client is gone, and writing it
                // for the remaining clients threw every tick and left a hole in their command
                // stream, freezing the game for everyone.
                NetIdentityBase owner = release.pAddress.pSlaveIdentity;
                foreach (SlaveClient slave in __instance.pSlaveClients.ToArray())
                {
                    if (slave != null && owner != null && slave.pID == owner)
                    {
                        slave.pGenericCommandService.QueueWaitForSendCommand(command);
                    }
                }
                return false;
            }
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

    // Targeted sends count too: if the game sent the call only to the owner (or to the
    // owners of some players), it is owner-specific and must not be relayed to others.
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.QueueCommandOnSlave))]
    internal static class LobbyManager_QueueCommandOnSlave
    {
        private static void Prefix() => EffectRelay.BroadcastCount++;
    }

    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.QueueCommandOnSlaves), new[] { typeof(Zyklus.Player.PlayerNumberFlag), typeof(NetCommand) })]
    internal static class LobbyManager_QueueCommandOnSlavesFlag
    {
        private static void Prefix() => EffectRelay.BroadcastCount++;
    }
}
