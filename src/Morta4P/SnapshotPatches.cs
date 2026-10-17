using System;
using System.Collections.Generic;
using System.Reflection;
using Altar.Collection;
using Altar.Networking;
using HarmonyLib;
using Zyklus.Network;

namespace Morta4P
{
    // World state (positions, animations, ...) goes from the host to the clients as a diff
    // against the last snapshot the client acknowledged. The host kept ONE "last acked
    // snapshot" for everyone and dropped history older than any ack, so with two or more
    // clients one client got diffs against a snapshot it never had ("history snapshot ...
    // not found") and stopped receiving positions.
    //
    // With two or more clients the host now tracks acks per client, keeps history back to
    // the oldest ack and sends each client a diff against its own last ack.
    internal static class Snapshots
    {
        internal const int HistoryOwner = 732196489;
        private const int BufferOwner = 413256984;
        private const int SyncOwner = 1496387523;

        internal static readonly Dictionary<NetIdentityBase, uint> Acks = new Dictionary<NetIdentityBase, uint>();

        internal static readonly AccessTools.FieldRef<SyncLobbyService, LinkedArray<Snapshot>> HistoryField =
            AccessTools.FieldRefAccess<SyncLobbyService, LinkedArray<Snapshot>>("snapshot_history_");

        internal static readonly AccessTools.FieldRef<SyncLobbyService, Snapshot> LastAckField =
            AccessTools.FieldRefAccess<SyncLobbyService, Snapshot>("last_ack_snapshot_");

        internal static readonly AccessTools.FieldRef<SyncLobbyService, bool> ClearedField =
            AccessTools.FieldRefAccess<SyncLobbyService, bool>("is_cleared_");

        private static readonly MethodInfo ResetSendStats = AccessTools.Method(typeof(SyncLobbyService), "ResetSendStats");
        private static readonly MethodInfo BuildCurrentSnapshot = AccessTools.Method(typeof(SyncLobbyService), "BuildCurrentSnapshot");
        private static readonly MethodInfo RecordSnapshotInHistory = AccessTools.Method(typeof(SyncLobbyService), "RecordSnapshotInHistory");
        internal static readonly MethodInfo RemoveHistoryOlderThan = AccessTools.Method(typeof(SyncLobbyService), "RemoveHistoryOlderThan");

        internal static bool Active()
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            return lobby != null && lobby.pIsMaster && lobby.pSlaveClientsCount >= 2;
        }

        internal static Snapshot Find(SyncLobbyService service, uint tick)
        {
            for (LinkedArrayNode<Snapshot> node = HistoryField(service).pFirst; node != null; node = node.pNext)
            {
                if (node.pValue.pTickID == tick)
                {
                    return node.pValue;
                }
            }
            return null;
        }

        internal static void Send(SyncLobbyService service)
        {
            ResetSendStats.Invoke(service, null);
            uint tick = NetManager.sSingleton.pTickID;
            var args = new object[] { HistoryOwner, tick, 6131, 0, 0, 0 };
            var snapshot = (Snapshot)BuildCurrentSnapshot.Invoke(null, args);
            RecordSnapshotInHistory.Invoke(service, new object[] { snapshot });

            foreach (SlaveClient slave in LobbyManager.sSingleton.pSlaveClients.ToArray())
            {
                if (slave?.pID == null)
                {
                    continue;
                }
                Snapshot baseline = Acks.TryGetValue(slave.pID, out uint acked) ? Find(service, acked) : null;
                ByteBuffer buffer = NetManager.AcquireBufferMid(BufferOwner);
                try
                {
                    buffer.WriteDiff(snapshot, baseline);
                    NetSync sync = NetSync.AcquireInstance(SyncOwner, baseline?.pTickID ?? 0u, tick, tick);
                    sync.FillNetSync(buffer);
                    slave.pSendQueueService.QueueSend(NetManager.sSingleton.pNonReliableSendMode, sync);
                    sync.RemoveOwner(SyncOwner);
                }
                finally
                {
                    buffer.RemoveOwner(BufferOwner);
                }
            }
        }

        // Keeps history back to the oldest ack among the connected clients.
        internal static void Prune(SyncLobbyService service)
        {
            uint? oldest = null;
            foreach (NetIdentityBase slave in LobbyManager.sSingleton.pSlaveClientsIdentity)
            {
                if (slave == null)
                {
                    continue;
                }
                if (!Acks.TryGetValue(slave, out uint acked))
                {
                    return; // a client that never acked needs full snapshots; keep everything
                }
                if (oldest == null || Altar.Utilities.NetworkUtility.CompareUIntID(acked, oldest.Value) < 0)
                {
                    oldest = acked;
                }
            }
            if (oldest != null)
            {
                RemoveHistoryOlderThan.Invoke(service, new object[] { oldest.Value });
                LastAckField(service) = Find(service, oldest.Value);
            }
        }
    }

    [HarmonyPatch(typeof(SyncLobbyService), "SendCurrentSyncs")]
    internal static class SyncLobbyService_SendCurrentSyncs
    {
        private static bool Prefix(SyncLobbyService __instance)
        {
            if (!Snapshots.Active())
            {
                return true;
            }
            try
            {
                Snapshots.Send(__instance);
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"Per-client snapshot: {e}");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(SyncLobbyService), "AckReceivedSync")]
    internal static class SyncLobbyService_AckReceivedSync
    {
        private static bool Prefix(SyncLobbyService __instance, object tick_id)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            NetIdentityBase sender = LobbyStatus.CurrentSender?.pID;
            if (!lobby.pIsMaster || sender == null)
            {
                return true;
            }
            uint tick = Traverse.Create(tick_id).Field("value").GetValue<uint>();
            Snapshots.Acks[sender] = tick;
            if (lobby.pSlaveClientsCount < 2)
            {
                return true;
            }
            if (!Snapshots.ClearedField(__instance))
            {
                try
                {
                    Snapshots.Prune(__instance);
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Snapshot ack: {e}");
                }
            }
            return false;
        }
    }

    // Snapshot history is reset between floors; forget the acks with it.
    [HarmonyPatch(typeof(SyncLobbyService), "Clear")]
    internal static class SyncLobbyService_Clear
    {
        private static void Postfix() => Snapshots.Acks.Clear();
    }
}
