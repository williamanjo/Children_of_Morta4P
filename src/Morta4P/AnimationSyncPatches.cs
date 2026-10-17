using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Zyklus.Animation;
using Zyklus.Network;
using Zyklus.Player;

namespace Morta4P
{
    // Player animations are replicated by AnimationSyncerModule in the host's snapshots, but
    // only for players with pIsMasterOwner, which on the host means the host's own player.
    // A client's attack animations therefore never left the host; with one client that was
    // fine (the only other machine is the owner, which plays them locally). With two or more
    // clients, client B never saw client A's attacks.
    //
    // On the host with two or more clients, animations of client-owned players are sent too.
    // Each client ignores animation data for its own player, which it already plays locally.
    internal static class AnimationSync
    {
        private static readonly ConditionalWeakTable<NetSyncerModule, PlayerBase> owners_ =
            new ConditionalWeakTable<NetSyncerModule, PlayerBase>();

        internal static PlayerBase PlayerOf(NetSyncerModule syncer)
        {
            if (!owners_.TryGetValue(syncer, out PlayerBase player))
            {
                player = syncer.GetComponentInParent<PlayerBase>();
                owners_.Add(syncer, player);
            }
            return player;
        }
    }

    [HarmonyPatch(typeof(AnimationSyncerModule), nameof(AnimationSyncerModule.CheckIsSendEnabled))]
    internal static class AnimationSyncerModule_CheckIsSendEnabled
    {
        private static readonly AccessTools.FieldRef<AnimationSyncerModule, bool> MasterOwnerField =
            AccessTools.FieldRefAccess<AnimationSyncerModule, bool>("is_master_owner_");

        private static readonly AccessTools.FieldRef<AnimationSyncerModule, List<AnimationEventToHFSMComponent>> ComponentsField =
            AccessTools.FieldRefAccess<AnimationSyncerModule, List<AnimationEventToHFSMComponent>>("animation_event_to_hfsms_");

        private static void Postfix(AnimationSyncerModule __instance, ref bool __result)
        {
            if (__result || MasterOwnerField(__instance))
            {
                return;
            }
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby == null || !lobby.pIsMaster || lobby.pSlaveClientsCount < 2 || !__instance.isActiveAndEnabled)
            {
                return;
            }
            PlayerBase player = AnimationSync.PlayerOf(__instance);
            if (player == null || player.pLocalNumber.GetIsLocal() || player.pIsCompanion)
            {
                return;
            }
            // Same rule as the original for the host's player: send while an action plays.
            List<AnimationEventToHFSMComponent> components = ComponentsField(__instance);
            if (components == null)
            {
                return;
            }
            foreach (AnimationEventToHFSMComponent component in components)
            {
                if (component.pCurrentActionName != null)
                {
                    __result = true;
                    return;
                }
            }
        }
    }

    [HarmonyPatch(typeof(AnimationSyncerModule), "SlaveTick_AfterReceive")]
    internal static class AnimationSyncerModule_SlaveTick_AfterReceive
    {
        private static bool Prefix(AnimationSyncerModule __instance)
        {
            PlayerBase player = AnimationSync.PlayerOf(__instance);
            // A client's own player is animated locally; host data for it is a delayed echo.
            return player == null || !player.pLocalNumber.GetIsLocal();
        }
    }

    // Same for the facing direction: FaceDirectionSyncerModule is send-enabled on the host
    // only for the host's own player, so other clients saw a client's player walk left
    // while still facing right. Enabled once per player when the run starts (clients only
    // join at home, so the client count is fixed for the run). Initialize_RunStart has
    // overloads; Initialize_RunStart2 is where all of them end, after the game set the flag.
    [HarmonyPatch(typeof(PlayerBase), "Initialize_RunStart2")]
    internal static class PlayerBase_Initialize_RunStart_FaceDirection
    {
        private static void Postfix(PlayerBase __instance)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby == null || !lobby.pIsMaster || lobby.pSlaveClientsCount < 2
                || __instance.pLocalNumber.GetIsLocal() || __instance.pIsCompanion || __instance.pFaceDirectionSyncer == null)
            {
                return;
            }
            __instance.pFaceDirectionSyncer.pSendEnabled = true;
        }
    }

    [HarmonyPatch(typeof(FaceDirectionSyncerModule), "SlaveTick_AfterReceive")]
    internal static class FaceDirectionSyncerModule_SlaveTick_AfterReceive
    {
        private static bool Prefix(FaceDirectionSyncerModule __instance)
        {
            PlayerBase player = AnimationSync.PlayerOf(__instance);
            return player == null || !player.pLocalNumber.GetIsLocal();
        }
    }
}
