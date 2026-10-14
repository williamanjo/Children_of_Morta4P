using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Altar.Networking;
using HarmonyLib;
using UnityEngine;
using Zyklus;
using Zyklus.LevelGeneration.Players;
using Zyklus.Loot;
using Zyklus.Managers;
using Zyklus.Narrative;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.ThirdParties.CinemaDirector;
using Zyklus.CameraManager;
using Zyklus.Utilities;

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

    // --- Crashes -------------------------------------------------------------------------

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

    // --- Loot: drops meant for "both players" were flagged P1|P2 only ---------------------

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropDivineRelic),
        new[] { typeof(DivineRelicHandle), typeof(Vector2), typeof(Vector2), typeof(PlayerNumberFlag), typeof(bool), typeof(bool) })]
    internal static class Loot_DropDivineRelic
    {
        private static void Prefix(ref PlayerNumberFlag player_flags)
        {
            if (player_flags == (PlayerNumberFlag.P1 | PlayerNumberFlag.P2))
            {
                player_flags = PlayerNumberExtensions.sAllPlayers;
            }
        }
    }

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropConsumable),
        new[] { typeof(Vector2), typeof(Vector2), typeof(ConsumableInvetoryItemAsset), typeof(PlayerNumberFlag), typeof(bool) })]
    internal static class Loot_DropConsumable
    {
        private static void Prefix(ref PlayerNumberFlag player_number_flag)
        {
            if (player_number_flag == (PlayerNumberFlag.P1 | PlayerNumberFlag.P2))
            {
                player_number_flag = PlayerNumberExtensions.sAllPlayers;
            }
        }
    }

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropRune),
        new[] { typeof(int), typeof(Vector2), typeof(Vector2), typeof(PlayerNumber) })]
    internal static class Loot_DropRune
    {
        private static void Prefix(ref PlayerNumber players_flag)
        {
            if (players_flag == (PlayerNumber.P1 | PlayerNumber.P2))
            {
                players_flag = PlayerNumber.All;
            }
        }
    }

    // --- Revive --------------------------------------------------------------------------

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

    // --- Cutscenes -----------------------------------------------------------------------

    // StartMainCutscene puts the responsible player first with "if (responsible != 0) Reverse()",
    // which is only right for two players. Move the responsible player to the front instead.
    [HarmonyPatch(typeof(InGameCutsceneLoaderComponent), nameof(InGameCutsceneLoaderComponent.StartMainCutscene))]
    internal static class InGameCutsceneLoaderComponent_StartMainCutscene
    {
        [ThreadStatic]
        private static int responsible_;

        private static readonly MethodInfo Reverse =
            AccessTools.Method(typeof(List<EntityEnum>), nameof(List<EntityEnum>.Reverse), Type.EmptyTypes);

        private static void Prefix(int responsible_player) => responsible_ = responsible_player;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(Reverse))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(InGameCutsceneLoaderComponent_StartMainCutscene), nameof(ResponsibleFirst)))
                        .MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError("StartMainCutscene: Reverse call not found");
            }
        }

        private static void ResponsibleFirst(List<EntityEnum> list)
        {
            int index = responsible_;
            if (list.Count <= 2 || index <= 0 || index >= list.Count)
            {
                list.Reverse();
                return;
            }
            EntityEnum responsible = list[index];
            list.RemoveAt(index);
            list.Insert(0, responsible);
        }
    }

    // --- Home cinematics sync ------------------------------------------------------------

    // A client asks the host for the home cutscenes/idles; the host answered with a broadcast,
    // so with several clients each one also received the others' batches (duplicated idles,
    // "Can't collect home cinematics now" errors, early "finished" callbacks). Reply only to
    // the client that asked.
    internal static class HomeSync
    {
        internal static bool TryGetRequester(out NetIdentityBase requester)
        {
            requester = LobbyStatus.CurrentSender?.pID;
            return requester != null && LobbyManager.sSingleton.pIsMaster;
        }
    }

    [HarmonyPatch(typeof(HomeCinematicSyncer), nameof(HomeCinematicSyncer.FetchMasterQueuedCutscenes))]
    internal static class HomeCinematicSyncer_FetchMasterQueuedCutscenes
    {
        private static bool Prefix(HomeCinematicSyncer __instance)
        {
            if (!HomeSync.TryGetRequester(out var requester))
            {
                return true;
            }
            try
            {
                var bridge = __instance.pRMCBridge;
                foreach (var record in Traverse.Create(__instance).Field("cutscenes_").GetValue<System.Collections.IList>())
                {
                    bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 2, record);
                }
                bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 3);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Home cutscene sync: {e}");
                return true;
            }
        }
    }

    [HarmonyPatch(typeof(HomeCinematicSyncer), nameof(HomeCinematicSyncer.FetchMasterQueuedIdles))]
    internal static class HomeCinematicSyncer_FetchMasterQueuedIdles
    {
        private static bool Prefix(HomeCinematicSyncer __instance)
        {
            if (!HomeSync.TryGetRequester(out var requester))
            {
                return true;
            }
            try
            {
                var bridge = __instance.pRMCBridge;
                var self = Traverse.Create(__instance);
                foreach (var record in self.Field("idles_").GetValue<System.Collections.IList>())
                {
                    bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 5, record);
                }
                if (ProfileManager.sSingleton.pGameMode == GameMode.Campaign)
                {
                    object sequence = self.Field("home_to_den_sequence_").GetValue();
                    if (sequence != null)
                    {
                        bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 7,
                            Traverse.Create(sequence).Property("pGUID").GetValue());
                    }
                }
                object corruption = Traverse.Create(typeof(PECorruptionLevelState)).Property("sCurrentCorruptionLevel").GetValue()
                    ?? Traverse.Create(typeof(PECorruptionLevelState)).Field("sCurrentCorruptionLevel").GetValue();
                bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 8, corruption);
                bridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 6);
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Home idle sync: {e}");
                return true;
            }
        }
    }

    // --- Camera shake --------------------------------------------------------------------

    // Remote shakes were only sent when P2 was near the hit. Use any remote player.
    [HarmonyPatch(typeof(CameraShakeComponent), "ShouldShakeSlavePlayer")]
    internal static class CameraShakeComponent_ShouldShakeSlavePlayer
    {
        private static void Postfix(CameraShakeComponent __instance, Vector2 position, ref bool __result)
        {
            if (__result || LobbyManager.sSingleton.pSlaveClientsCount == 0)
            {
                return;
            }
            foreach (var number in new[] { PlayerNumber.P3, PlayerNumber.P4 })
            {
                if (CameraShakes.IsNear(__instance, number, position))
                {
                    __result = true;
                    return;
                }
            }
        }
    }

    // The per-player attack shake only forwarded P2's hits.
    [HarmonyPatch(typeof(CameraShakeComponent), nameof(CameraShakeComponent.AttackShake),
        new[] { typeof(Vector2), typeof(bool), typeof(PlayerNumber) })]
    internal static class CameraShakeComponent_AttackShake
    {
        private static void Postfix(CameraShakeComponent __instance, Vector2 position, bool network, PlayerNumber player_number)
        {
            if (!network || (player_number != PlayerNumber.P3 && player_number != PlayerNumber.P4)
                || !CameraShakes.IsNear(__instance, player_number, position))
            {
                return;
            }
            try
            {
                NetIdentityBase owner = PlayerManager.sSingleton.GetPlayerClientIdentity(player_number);
                __instance.pRMCBridge?.SendRMCToSlave(owner, RMCHandler.ErrorCondition.All, __instance, 1);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Attack shake P3/P4: {e}");
            }
        }
    }

    internal static class CameraShakes
    {
        private static readonly MethodInfo ShouldShake =
            AccessTools.Method(typeof(CameraShakeComponent), "ShouldShake", new[] { typeof(Vector2), typeof(Vector2) });

        internal static bool IsNear(CameraShakeComponent shake, PlayerNumber number, Vector2 position)
        {
            try
            {
                PlayerBase player = PlayerManager.sSingleton.GetPlayer(number);
                return player != null && !player.pLocalNumber.GetIsLocal()
                    && (bool)ShouldShake.Invoke(shake, new object[] { (Vector2)player.transform.position, position });
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    // --- Health-globe shop ---------------------------------------------------------------

    // The shop loops over every standing player but heals the buyer each time, so the buyer
    // got healed once per player and the others not at all. Heal each standing player once.
    [HarmonyPatch(typeof(Shop), nameof(Shop.OnHealthGlobeInteractionStart))]
    internal static class Shop_OnHealthGlobeInteractionStart
    {
        private static readonly MethodInfo HealthGlobeAcquired =
            AccessTools.Method(typeof(PlayerBase), nameof(PlayerBase.HealthGlobeAcquired));

        [ThreadStatic]
        private static bool healed_;

        private static void Prefix() => healed_ = false;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(HealthGlobeAcquired))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Shop_OnHealthGlobeInteractionStart), nameof(HealAll)))
                        .MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError("Shop.OnHealthGlobeInteractionStart: heal call not found");
            }
        }

        // Replaces "buyer.HealthGlobeAcquired(heal, mana)" inside the per-player loop.
        private static void HealAll(PlayerBase buyer, float heal_value, float mana_value)
        {
            if (healed_)
            {
                return;
            }
            healed_ = true;
            foreach (var player in PlayerManager.sSingleton.GetPlayers())
            {
                if (player != null && !player.pIsInLimbo)
                {
                    player.HealthGlobeAcquired(heal_value, mana_value);
                }
            }
        }
    }
}
