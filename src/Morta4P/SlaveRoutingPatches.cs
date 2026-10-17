using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Altar.Networking;
using Altar.Steam;
using HarmonyLib;
using Zyklus;
using Zyklus.GameManager;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Platform;
using Zyklus.Player;
using Zyklus.TrainMan;

namespace Morta4P
{
    // The host-to-client sync was written for one client and often uses
    // "LobbyManager.sSingleton.pSlaveClients[0]" or broadcasts per-client data. With two or
    // more clients the second one never got the host save (stuck on "fetching host data")
    // while the first one received a second copy (stuck loading). These patches send each
    // piece of data to the client it belongs to.

    // --- Per-client profile service: use this service's own client ---------------------

    internal static class FirstSlaveIL
    {
        // LobbyManager.sSingleton is a static field.
        private static readonly FieldInfo LobbySingleton = AccessTools.Field(typeof(LobbyManager), nameof(LobbyManager.sSingleton));

        private static readonly MethodInfo SlaveClients =
            AccessTools.PropertyGetter(typeof(LobbyManager), nameof(LobbyManager.pSlaveClients));

        private static readonly MethodInfo GetItem =
            AccessTools.Method(typeof(List<SlaveClient>), "get_Item");

        private static readonly MethodInfo ServiceSlave =
            AccessTools.PropertyGetter(typeof(ProfileClientService), nameof(ProfileClientService.pSlave));

        // Rewrites "LobbyManager.sSingleton.pSlaveClients[0]" into "this.pSlave".
        internal static IEnumerable<CodeInstruction> UseOwnSlave(IEnumerable<CodeInstruction> instructions, string method)
        {
            var codes = new List<CodeInstruction>(instructions);
            int replaced = 0;
            for (int i = 0; i + 3 < codes.Count; i++)
            {
                if (codes[i].LoadsField(LobbySingleton) && codes[i + 1].Calls(SlaveClients)
                    && codes[i + 2].LoadsConstant(0) && codes[i + 3].Calls(GetItem))
                {
                    var load = new CodeInstruction(OpCodes.Ldarg_0).MoveLabelsFrom(codes[i]);
                    codes[i] = load;
                    codes[i + 1] = new CodeInstruction(OpCodes.Call, ServiceSlave).MoveLabelsFrom(codes[i + 1]);
                    codes[i + 2] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(codes[i + 2]);
                    codes[i + 3] = new CodeInstruction(OpCodes.Nop).MoveLabelsFrom(codes[i + 3]);
                    replaced++;
                }
            }
            if (replaced == 0)
            {
                Plugin.Log.LogError($"{method}: pSlaveClients[0] not found");
            }
            return codes;
        }

        internal static bool IsMasterOnline() =>
            LobbyManager.sSingleton != null && LobbyManager.sSingleton.pIsMaster && LobbyManager.sSingleton.pSlaveClientsCount > 0;
    }

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

    // --- Host profile changes that only reached the first client -----------------------

    internal static class OtherSlaves
    {
        internal static void ForEach(Action<ProfileClientService> action)
        {
            if (!FirstSlaveIL.IsMasterOnline())
            {
                return;
            }
            List<SlaveClient> slaves = LobbyManager.sSingleton.pSlaveClients;
            for (int i = 1; i < slaves.Count; i++)
            {
                try
                {
                    if (slaves[i]?.pProfileService != null)
                    {
                        action(slaves[i].pProfileService);
                    }
                }
                catch (Exception e)
                {
                    Plugin.Log.LogError($"Profile sync to client {i}: {e}");
                }
            }
        }
    }

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.AddBoost))]
    internal static class LocalProfile_AddBoost
    {
        private static void Postfix(IdentifiableCharacterBoost boost) => OtherSlaves.ForEach(s => s.AddBoost(boost));
    }

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.RemoveBoost))]
    internal static class LocalProfile_RemoveBoost
    {
        private static void Postfix(IdentifiableCharacterBoost boost) => OtherSlaves.ForEach(s => s.RemoveBoost(boost));
    }

    [HarmonyPatch(typeof(LocalProfile), nameof(LocalProfile.UnlockCharacter))]
    internal static class LocalProfile_UnlockCharacter
    {
        private static void Postfix(PlayerCharacterEnum character, bool update_game_stats) =>
            OtherSlaves.ForEach(s => s.UnlockCharacter(character, update_game_stats));
    }

    // --- Host save buffer after a save (end of run, etc.) ------------------------------

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

    // --- Lobby departures --------------------------------------------------------------

    // A leaving client's notice was resolved as "the first client left", so with several
    // clients the wrong one could be dropped. Use the client that sent the RMC.
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnSlaveLeft))]
    internal static class LobbyManager_OnSlaveLeft
    {
        private static bool Prefix()
        {
            Client sender = LobbyStatus.CurrentSender;
            if (!LobbyManager.sSingleton.pIsMaster || sender?.pID == null)
            {
                return true;
            }
            NetManager.sTransportLayer.RaiseLobbyEvent(LobbyEventReason.DC_SlaveLeft, sender.pID);
            return false;
        }
    }

    // Kicking from the host (going back to the menu, ...) only raised the event for the
    // first client.
    [HarmonyPatch(typeof(LobbyManager), nameof(LobbyManager.OnMasterKick))]
    internal static class LobbyManager_OnMasterKick
    {
        private static bool Prefix(LobbyManager __instance)
        {
            if (!__instance.pIsMaster || __instance.pSlaveClientsCount < 2)
            {
                return true;
            }
            var identities = new List<NetIdentityBase>(__instance.pSlaveClientsIdentity);
            __instance.pRMCBridge.SendRMCToSlaves(RMCHandler.ErrorCondition.All, __instance, 4);
            __instance.MasterFlushCommands();
            foreach (NetIdentityBase identity in identities)
            {
                NetManager.sTransportLayer.RaiseLobbyEvent(LobbyEventReason.MasterKicking, identity);
            }
            return false;
        }
    }

    // --- Player list on a client -------------------------------------------------------

    // On every capsule change each machine maps players to their owner's connection with
    // LobbyManager.FindClient(user). A client is only connected to the host, so with two or
    // more clients the other client's player has no Client there: FindClient returned null,
    // ".pID" threw, and the client's network loop stopped (it went silent and timed out).
    // Map such players to their owner's network identity instead.
    [HarmonyPatch(typeof(PlayerManager), "SetPlayersClients")]
    internal static class PlayerManager_SetPlayersClients
    {
        private static readonly AccessTools.FieldRef<PlayerManager, List<SlaveClient>> ClientsField =
            AccessTools.FieldRefAccess<PlayerManager, List<SlaveClient>>("players_client_");

        private static readonly AccessTools.FieldRef<PlayerManager, List<NetIdentityBase>> IdsField =
            AccessTools.FieldRefAccess<PlayerManager, List<NetIdentityBase>>("players_client_id_");

        private static bool Prefix(PlayerManager __instance, PlayersCapsule players)
        {
            try
            {
                List<SlaveClient> clients = ClientsField(__instance);
                List<NetIdentityBase> ids = IdsField(__instance);
                clients.Clear();
                ids.Clear();
                for (int i = 0; i < players.pPlayers.Count; i++)
                {
                    IUser user = players.pPlayers[i].pUser;
                    if (user.CheckIsLocal())
                    {
                        clients.Add(null);
                        ids.Add(King.sSingleton.GetLocalIdentity());
                        continue;
                    }
                    Client client = LobbyManager.sSingleton.FindClient(user);
                    if (client != null)
                    {
                        clients.Add(client as SlaveClient);
                        ids.Add(client.pID);
                        continue;
                    }
                    clients.Add(null);
                    ids.Add(IdentityOf(user));
                }
                return false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"SetPlayersClients: {e}");
                return true;
            }
        }

        private static NetIdentityBase IdentityOf(IUser user)
        {
            if (user is SteamUser steamUser && SteamInterface.sSingleton != null)
            {
                return SteamInterface.sSingleton.GetIdentity(steamUser, create_if_not_found: true);
            }
            // Unknown platform: attribute the player to the host, who relays its state.
            return LobbyManager.sSingleton.pMasterIdentity;
        }
    }

    // --- Trainman asset bundles ------------------------------------------------------

    // A client asks the host which bundles to load; the host answered with a broadcast, so
    // the other clients (not asking) ran a null callback and threw. Reply to the asker only,
    // and ignore an answer nobody waits for.
    [HarmonyPatch(typeof(ZyklusAssetBundleManager), "SendMasterTrainmanRequiredBundles")]
    internal static class ZyklusAssetBundleManager_SendMasterTrainmanRequiredBundles
    {
        private static readonly FieldInfo CallbackField = AccessTools.Field(typeof(ZyklusAssetBundleManager), "fetch_done_callback_");

        private static bool Prefix(ZyklusAssetBundleManager __instance, List<ZyklusAssetBundles> bundles)
        {
            LobbyManager lobby = LobbyManager.sSingleton;
            if (lobby.pIsMaster)
            {
                NetIdentityBase requester = LobbyStatus.CurrentSender?.pID;
                if (requester == null)
                {
                    return true;
                }
                __instance.pRMCBridge.SendRMCToSlave(requester, RMCHandler.ErrorCondition.All, __instance, 2, bundles);
                return false;
            }
            // Client: only handle the answer if this client asked.
            return CallbackField.GetValue(__instance) != null;
        }
    }

    // --- Host save buffer on a client ------------------------------------------------

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

    // Every profile change (gold, XP, items, ...) logged "Client count is not supported!"
    // as an error once the host had two or more clients. The code after it already handles
    // every client; only the log is wrong. Unity captures a stack trace and writes the log
    // file for each error, and in a fight this ran thousands of times on the host, stalling
    // its frames: positions reached the clients late and characters jumped around.
    [HarmonyPatch(typeof(ProfileManager), "OnProfileModified")]
    internal static class ProfileManager_OnProfileModified
    {
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var codes = new List<CodeInstruction>(instructions);
            int start = codes.FindIndex(c => c.opcode == OpCodes.Ldstr && c.operand is string text && text.StartsWith("Client count is not supported"));
            int call = start < 0 ? -1 : codes.FindIndex(start, c => (c.opcode == OpCodes.Call || c.opcode == OpCodes.Callvirt)
                && c.operand is MethodInfo method && method.DeclaringType == typeof(UnityEngine.Debug) && method.Name == "LogError");
            if (call < 0 || ((MethodInfo)codes[call].operand).GetParameters().Length != 1)
            {
                Plugin.Log.LogError("OnProfileModified: client count log not found");
                return codes;
            }
            // Drop the formatted message instead of logging it.
            codes[call] = new CodeInstruction(OpCodes.Pop).MoveLabelsFrom(codes[call]);
            return codes;
        }
    }
}
