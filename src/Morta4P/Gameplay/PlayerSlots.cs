using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using Zyklus;
using Zyklus.LevelGeneration.Players;
using Zyklus.Managers;
using Zyklus.Player;
using Zyklus.ThirdParties.CinemaDirector;

namespace Morta4P
{
    // Gameplay code that indexes per-player data authored for two players: start positions in
    // passages, cutscene wait points, spawn tiles, companion player numbers. Missing entries are
    // synthesized next to the authored ones instead of throwing.
    internal static class PlayerSlots
    {
        internal const int Max = 4;

        // Extra positions go to the side of the authored one they copy, a bit further each time.
        internal static Vector3 Offset(int extra) => new Vector3(0.8f * extra, -0.4f * extra, 0f);

        internal static Vector2[] Extend(Vector2[] source)
        {
            if (source == null || source.Length == 0 || source.Length >= Max)
            {
                return source;
            }
            var result = new Vector2[Max];
            for (int i = 0; i < Max; i++)
            {
                result[i] = i < source.Length
                    ? source[i]
                    : source[i % source.Length] + (Vector2)Offset(i / source.Length);
            }
            return result;
        }

        private static readonly ConditionalWeakTable<Transform[], Transform[]> extended_ =
            new ConditionalWeakTable<Transform[], Transform[]>();

        internal static Transform[] Extend(Transform[] source)
        {
            if (source == null || source.Length == 0 || source.Length >= Max || source[0] == null)
            {
                return source;
            }
            if (extended_.TryGetValue(source, out var cached))
            {
                return cached;
            }
            var result = new Transform[Max];
            for (int i = 0; i < Max; i++)
            {
                if (i < source.Length)
                {
                    result[i] = source[i];
                    continue;
                }
                Transform template = source[i % source.Length] ?? source[0];
                var go = new GameObject(template.name + "_Morta4P_P" + (i + 1));
                go.transform.SetParent(template.parent, false);
                go.transform.position = template.position + Offset(i / source.Length);
                go.transform.rotation = template.rotation;
                result[i] = go.transform;
            }
            extended_.Add(source, result);
            return result;
        }

        internal static void Extend(List<Vector2> list, int count)
        {
            if (list == null || list.Count == 0)
            {
                return;
            }
            int authored = list.Count;
            for (int i = authored; i < count; i++)
            {
                list.Add(list[i % authored] + (Vector2)Offset(i / authored));
            }
        }
    }

    // Companions take "player count" as their PlayerNumber; with four players that is index 4,
    // which throws NotImplementedException. Give them P4 instead, as the game already does
    // with three players.
    [HarmonyPatch(typeof(PlayerNumberExtensions), nameof(PlayerNumberExtensions.ToPlayerNumber))]
    internal static class PlayerNumberExtensions_ToPlayerNumber
    {
        private static bool logged_;

        private static bool Prefix(int player_index, ref PlayerNumber __result)
        {
            if (player_index < PlayerSlots.Max)
            {
                return true;
            }
            if (!logged_)
            {
                logged_ = true;
                Plugin.Log.LogInfo($"Player index {player_index} (companion with 4 players) mapped to P4");
            }
            __result = PlayerNumber.P4;
            return false;
        }
    }

    [HarmonyPatch(typeof(PassageManager), nameof(PassageManager.pStartPositionOffset), MethodType.Getter)]
    internal static class PassageManager_StartPositionOffset
    {
        private static void Postfix(ref Vector2[] __result) => __result = PlayerSlots.Extend(__result);
    }

    [HarmonyPatch(typeof(PassageChunk), nameof(PassageChunk.pStartPosition), MethodType.Getter)]
    internal static class PassageChunk_StartPosition
    {
        private static void Postfix(ref Transform[] __result) => __result = PlayerSlots.Extend(__result);
    }

    [HarmonyPatch(typeof(CinemaDirectorPlayerWaitComponent), nameof(CinemaDirectorPlayerWaitComponent.pStartPositions), MethodType.Getter)]
    internal static class CinemaDirectorPlayerWaitComponent_StartPositions
    {
        private static void Postfix(ref Transform[] __result) => __result = PlayerSlots.Extend(__result);
    }

    // WaitForPlayerLoopCinemaActorAction reads its own field directly.
    [HarmonyPatch(typeof(WaitForPlayerLoopCinemaActorAction), "SetPositions")]
    internal static class WaitForPlayerLoopCinemaActorAction_SetPositions
    {
        private static readonly AccessTools.FieldRef<WaitForPlayerLoopCinemaActorAction, Transform[]> Field =
            AccessTools.FieldRefAccess<WaitForPlayerLoopCinemaActorAction, Transform[]>("start_positions_");

        private static void Prefix(WaitForPlayerLoopCinemaActorAction __instance)
        {
            Field(__instance) = PlayerSlots.Extend(Field(__instance));
        }
    }

    // Start rooms have spawn tiles for two players; with more the generator gives up and the
    // level never starts. Ask it for at most two positions and add the rest next to them.
    [HarmonyPatch]
    internal static class PlayersMatrixPassDataGenerator_GenerateData
    {
        private static MethodBase TargetMethod() =>
            AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(PlayersMatrixPassDataGenerator), nameof(PlayersMatrixPassDataGenerator.GenerateData)));

        private static readonly MethodInfo TotalPlayerCount =
            AccessTools.PropertyGetter(typeof(PlayerManager), nameof(PlayerManager.pTotalPlayerCount));

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            int replaced = 0;
            foreach (var code in instructions)
            {
                if (code.Calls(TotalPlayerCount))
                {
                    replaced++;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(PlayersMatrixPassDataGenerator_GenerateData), nameof(SpawnCount)))
                        .MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (replaced == 0)
            {
                Plugin.Log.LogError("PlayersMatrixPassDataGenerator: player count call not found");
            }
        }

        private static int SpawnCount(PlayerManager manager) => Mathf.Min(manager.pTotalPlayerCount, 2);
    }

    [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.InitializePlayers_LevelStart))]
    internal static class PlayerManager_InitializePlayers_LevelStart
    {
        private static void Prefix(PlayerManager __instance, List<Vector2> players_start_position)
        {
            PlayerSlots.Extend(players_start_position, __instance.GetPlayers().Count);
        }
    }
}
