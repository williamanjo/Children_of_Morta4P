using System;
using System.Reflection;
using Altar.Networking;
using HarmonyLib;
using UnityEngine;
using Zyklus.CameraManager;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;

namespace Morta4P
{
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
}
