using System;
using System.Collections.Generic;
using Altar.Localization;
using Altar.Networking;
using HarmonyLib;
using Zyklus;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;

namespace Morta4P
{
    // MiniLobby.SetMiniLobbyStatus is a remote method with no sender information. A client
    // only sends its status to the host, and the host only broadcasts its own, so with more
    // than one client every status ended up on the same partner HUD, and clients never saw
    // each other's status.
    //
    // The host now works out who sent a status (from the Client that executes the RMC),
    // shows it on that player's HUD and relays it to the other clients. The status enum
    // travels as an Int32, so the relay carries the player index in the upper bits.
    internal static class LobbyStatus
    {
        private const int IndexShift = 8;
        private const int StatusMask = 0xFF;

        [ThreadStatic]
        internal static Client CurrentSender;

        private static readonly Func<MiniLobby, MiniLobby.LobbyStatusEnum, LocalizedText> GetText =
            AccessTools.MethodDelegate<Func<MiniLobby, MiniLobby.LobbyStatusEnum, LocalizedText>>(
                AccessTools.Method(typeof(MiniLobby), "GetLobbyStatusLocalizedText"));

        private static readonly AccessTools.FieldRef<MiniLobby, MiniLobbyPlayer> OtherPlayerField =
            AccessTools.FieldRefAccess<MiniLobby, MiniLobbyPlayer>("other_player_");

        internal static MiniLobby.LobbyStatusEnum Encode(MiniLobby.LobbyStatusEnum status, int playerIndex)
        {
            return (MiniLobby.LobbyStatusEnum)(((int)status & StatusMask) | ((playerIndex + 1) << IndexShift));
        }

        // Returns the player index carried in the value, or -1 if it is a plain status.
        internal static int Decode(MiniLobby.LobbyStatusEnum raw, out MiniLobby.LobbyStatusEnum status)
        {
            int value = (int)raw;
            status = (MiniLobby.LobbyStatusEnum)(value & StatusMask);
            return (value >> IndexShift) - 1;
        }

        internal static void Received(MiniLobby lobby, MiniLobby.LobbyStatusEnum raw)
        {
            LobbyManager manager = LobbyManager.sSingleton;
            int index = Decode(raw, out var status);
            Client sender = CurrentSender;

            if (index < 0)
            {
                if (manager.pIsMaster)
                {
                    index = FindPlayerIndex(sender?.pID);
                    if (index >= 0)
                    {
                        Relay(lobby, manager, sender.pID, status, index);
                    }
                }
                else
                {
                    // Plain status on a client always comes from the host, who is P1.
                    index = 0;
                }
            }

            if (index < 0 || IsLocal(index))
            {
                return;
            }

            LocalizedText text = GetText(lobby, status);
            if (text == null)
            {
                return;
            }

            HudStack.SetStatus(index, text.pText);
            if (index == FirstRemoteIndex())
            {
                // Home-screen widgets only have room for one remote player.
                OtherPlayerField(lobby)?.SetStatus(text.ForceGetKey());
                OnlineMenu.sSingleton?.SetStatus(text.pText);
            }
        }

        private static void Relay(MiniLobby lobby, LobbyManager manager, NetIdentityBase source,
            MiniLobby.LobbyStatusEnum status, int index)
        {
            var bridge = lobby.pRMCBridge;
            if (bridge == null)
            {
                return;
            }
            MiniLobby.LobbyStatusEnum encoded = Encode(status, index);
            foreach (NetIdentityBase slave in manager.pSlaveClientsIdentity)
            {
                if (slave != null && !slave.Equals(source))
                {
                    bridge.SendRMCToSlave(slave, RMCHandler.ErrorCondition.All, lobby, 0, encoded, true);
                }
            }
        }

        private static int FindPlayerIndex(NetIdentityBase owner)
        {
            if (owner == null)
            {
                return -1;
            }
            try
            {
                List<int> indices = PlayerManager.sSingleton.GetOwnerPlayerIndices(owner);
                return indices != null && indices.Count > 0 ? indices[0] : -1;
            }
            catch (Exception)
            {
                return -1;
            }
        }

        private static bool IsLocal(int index)
        {
            var locals = PlayerManager.sSingleton?.pLocalPlayerIndices;
            return locals != null && locals.Contains(index);
        }

        private static int FirstRemoteIndex()
        {
            var capsule = PlayerManager.sSingleton?.GetActiveCapsule();
            if (capsule == null)
            {
                return -1;
            }
            for (int i = 0; i < capsule.pPlayers.Count; i++)
            {
                if (!capsule.pPlayers[i].pUser.CheckIsLocal())
                {
                    return i;
                }
            }
            return -1;
        }
    }

    [HarmonyPatch(typeof(RMCNetCommand), nameof(RMCNetCommand.Execute))]
    internal static class RMCNetCommand_Execute
    {
        private static void Prefix(Client client) => LobbyStatus.CurrentSender = client;

        private static Exception Finalizer(Exception __exception)
        {
            LobbyStatus.CurrentSender = null;
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MiniLobby), nameof(MiniLobby.SetMiniLobbyStatus))]
    internal static class MiniLobby_SetMiniLobbyStatus
    {
        private static bool Prefix(MiniLobby __instance, MiniLobby.LobbyStatusEnum status, bool called_from_other,
            ref MiniLobby.LobbyStatusEnum __result)
        {
            if (!called_from_other || LobbyManager.LobbyMode.OnlineCoop != LobbyManager.sSingleton.pLobbyMode)
            {
                return true;
            }
            try
            {
                LobbyStatus.Received(__instance, status);
                LobbyStatus.Decode(status, out __result);
                return false;
            }
            catch (Exception e)
            {
                // Do not fall back to the original: it cannot handle an encoded value.
                Plugin.Log.LogError($"Lobby status: {e}");
                LobbyStatus.Decode(status, out __result);
                return false;
            }
        }
    }
}
