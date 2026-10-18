using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using HarmonyLib;
using Zyklus;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;

namespace Morta4P
{
    // When a client's player goes down, only the host showed the revive prompt and the
    // off-screen arrow. Send them to the other clients too (not to the downed player's owner,
    // matching what the game does for the host's own player).
    [HarmonyPatch(typeof(PlayerBase), nameof(PlayerBase.SetRevivable))]
    internal static class PlayerBase_SetRevivable
    {
        private static void Postfix(PlayerBase __instance, bool revivable)
        {
            try
            {
                LobbyManager lobby = LobbyManager.sSingleton;
                if (lobby.pLobbyMode != LobbyManager.LobbyMode.OnlineCoop || !lobby.pIsMaster
                    || __instance.pIsCompanion || __instance.pAvatarData.pUser.CheckIsLocal())
                {
                    return;
                }
                var bridge = __instance.pRMCBridge;
                if (bridge == null)
                {
                    return;
                }
                NetIdentityBase owner = PlayerManager.sSingleton.GetPlayerClientIdentity(PlayerManager.sSingleton.GetPlayerIndex(__instance));
                foreach (NetIdentityBase slave in lobby.pSlaveClientsIdentity)
                {
                    if (slave != null && !slave.Equals(owner))
                    {
                        bridge.SendRMCToSlave(slave, RMCHandler.ErrorCondition.All, __instance, 16, revivable);
                    }
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Revive prompt relay: {e}");
            }
        }
    }

    // Initialize_LevelStart revives a downed player on "master, or slave and not master-owned".
    // With one client that meant the client's own player; with more, every client also ran
    // the revive for the other clients' players. Restrict the slave side to its own player.
    [HarmonyPatch(typeof(PlayerBase), nameof(PlayerBase.Initialize_LevelStart))]
    internal static class PlayerBase_Initialize_LevelStart
    {
        private static readonly MethodInfo IsMasterOwner =
            AccessTools.PropertyGetter(typeof(PlayerBase), nameof(PlayerBase.pIsMasterOwner));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(IsMasterOwner))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PlayerBase_Initialize_LevelStart), nameof(NotOwnedHere)))
                        .MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError("Initialize_LevelStart: pIsMasterOwner check not found");
            }
        }

        // Stands in for pIsMasterOwner inside "pIsSlave && !pIsMasterOwner".
        private static bool NotOwnedHere(PlayerBase player) => !player.pLocalNumber.GetIsLocal();
    }

    // The "everyone is down" check only runs when someone goes down. If the last standing
    // player leaves, the remaining downed players would wait forever.
    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.RemoveClientMainPlayer))]
    internal static class PlayerManager_RemoveClientMainPlayer
    {
        private static void Postfix(PlayerManager __instance)
        {
            try
            {
                if (!LobbyManager.sSingleton.pIsMaster || !__instance.pHasInitializedPlayerGameobjects)
                {
                    return;
                }
                List<PlayerBase> players = __instance.GetPlayers();
                if (players.Count == 0)
                {
                    return;
                }
                foreach (var player in players)
                {
                    if (player == null || !player.pIsInLimbo)
                    {
                        return;
                    }
                }
                Plugin.Log.LogInfo("All remaining players are down after a disconnect; ending the run");
                foreach (var player in players)
                {
                    player.RemoteHfsmPushEvent(160, __instance);
                }
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Disconnect game-over check: {e}");
            }
        }
    }
}
