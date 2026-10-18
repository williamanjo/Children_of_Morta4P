using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.TrainMan;

namespace Morta4P
{
    // Host profile changes that only reached the first client.

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.AddBoost))]
    internal static class LocalProfile_AddBoost
    {
        private static void Postfix(IdentifiableCharacterBoost boost) => OtherSlaves.ForEach(s => s.AddBoost(boost));
    }

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.RemoveBoost))]
    internal static class LocalProfile_RemoveBoost
    {
        private static void Postfix(IdentifiableCharacterBoost boost) => OtherSlaves.ForEach(s => s.RemoveBoost(boost));
    }

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.UnlockCharacter))]
    internal static class LocalProfile_UnlockCharacter
    {
        private static void Postfix(PlayerCharacterEnum character, bool update_game_stats) =>
            OtherSlaves.ForEach(s => s.UnlockCharacter(character, update_game_stats));
    }

    // Family stats answering one client's save/endless fetch went to every client; a client
    // in the middle of its own fetch could complete it with someone else's answer.
    [HarmonyPatch(typeof(ProfileClientService), nameof(ProfileClientService.SetFamilyStatsFromHost))]
    internal static class ProfileClientService_SetFamilyStatsFromHost
    {
        private static bool Prefix(ProfileClientService __instance, object host_family_stats)
        {
            if (!LobbyManager.sSingleton.pIsMaster || __instance.pSlave == null
                || (!ProfileClientService_FetchHostSaveData.Running && LobbyStatus.CurrentSender == null))
            {
                return true;
            }
            __instance.pRMCBridge.SendRMCToSlave(__instance.pSlave.pSlaveIndex, RMCHandler.ErrorCondition.All, __instance, 29, host_family_stats);
            return false;
        }
    }

    // Every profile change (gold, XP, items, ...) logged "Client count is not supported!"
    // as an error once the host had two or more clients. The code after it already handles
    // every client; only the log is wrong. Unity captures a stack trace and writes the log
    // file for each error, and in a fight this ran thousands of times on the host, stalling
    // its frames: positions reached the clients late and characters jumped around.
    [HarmonyPatch(typeof(ProfileManager), "OnProfileModified")]
    internal static class ProfileManager_OnProfileModified
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int start = codes.FindIndex(c => c.opcode == OpCodes.Ldstr && c.operand is string text && text.StartsWith("Client count is not supported"));
            int call = start < 0 ? -1 : codes.FindIndex(start, c => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt)
                && c.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Debug) && method.Name == "LogError");
            if (call < 0 || ((MethodInfo)codes[call].operand).GetParameters().Length != 1)
            {
                Plugin.Log.LogError("OnProfileModified: client count log not found");
                return codes;
            }
            // Drop the formatted message instead of logging it.
            codes[call] = new CodeInstruction(OpCodes.Pop).MoveLabelsFrom(codes[call]);
            return codes;
        }
    }
}
