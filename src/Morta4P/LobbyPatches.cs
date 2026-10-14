using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Steam;
using HarmonyLib;
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
}
