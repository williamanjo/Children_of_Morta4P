using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Platform;

namespace Morta4P
{
    // The host-to-client sync was written for one client and often uses
    // "LobbyManager.sSingleton.pSlaveClients[0]" or broadcasts per-client data. With two or
    // more clients the second one never got the host save (stuck on "fetching host data")
    // while the first one received a second copy (stuck loading). These patches send each
    // piece of data to the client it belongs to.

    // Host save for a joining client went into the first client's bulk channel.
    [HarmonyPatch(typeof(ProfileClientService), "FetchHostSaveData")]
    internal static class ProfileClientService_FetchHostSaveData
    {
        [ThreadStatic]
        internal static bool Running;

        private static void Prefix() => Running = true;

        private static Exception Finalizer(Exception __exception)
        {
            Running = false;
            return __exception;
        }

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            FirstSlaveIL.UseOwnSlave(instructions, "FetchHostSaveData");
    }

    [HarmonyPatch(typeof(ProfileClientService), "SendMentalBubblesToSlave")]
    internal static class ProfileClientService_SendMentalBubblesToSlave
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions) =>
            FirstSlaveIL.UseOwnSlave(instructions, "SendMentalBubblesToSlave");
    }

    // "Save data sent" / "failed" were broadcast to every client; only the requesting one
    // is waiting for them (the bulk id is only valid on its channel).
    [HarmonyPatch(typeof(ProfileClientService), "FetchingHostSaveDataDone")]
    internal static class ProfileClientService_FetchingHostSaveDataDone
    {
        private static bool Prefix(ProfileClientService __instance, ushort bulk_id)
        {
            if (!LobbyManager.sSingleton.pIsMaster || __instance.pSlave == null)
            {
                return true;
            }
            Traverse.Create(__instance).Property("pIsHostSaveDataSent").SetValue(true);
            UnityEngine.Debug.Log($"[Morta4P] FetchingHostSaveDataDone -> slave {__instance.pSlave.pSlaveIndex}");
            __instance.pRMCBridge.SendRMCToSlave(__instance.pSlave.pSlaveIndex, RMCHandler.ErrorCondition.All, __instance, 12, bulk_id);
            return false;
        }
    }

    [HarmonyPatch(typeof(ProfileClientService), "FetchingHostSaveDataFailed")]
    internal static class ProfileClientService_FetchingHostSaveDataFailed
    {
        private static bool Prefix(ProfileClientService __instance)
        {
            if (!LobbyManager.sSingleton.pIsMaster || __instance.pSlave == null)
            {
                return true;
            }
            __instance.pRMCBridge.SendRMCToSlave(__instance.pSlave.pSlaveIndex, RMCHandler.ErrorCondition.All, __instance, 11);
            return false;
        }
    }

    // When the host enters the house it refreshes the thought bubbles through the first
    // client's service only. Fan it out to every client (not while answering one client's
    // save request, where it must stay with that client).
    [HarmonyPatch(typeof(ProfileClientService), nameof(ProfileClientService.RequestSendingMentalBubbles))]
    internal static class ProfileClientService_RequestSendingMentalBubbles
    {
        [ThreadStatic]
        private static bool fanning_;

        private static bool Prefix(ProfileClientService __instance)
        {
            if (fanning_ || ProfileClientService_FetchHostSaveData.Running || !FirstSlaveIL.IsMasterOnline())
            {
                return true;
            }
            List<SlaveClient> slaves = LobbyManager.sSingleton.pSlaveClients;
            if (slaves.Count < 2 || __instance.pSlave != slaves[0])
            {
                return true;
            }
            fanning_ = true;
            try
            {
                foreach (SlaveClient slave in slaves.ToArray())
                {
                    slave.pProfileService?.RequestSendingMentalBubbles();
                }
            }
            finally
            {
                fanning_ = false;
            }
            return false;
        }
    }

    // The host pushed its save into the first client's bulk channel and broadcast the id, so
    // other clients popped an id that does not exist on their channel. Push to each client
    // and send each one its own id.
    [HarmonyPatch(typeof(ProfileManager), nameof(ProfileManager.Internal_TryStartSavingDataToSlot))]
    internal static class ProfileManager_Internal_TryStartSavingDataToSlot
    {
        private static readonly MethodInfo SaveProfileBuffer = AccessTools.Method(typeof(ProfileManager), "SaveProfileBuffer");

        private static bool Prefix(ProfileManager __instance, SaveDataType save_data_type, SaveData save_data, int slot_id,
            ProfileManager.SaveDoneDelegate done_callback)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (save_data_type != SaveDataType.Profile || !lobby.pIsMasterAndOnline || lobby.pSlaveClientsCount < 2)
            {
                return true;
            }
            try
            {
                byte[] buffer = save_data.ToByteBuffer(save_data_type);
                SaveProfileBuffer.Invoke(__instance, new object[] { save_data_type, buffer, slot_id, done_callback });
                HostSaveQueue.Enqueue(__instance, buffer, lobby.pSlaveClients.ToArray());
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Host save sync: {e}");
                return true;
            }
        }
    }

    // The host save copy is large. Pushing it to every client in the same frame queued the
    // whole buffer several times at once; it now goes to one client, and the next client
    // only gets it once the previous client's command queue has drained.
    internal static class HostSaveQueue
    {
        private static readonly AccessTools.FieldRef<CommandClientService, List<NetCommand>> SendQueueField =
            AccessTools.FieldRefAccess<CommandClientService, List<NetCommand>>("command_send_queue_");

        private const float MaxWaitSeconds = 20f;

        private static ProfileManager manager_;
        private static byte[] buffer_;
        private static readonly Queue<SlaveClient> pending_ = new Queue<SlaveClient>();
        private static SlaveClient sending_;
        private static float started_;

        internal static void Enqueue(ProfileManager manager, byte[] buffer, SlaveClient[] slaves)
        {
            // A newer save replaces whatever is still waiting.
            manager_ = manager;
            buffer_ = buffer;
            pending_.Clear();
            foreach (SlaveClient slave in slaves)
            {
                if (slave != null)
                {
                    pending_.Enqueue(slave);
                }
            }
            sending_ = null;
            Update();
        }

        // Called every frame by the plugin.
        internal static void Update()
        {
            if (buffer_ == null)
            {
                return;
            }
            if (sending_ != null)
            {
                bool gone = sending_ == null || !LobbyManager.sSingleton.pSlaveClients.Contains(sending_);
                bool drained = gone || SendQueueField(sending_.pGenericCommandService).Count == 0;
                bool timedOut = UnityEngine.Time.realtimeSinceStartup - started_ > MaxWaitSeconds;
                if (!drained && !timedOut)
                {
                    return;
                }
                sending_ = null;
            }
            while (pending_.Count > 0)
            {
                SlaveClient next = pending_.Dequeue();
                if (next == null || !LobbyManager.sSingleton.pSlaveClients.Contains(next))
                {
                    continue;
                }
                ushort bulk_id = next.pBulkService.PushBulk(buffer_);
                manager_.pRMCBridge.SendRMCToSlave(next.pSlaveIndex, RMCHandler.ErrorCondition.All, manager_, 9, bulk_id);
                sending_ = next;
                started_ = UnityEngine.Time.realtimeSinceStartup;
                Plugin.Log.LogInfo($"Host save copy sent to client {next.pSlaveIndex} ({buffer_.Length} bytes)");
                return;
            }
            buffer_ = null;
            manager_ = null;
        }
    }

    // The host's save copy can be announced before its bulk data finished downloading
    // ("No downloaded bulk found"). Wait for it for a few seconds instead of throwing.
    [HarmonyPatch(typeof(ProfileManager), "FetchingHostSaveBufferDone")]
    internal static class ProfileManager_FetchingHostSaveBufferDone
    {
        private const float WaitSeconds = 15f;

        private static readonly AccessTools.FieldRef<BulkClientService, Dictionary<ushort, byte[]>> DownloadedField =
            AccessTools.FieldRefAccess<BulkClientService, Dictionary<ushort, byte[]>>("downloaded_bulks_");

        private static readonly List<KeyValuePair<ushort, float>> pending_ = new List<KeyValuePair<ushort, float>>();

        private static bool Prefix(ushort bulk_id)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby.pIsMaster)
            {
                return true;
            }
            if (TryApply(bulk_id))
            {
                return false;
            }
            Plugin.Log.LogInfo($"Host save buffer {bulk_id} not downloaded yet; waiting");
            pending_.Add(new KeyValuePair<ushort, float>(bulk_id, UnityEngine.Time.realtimeSinceStartup + WaitSeconds));
            return false;
        }

        // Called every frame by the plugin.
        internal static void Update()
        {
            for (int i = pending_.Count - 1; i >= 0; i--)
            {
                var entry = pending_[i];
                if (TryApply(entry.Key))
                {
                    pending_.RemoveAt(i);
                }
                else if (UnityEngine.Time.realtimeSinceStartup > entry.Value)
                {
                    Plugin.Log.LogWarning($"Host save buffer {entry.Key} never arrived; keeping the previous copy");
                    pending_.RemoveAt(i);
                }
            }
        }

        private static bool TryApply(ushort bulk_id)
        {
            BulkClientService bulk = LobbyManager.sSingleton?.pMasterClient?.pBulkService;
            if (bulk == null || !DownloadedField(bulk).ContainsKey(bulk_id))
            {
                return false;
            }
            byte[] buffer = bulk.PopBulk(bulk_id);
            ProfileManager manager = ProfileManager.sSingleton;
            manager.QueueSaveJob(SaveDataType.Profile, buffer, manager.pSelectedProfileSlotID, null);
            return true;
        }
    }
}
