using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Altar.FMOD;
using HarmonyLib;
using UnityEngine;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.Sound;

namespace Morta4P
{
    // In the original co-op both players shared one camera, so another player's sounds were
    // always "nearby". With each player on their own camera, sounds of a player on the other
    // side of the map were heard at full volume. Sounds emitted by another player's character
    // are now skipped when that character is farther than Sound.OtherPlayerMaxDistance from
    // this machine's own player. World sounds and the local player's sounds are untouched.
    internal static class PlayerSoundFilter
    {
        private static readonly ConditionalWeakTable<ZyklusSoundEmitter, PlayerBase> owners_ =
            new ConditionalWeakTable<ZyklusSoundEmitter, PlayerBase>();

        internal static bool ShouldMute(ZyklusSoundEmitter emitter, bool hasCallback)
        {
            float maxDistance = Plugin.OtherPlayerSoundDistance.Value;
            LobbyManager lobby = LobbyManager.sSingleton;
            if (maxDistance <= 0f || lobby == null || lobby.pLobbyMode != LobbyManager.LobbyMode.OnlineCoop)
            {
                return false;
            }
            if (!owners_.TryGetValue(emitter, out PlayerBase owner))
            {
                owner = emitter.GetComponentInParent<PlayerBase>();
                owners_.Add(emitter, owner);
            }
            if (owner == null || owner.pLocalNumber.GetIsLocal())
            {
                return false;
            }
            // On the host, sounds with a stop callback may drive gameplay; keep them.
            if (lobby.pIsMaster && hasCallback)
            {
                return false;
            }
            List<PlayerBase> locals = PlayerManager.sSingleton?.GetLocalPlayers();
            if (locals == null || locals.Count == 0 || locals[0] == null)
            {
                return false;
            }
            Vector2 offset = (Vector2)(owner.transform.position - locals[0].transform.position);
            return offset.sqrMagnitude > maxDistance * maxDistance;
        }
    }

    [HarmonyPatch(typeof(ZyklusSoundEmitter), nameof(ZyklusSoundEmitter.PlayCurrentEvent_Locally))]
    internal static class ZyklusSoundEmitter_PlayCurrentEvent_Locally
    {
        private static bool Prefix(ZyklusSoundEmitter __instance, FMODEventStoppedDelegate on_stopped_callback) =>
            !PlayerSoundFilter.ShouldMute(__instance, on_stopped_callback != null);
    }

    [HarmonyPatch(typeof(ZyklusSoundEmitter), nameof(ZyklusSoundEmitter.Play_Locally))]
    internal static class ZyklusSoundEmitter_Play_Locally
    {
        private static bool Prefix(ZyklusSoundEmitter __instance, FMODEventStoppedDelegate on_stopped_callback, ref SoundEmitter __result)
        {
            if (!PlayerSoundFilter.ShouldMute(__instance, on_stopped_callback != null))
            {
                return true;
            }
            __result = null;
            return false;
        }
    }
}
