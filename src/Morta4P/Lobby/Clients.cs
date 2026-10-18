using System;
using System.Collections.Generic;
using Altar.Networking;
using Altar.Steam;
using HarmonyLib;
using Zyklus;
using Zyklus.GameManager;
using Zyklus.Managers;
using Zyklus.Network;

namespace Morta4P
{
    // A leaving client's notice was resolved as "the first client left", so with several
    // clients the wrong one could be dropped. Use the client that sent the RMC.
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnSlaveLeft))]
    internal static class LobbyManager_OnSlaveLeft
    {
        private static bool Prefix()
        {
            Client sender = LobbyStatus.CurrentSender;
            if (!LobbyManager.sSingleton.pIsMaster || sender?.pID == null)
            {
                return true;
            }
            NetManager.sTransportLayer.RaiseLobbyEvent(LobbyEventReason.DC_SlaveLeft, sender.pID);
            return false;
        }
    }

    // Kicking from the host (going back to the menu, ...) only raised the event for the
    // first client.
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnMasterKick))]
    internal static class LobbyManager_OnMasterKick
    {
        private static bool Prefix(LobbyManager __instance)
        {
            if (!__instance.pIsMaster || __instance.pSlaveClientsCount < 2)
            {
                return true;
            }
            var identities = new List<NetIdentityBase>(__instance.pSlaveClientsIdentity);
            __instance.pRMCBridge.SendRMCToSlaves(RMCHandler.ErrorCondition.All, __instance, 4);
            __instance.MasterFlushCommands();
            foreach (NetIdentityBase identity in identities)
            {
                NetManager.sTransportLayer.RaiseLobbyEvent(LobbyEventReason.MasterKicking, identity);
            }
            return false;
        }
    }

    // On every capsule change each machine maps players to their owner's connection with
    // LobbyManager.FindClient(user). A client is only connected to the host, so with two or
    // more clients the other client's player has no Client there: FindClient returned null,
    // ".pID" threw, and the client's network loop stopped (it went silent and timed out).
    // Map such players to their owner's network identity instead.
    [HarmonyPatch(typeof(PlayerManager), "SetPlayersClients")]
    internal static class PlayerManager_SetPlayersClients
    {
        private static readonly AccessTools.FieldRef<PlayerManager, List<SlaveClient>> ClientsField =
            AccessTools.FieldRefAccess<PlayerManager, List<SlaveClient>>("players_client_");

        private static readonly AccessTools.FieldRef<PlayerManager, List<NetIdentityBase>> IdsField =
            AccessTools.FieldRefAccess<PlayerManager, List<NetIdentityBase>>("players_client_id_");

        private static bool Prefix(PlayerManager __instance, PlayersCapsule players)
        {
            try
            {
                List<SlaveClient> clients = ClientsField(__instance);
                List<NetIdentityBase> ids = IdsField(__instance);
                clients.Clear();
                ids.Clear();
                for (int i = 0; i < players.pPlayers.Count; i++)
                {
                    IUser user = players.pPlayers[i].pUser;
                    if (user.CheckIsLocal())
                    {
                        clients.Add(null);
                        ids.Add(King.sSingleton.GetLocalIdentity());
                        continue;
                    }
                    Client client = LobbyManager.sSingleton.FindClient(user);
                    if (client != null)
                    {
                        clients.Add(client as SlaveClient);
                        ids.Add(client.pID);
                        continue;
                    }
                    clients.Add(null);
                    ids.Add(IdentityOf(user));
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"SetPlayersClients: {e}");
                return true;
            }
        }

        private static NetIdentityBase IdentityOf(IUser user)
        {
            if (user is SteamUser steamUser && SteamInterface.sSingleton != null)
            {
                return SteamInterface.sSingleton.GetIdentity(steamUser, create_if_not_found: true);
            }
            // Unknown platform: attribute the player to the host, who relays its state.
            return LobbyManager.sSingleton.pMasterIdentity;
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
}
