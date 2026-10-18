using Altar.Utilities;
using HarmonyLib;
using Zyklus.Network;
using Zyklus.Utilities;

namespace Morta4P
{
    // Addresses and ids of networked objects with more than one client.

    internal static class ClientAddress
    {
        internal static bool Is(NetGOAddress address) =>
            address != null && (address.pMode == NetGOAddress.Mode.Client || address.pMode == NetGOAddress.Mode.ClientService);
    }

    // Last line of defence for the same case: an address whose client is already gone is
    // written as a static object that does not exist. The receiver only logs "not found",
    // instead of the host throwing in the middle of its send loop.
    [HarmonyPatch(typeof(NetGOAddress), nameof(NetGOAddress.WriteToStream), new[] { typeof(System.IO.Stream), typeof(bool) })]
    internal static class NetGOAddress_WriteToStream
    {
        private static bool Prefix(NetGOAddress __instance, System.IO.Stream stream, bool is_simple_dynamic_mode)
        {
            if (is_simple_dynamic_mode || !ClientAddress.Is(__instance) || __instance.pSlaveIdentity != null)
            {
                return true;
            }
            bool root = __instance.pChildIndex == byte.MaxValue;
            int header = Altar.Utilities.SerializationUtility.BoolToBit(root, 0)
                | Altar.Utilities.SerializationUtility.IntToBits((int)NetGOAddress.Mode.Static, 1, 3);
            stream.WriteByte2((byte)header);
            stream.WriteInt32(int.MinValue);
            if (!root)
            {
                stream.WriteByte2(__instance.pChildIndex);
            }
            return false;
        }
    }

    // Clients number networked objects with their own counter and expect it to match the id
    // the host sent. Leaving the owner out of a projectile spawn (above) skipped a number on
    // the owner, so every later object there got a different id than on the host: releases
    // and RMCs hit the wrong object (picked-up items stayed on screen). On a client, take the
    // host's id for every networked object instead of counting.
    [HarmonyPatch(typeof(ZyklusUtility), nameof(ZyklusUtility.TryRegisteringNetGOInstance))]
    internal static class ZyklusUtility_TryRegisteringNetGOInstance
    {
        private static readonly AccessTools.FieldRef<NetManager, ushort> LastIdField =
            AccessTools.FieldRefAccess<NetManager, ushort>("last_assigned_instance_id_");

        private static void Prefix(int net_instance_id)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby == null || !lobby.pIsSlave || net_instance_id < 1 || net_instance_id > ushort.MaxValue || NetManager.sSingleton == null)
            {
                return;
            }
            // The original then assigns last id + 1, which is the requested id.
            LastIdField(NetManager.sSingleton) = (ushort)(net_instance_id - 1);
        }
    }
}
