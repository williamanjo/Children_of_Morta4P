using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using Altar.Steam;
using HarmonyLib;
using Zyklus;
using Zyklus.Managers;
using Zyklus.Network;

namespace Morta4P
{
    // Steam lobby is created with a serialized member limit (2 in the shipped prefab).
    [HarmonyPatch(typeof(SteamInterface), "CreateLobby", new Type[0])]
    internal static class SteamInterface_CreateLobby
    {
        private static readonly AccessTools.FieldRef<SteamInterface, int> MaxPlayersField =
            AccessTools.FieldRefAccess<SteamInterface, int>("lobby_max_players_count_");

        private static void Prefix(SteamInterface __instance)
        {
            int old = MaxPlayersField(__instance);
            MaxPlayersField(__instance) = Plugin.MaxPlayers.Value;
            Plugin.Log.LogInfo($"Steam lobby max members {old} -> {Plugin.MaxPlayers.Value}");
        }
    }

    // Despite the name, the original returns true when the lobby still has room
    // (it is true only while there are zero slaves).
    [HarmonyPatch(typeof(LobbyManager), "CheckLobbyIsFull")]
    internal static class LobbyManager_CheckLobbyIsFull
    {
        private static void Postfix(LobbyManager __instance, ref bool __result)
        {
            __result = __instance.pSlaveClients.Count < Plugin.MaxSlaves;
        }
    }

    // CheckIsJoiningPossible also has an inlined "GetCurrentPlayersCount() < 2".
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.CheckIsJoiningPossible))]
    internal static class LobbyManager_CheckIsJoiningPossible
    {
        private static readonly MethodInfo GetCurrentPlayersCount =
            AccessTools.Method(typeof(LobbyManager), nameof(LobbyManager.GetCurrentPlayersCount));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i < codes.Count - 1; i++)
            {
                if (codes[i].Calls(GetCurrentPlayersCount) && codes[i + 1].opcode == OpCodes.Ldc_I4_2)
                {
                    codes[i + 1] = new CodeInstruction(OpCodes.Call,
                        AccessTools.PropertyGetter(typeof(LobbyManager_CheckIsJoiningPossible), nameof(Limit)))
                        .MoveLabelsFrom(codes[i + 1]);
                    replaced++;
                }
            }
            if (replaced == 0)
            {
                Plugin.Log.LogError("CheckIsJoiningPossible: player-count literal not found, join limit stays at 2");
            }
            return codes;
        }

        private static int Limit => Plugin.MaxPlayers.Value;
    }

    // A client asks the host which bundles to load; the host answered with a broadcast, so
    // the other clients (not asking) ran a null callback and threw. Reply to the asker only,
    // and ignore an answer nobody waits for.
    [HarmonyPatch(typeof(ZyklusAssetBundleManager), "SendMasterTrainmanRequiredBundles")]
    internal static class ZyklusAssetBundleManager_SendMasterTrainmanRequiredBundles
    {
        private static readonly FieldInfo CallbackField = AccessTools.Field(typeof(ZyklusAssetBundleManager), "fetch_done_callback_");

        private static bool Prefix(ZyklusAssetBundleManager __instance, List<ZyklusAssetBundles> bundles)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby.pIsMaster)
            {
                NetIdentityBase requester = LobbyStatus.CurrentSender?.pID;
                if (requester == null)
                {
                    return true;
                }
                __instance.pRMCBridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 2, bundles);
                return false;
            }
            // Client: only handle the answer if this client asked.
            return CallbackField.GetValue(__instance) != null;
        }
    }
}
