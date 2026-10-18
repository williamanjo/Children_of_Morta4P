using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;
using Zyklus.Utilities;

namespace Morta4P
{
    // When a client's player stands on an item or a shop slot, the host showed the
    // description (and the "pick up" panel) on every client. The panels only receive a
    // local number, so the callers record which player it is and the panels send to that
    // player's client only.
    internal static class PanelTarget
    {
        internal static void Set(PlayerBase player) => OwnerSend.Target = OwnerSend.OwnerOf(player);

        internal static void SetFromCollider(object parameter) =>
            Set((parameter as UnityEngine.Component)?.GetComponentInParent<PlayerBase>());

        internal static Exception Clear(Exception exception)
        {
            OwnerSend.Target = null;
            return exception;
        }
    }

    [HarmonyPatch(typeof(InGameInteractionManager), "ShowItemUI")]
    internal static class InGameInteractionManager_ShowItemUI
    {
        private static readonly AccessTools.FieldRef<InGameInteractionManager, PlayerBase> PlayerField =
            AccessTools.FieldRefAccess<InGameInteractionManager, PlayerBase>("player_");

        private static void Prefix(InGameInteractionManager __instance) => PanelTarget.Set(PlayerField(__instance));

        private static Exception Finalizer(Exception __exception) => PanelTarget.Clear(__exception);
    }

    [HarmonyPatch(typeof(InGameInteractionManager), "HideItemUI")]
    internal static class InGameInteractionManager_HideItemUI
    {
        private static readonly AccessTools.FieldRef<InGameInteractionManager, PlayerBase> PlayerField =
            AccessTools.FieldRefAccess<InGameInteractionManager, PlayerBase>("player_");

        private static void Prefix(InGameInteractionManager __instance) => PanelTarget.Set(PlayerField(__instance));

        private static Exception Finalizer(Exception __exception) => PanelTarget.Clear(__exception);
    }

    [HarmonyPatch(typeof(Shop))]
    internal static class Shop_PanelCallers
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string name in new[] { "OnItemTriggerEnter", "OnItemTriggerExit", "OnHealthGlobeTriggerEnter", "OnHealthGlobeTriggerExit" })
            {
                MethodInfo method = AccessTools.Method(typeof(Shop), name);
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        private static void Prefix(object parameter) => PanelTarget.SetFromCollider(parameter);

        private static Exception Finalizer(Exception __exception) => PanelTarget.Clear(__exception);
    }

    [HarmonyPatch(typeof(Shop), nameof(Shop.OnInteractionStart))]
    internal static class Shop_OnInteractionStart
    {
        private static void Prefix(InGameInteractionManager manager) => PanelTarget.Set(manager?.pInteractionOwner);

        private static Exception Finalizer(Exception __exception) => PanelTarget.Clear(__exception);
    }

    [HarmonyPatch]
    internal static class ItemPanels_Redirect
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(ItemComparisonPanel), nameof(ItemComparisonPanel.ShowDescription),
                new[] { typeof(IAddressableNetGO), typeof(PlayerLocalNumber), typeof(int) });
            yield return AccessTools.Method(typeof(ItemComparisonPanel), nameof(ItemComparisonPanel.ShowHealthDescription));
            yield return AccessTools.Method(typeof(ItemComparisonPanel), nameof(ItemComparisonPanel.HideDescription));
            yield return AccessTools.Method(typeof(ItemComparisonPanel), nameof(ItemComparisonPanel.HideItemComparision));
            yield return AccessTools.Method(typeof(GamblerPanel), nameof(GamblerPanel.Show));
            yield return AccessTools.Method(typeof(GamblerPanel), nameof(GamblerPanel.Hide));
        }

        // Each of these broadcasts once; the parameter count tells which overload it uses.
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
        {
            var codes = new List<CodeInstruction>(instructions);
            foreach (var code in codes)
            {
                if (code.operand is MethodInfo called && called.DeclaringType == typeof(IRMCBridgeExtensions)
                    && called.Name == nameof(IRMCBridgeExtensions.SendRMCToSlaves))
                {
                    return OwnerSend.RedirectFirstBroadcast(codes, called.GetParameters().Length, __originalMethod.Name);
                }
            }
            Plugin.Log.LogError($"{__originalMethod.Name}: broadcast not found");
            return codes;
        }
    }
}
