using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Narrative;
using Zyklus.Network;
using Zyklus.ThirdParties.CinemaDirector;
using Zyklus.Utilities;

namespace Morta4P
{
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

    // A cutscene that never started on this machine (a client that was not part of the
    // cutscene's start, e.g. the companion unlock sequence on a third player) is still
    // finished when the floor is cleared. The original then calls RemoveOwner on its empty
    // visibility lists and throws; that exception killed the scene clearing job, so the
    // client never finished loading the next floor and everyone waited forever.
    [HarmonyPatch(typeof(CutsceneInitializer), nameof(CutsceneInitializer.FinalizeEntitiesVisibilityState))]
    internal static class CutsceneInitializer_FinalizeEntitiesVisibilityState
    {
        private static readonly FieldInfo EntitiesField = AccessTools.Field(typeof(CutsceneInitializer), "visibility_entities_");
        private static readonly FieldInfo RuntimeEntitiesField = AccessTools.Field(typeof(CutsceneInitializer), "visibility_runtime_entities_");

        private static bool Prefix(CutsceneInitializer __instance)
        {
            return EntitiesField.GetValue(__instance) != null && RuntimeEntitiesField.GetValue(__instance) != null;
        }
    }
}
