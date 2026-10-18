using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Zyklus.Player;
using Zyklus.Stat;

namespace Morta4P
{
    // XP from a kill is split by the number of players and each one gets the share. With
    // four players that meant a quarter each; split by at most two instead, so 3-4 players
    // level like a 2-player co-op (10 XP -> 5 to every player).
    [HarmonyPatch(typeof(StatStaticDataContainer), nameof(StatStaticDataContainer.ShareXPBetweenCurrentPlayers))]
    internal static class StatStaticDataContainer_ShareXPBetweenCurrentPlayers
    {
        private static readonly MethodInfo Count =
            AccessTools.PropertyGetter(typeof(List<PlayerBase>), nameof(List<PlayerBase>.Count));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                // The divisor: "(float)players.Count"
                if (codes[i].Calls(Count) && codes[i + 1].opcode == OpCodes.Conv_R4)
                {
                    codes[i] = new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(StatStaticDataContainer_ShareXPBetweenCurrentPlayers), nameof(Divisor)))
                        .MoveLabelsFrom(codes[i]);
                    return codes;
                }
            }
            Plugin.Log.LogError("ShareXPBetweenCurrentPlayers: divisor not found, XP is split by every player");
            return codes;
        }

        private static int Divisor(List<PlayerBase> players)
        {
            int max = Plugin.XPSplitMax.Value;
            return max > 0 ? Mathf.Min(players.Count, max) : players.Count;
        }
    }
}
