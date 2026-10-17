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
    // Receiving: fragments were reassembled in one history keyed only by (tick, packet id),
    // so two clients' packets with the same key were merged together or thrown away. Old
    // unreliable packets were also dropped against one shared tick threshold, but every
    // machine counts its own ticks: once a client with a higher tick joined, everything
    // unreliable from a client with a lower tick was dropped and that client timed out.
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

    // Fragment reassembly per sender. Instead of replacing Tick_Receiving (reflection on every
    // packet caused stutter on the host), the original loop runs unchanged: right after the
    // transport hands over a packet, the service's two fragment histories and its receive
    // tick threshold are switched to the sender's own, so the original code only ever sees
    // that sender's packets.
    [HarmonyPatch(typeof(Altar.Steam.SteamTransportLayer), nameof(Altar.Steam.SteamTransportLayer.TryReceive))]
    internal static class SteamTransportLayer_TryReceive
    {
        private static readonly FieldInfo ReliableHistory = AccessTools.Field(typeof(TransportLobbyService), "reliable_fragment_history_");
        private static readonly FieldInfo NonReliableHistory = AccessTools.Field(typeof(TransportLobbyService), "non_reliable_fragment_history_");
        private static readonly AccessTools.FieldRef<TransportLobbyService, uint> ThresholdField =
            AccessTools.FieldRefAccess<TransportLobbyService, uint>("receive_tick_threshold_");

        private sealed class History
        {
            public object Reliable;
            public object NonReliable;
            public uint Threshold;
        }

        private static readonly Dictionary<NetIdentityBase, History> histories_ = new Dictionary<NetIdentityBase, History>();
        private static NetIdentityBase current_;

        private static void Postfix(bool __result, NetBufferHandle result)
        {
            if (!__result || result?.pIdentity == null || result.pIdentity == current_ || !Transport.IsMultiClientHost())
            {
                return;
            }
            TransportLobbyService service = LobbyManager.sSingleton.pTransportQueueService;
            if (service == null)
            {
                return;
            }
            if (!histories_.TryGetValue(result.pIdentity, out History history))
            {
                history = new History
                {
                    Reliable = Activator.CreateInstance(ReliableHistory.FieldType),
                    NonReliable = Activator.CreateInstance(NonReliableHistory.FieldType),
                };
                histories_[result.pIdentity] = history;
                if (histories_.Count > 8)
                {
                    Prune(result.pIdentity);
                }
            }
            ref uint threshold = ref ThresholdField(service);
            if (current_ != null && histories_.TryGetValue(current_, out History previous))
            {
                previous.Threshold = threshold;
            }
            ReliableHistory.SetValue(service, history.Reliable);
            NonReliableHistory.SetValue(service, history.NonReliable);
            threshold = history.Threshold;
            current_ = result.pIdentity;
        }

        private static void Prune(NetIdentityBase keep)
        {
            var stale = new List<NetIdentityBase>();
            foreach (NetIdentityBase sender in histories_.Keys)
            {
                if (sender != keep && !LobbyManager.sSingleton.pAllClientsIdentity.Contains(sender))
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
