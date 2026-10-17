using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Altar.Networking;
using Altar.Pool;
using Altar.Utilities;
using HarmonyLib;
using Zyklus.Network;
using Zyklus.Utilities;

namespace Morta4P
{
    // The game's packet layer (TransportLobbyService) was written for one remote peer.
    //
    // Sending: every tick the host merged everything queued for every client into a single
    // packet and sent that packet to all of them (always including the first client). Each
    // client then executed the other clients' command streams; command ids collide between
    // streams, so a client ran someone else's commands and dropped its own as duplicates,
    // its stream stalled and the connection timed out (one client at random, as soon as a
    // second client started syncing).
    //
    // Receiving: fragments were reassembled in one history keyed only by (tick, packet id).
    // Clients sync their tick to the host, so two clients sent packets with the same key and
    // their fragments were merged together or thrown away.
    //
    // On the host with two or more clients, packets are now built per client and fragments
    // are reassembled per sender. Otherwise the original code runs unchanged.
    internal static class Transport
    {
        internal static bool IsMultiClientHost()
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            return lobby != null && lobby.pIsMaster && lobby.pAllClientsIdentity != null && lobby.pAllClientsIdentity.Count >= 2;
        }
    }

    [HarmonyPatch(typeof(TransportLobbyService), "Flush")]
    internal static class TransportLobbyService_Flush
    {
        private const int MergedOwner = 4532986;
        private const int QueueOwner = 93871698;
        private const int FragmentsOwner = 87965453;

        private static readonly AccessTools.FieldRef<TransportLobbyService, byte> PacketIdField =
            AccessTools.FieldRefAccess<TransportLobbyService, byte>("packet_id_current_");

        private struct Entry
        {
            public NetIdentityBase Identity;
            public ByteBuffer Buffer;
        }

        private static readonly List<Entry> entries_ = new List<Entry>(64);
        private static readonly List<NetIdentityBase> targets_ = new List<NetIdentityBase>(4);

        private static bool Prefix(TransportLobbyService __instance, MultiSendBuffer multi_send_buffer, SendMode send_mode,
            ref int fragments_count, ref int fragments_sizesum, ref int original_data_size, ref int compressed_data_size)
        {
            if (!Transport.IsMultiClientHost())
            {
                return true;
            }

            fragments_count = 0;
            fragments_sizesum = 0;
            original_data_size = 0;
            compressed_data_size = 0;
            if (multi_send_buffer.Count == 0)
            {
                return false;
            }

            entries_.Clear();
            targets_.Clear();
            bool anyCommon = false;
            while (multi_send_buffer.Dequeue(0, out NetIdentityBase identity, out SendMode _, out ByteBuffer buffer))
            {
                entries_.Add(new Entry { Identity = identity, Buffer = buffer });
                if (identity == null)
                {
                    anyCommon = true;
                }
                else if (!targets_.Contains(identity))
                {
                    targets_.Add(identity);
                }
            }
            // Data queued without a receiver goes to every client.
            if (anyCommon)
            {
                foreach (NetIdentityBase client in LobbyManager.sSingleton.pAllClientsIdentity)
                {
                    if (client != null && !targets_.Contains(client))
                    {
                        targets_.Add(client);
                    }
                }
            }

            try
            {
                foreach (NetIdentityBase target in targets_)
                {
                    SendTo(__instance, target, send_mode, ref fragments_count, ref fragments_sizesum, ref original_data_size, ref compressed_data_size);
                }
            }
            finally
            {
                foreach (Entry entry in entries_)
                {
                    entry.Buffer.RemoveOwner(QueueOwner);
                }
                entries_.Clear();
            }
            return false;
        }

        private static void SendTo(TransportLobbyService service, NetIdentityBase target, SendMode send_mode,
            ref int fragments_count, ref int fragments_sizesum, ref int original_data_size, ref int compressed_data_size)
        {
            ushort count = 0;
            foreach (Entry entry in entries_)
            {
                if (entry.Identity == null || entry.Identity == target)
                {
                    count++;
                }
            }
            if (count == 0)
            {
                return;
            }

            ByteBuffer merged = NetManager.AcquireBufferBig(MergedOwner);
            try
            {
                merged.SetLength(0L);
                merged.SeekToBegin();
                merged.WriteUInt16(count);
                foreach (Entry entry in entries_)
                {
                    if (entry.Identity == null || entry.Identity == target)
                    {
                        // Concat consumes from the source; rewind so a shared buffer can be reused.
                        entry.Buffer.SeekToBegin();
                        merged.Concat(entry.Buffer);
                    }
                }
                original_data_size += (int)merged.Length;
                merged.Compress(NetManager.CompressionMethod.LZF);
                compressed_data_size += (int)merged.Length;

                ref byte packetId = ref PacketIdField(service);
                ListPoolInstance<ByteBuffer> fragments = ZyklusNetworkUtility.FragmentBuffer(merged, LobbyManager.sSingleton.pSessionID,
                    send_mode, NetManager.sSingleton.pTickID, packetId, FragmentsOwner);
                foreach (ByteBuffer fragment in fragments.pList)
                {
                    NetManager.sTransportLayer.SendImmediate(target, send_mode, fragment);
                    fragments_sizesum += (int)fragment.Length;
                    fragment.RemoveOwner(FragmentsOwner);
                }
                fragments_count += fragments.pList.Count;
                fragments.RemoveOwner(FragmentsOwner);
                packetId++;
            }
            finally
            {
                merged.RemoveOwner(MergedOwner);
            }
        }
    }

    [HarmonyPatch(typeof(TransportLobbyService), nameof(TransportLobbyService.Tick_Receiving))]
    internal static class TransportLobbyService_Tick_Receiving
    {
        private const int HandleOwner = -100;
        private const int FragmentOwner = 43921786;
        private const int MergedOwner = 36981692;

        private static readonly FieldInfo ReliableHistory = AccessTools.Field(typeof(TransportLobbyService), "reliable_fragment_history_");
        private static readonly FieldInfo NonReliableHistory = AccessTools.Field(typeof(TransportLobbyService), "non_reliable_fragment_history_");
        private static readonly AccessTools.FieldRef<TransportLobbyService, uint> ThresholdField =
            AccessTools.FieldRefAccess<TransportLobbyService, uint>("receive_tick_threshold_");

        private static readonly MethodInfo AcquireItem = AccessTools.Method(typeof(TransportLobbyService), "AcquireFragmentHistoryItem");
        private static readonly MethodInfo Record = AccessTools.Method(typeof(TransportLobbyService), "RecordFragmentInHistory");
        private static readonly MethodInfo TryMerge = AccessTools.Method(typeof(TransportLobbyService), "TryMergeFragments");
        private static readonly MethodInfo HandleBuffer = AccessTools.Method(typeof(TransportLobbyService), "HandleBuffer");
        private static readonly MethodInfo RemoveFromHistory = AccessTools.Method(typeof(TransportLobbyService), "RemoveFromHistory");
        private static readonly MethodInfo RemoveOlder = AccessTools.Method(typeof(TransportLobbyService), "RemoveNonReliableHistoryOlderThan");

        private sealed class History
        {
            public object Reliable;
            public object NonReliable;
        }

        private static readonly Dictionary<NetIdentityBase, History> histories_ = new Dictionary<NetIdentityBase, History>();

        private static History HistoryOf(NetIdentityBase sender)
        {
            if (!histories_.TryGetValue(sender, out History history))
            {
                history = new History
                {
                    Reliable = Activator.CreateInstance(ReliableHistory.FieldType),
                    NonReliable = Activator.CreateInstance(NonReliableHistory.FieldType),
                };
                histories_[sender] = history;
            }
            return history;
        }

        private static bool Prefix(TransportLobbyService __instance)
        {
            if (!Transport.IsMultiClientHost())
            {
                return true;
            }

            object originalReliable = ReliableHistory.GetValue(__instance);
            object originalNonReliable = NonReliableHistory.GetValue(__instance);
            try
            {
                while (NetManager.sTransportLayer.TryReceive(out NetBufferHandle result, HandleOwner))
                {
                    if (result == null)
                    {
                        continue;
                    }
                    try
                    {
                        Receive(__instance, result);
                    }
                    finally
                    {
                        result.RemoveOwner(HandleOwner);
                    }
                }
            }
            finally
            {
                ReliableHistory.SetValue(__instance, originalReliable);
                NonReliableHistory.SetValue(__instance, originalNonReliable);
            }
            PruneDisconnected();
            return false;
        }

        private static void Receive(TransportLobbyService service, NetBufferHandle result)
        {
            if (!result.pBuffer.ReadZyklusHeader(out byte session_id, out SendMode send_mode, out uint tick_id,
                    out byte packet_id, out ushort fragment_count, out ushort index))
            {
                UnityEngine.Debug.LogError("Invalid buffer received! buffer=" + result);
                return;
            }

            ref uint threshold = ref ThresholdField(service);
            uint candidate = NetworkUtility.SeekUIntID(tick_id, -60);
            bool raised = NetworkUtility.CompareUIntID(candidate, threshold) > 0;
            if (raised)
            {
                threshold = candidate;
            }
            bool reliable = send_mode.IsReliable();
            if (!reliable && NetworkUtility.CompareUIntID(tick_id, threshold) < 0)
            {
                return;
            }
            byte lobbySession = LobbyManager.sSingleton.pSessionID;
            if (session_id != 0 && lobbySession != 0 && lobbySession != session_id)
            {
                return;
            }

            // Reassemble with this sender's own fragment history.
            History history = HistoryOf(result.pIdentity);
            ReliableHistory.SetValue(service, history.Reliable);
            NonReliableHistory.SetValue(service, history.NonReliable);

            int size = (int)(result.pBuffer.Length - ZyklusNetworkUtility.GetZyklusHeaderSize());
            object fragment = AcquireItem.Invoke(null, new object[] { FragmentOwner, send_mode, tick_id, (int)packet_id, (int)index, result.pBuffer, size });
            Record.Invoke(service, new object[] { reliable, fragment });

            var mergeArgs = new object[] { MergedOwner, null, reliable, fragment_count, tick_id, packet_id };
            if ((bool)TryMerge.Invoke(service, mergeArgs))
            {
                var merged = (ByteBuffer)mergeArgs[1];
                merged.SeekToBegin();
                HandleBuffer.Invoke(service, new object[] { merged, result.pIdentity, tick_id });
                RemoveFromHistory.Invoke(service, new object[] { reliable, tick_id, (int)packet_id });
                merged.RemoveOwner(MergedOwner);
            }
            if (!reliable && raised)
            {
                RemoveOlder.Invoke(service, new object[] { threshold });
            }
        }

        // Forget histories of peers that left (their pooled fragments are simply dropped).
        private static void PruneDisconnected()
        {
            if (histories_.Count <= LobbyManager.sSingleton.pAllClientsIdentity.Count)
            {
                return;
            }
            var stale = new List<NetIdentityBase>();
            foreach (NetIdentityBase sender in histories_.Keys)
            {
                if (!LobbyManager.sSingleton.pAllClientsIdentity.Contains(sender))
                {
                    stale.Add(sender);
                }
            }
            foreach (NetIdentityBase sender in stale)
            {
                histories_.Remove(sender);
            }
        }
    }
}
