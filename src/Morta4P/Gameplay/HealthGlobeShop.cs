using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Player;

namespace Morta4P
{
    // The shop loops over every standing player but heals the buyer each time, so the buyer
    // got healed once per player and the others not at all. Heal each standing player once.
    [HarmonyPatch(typeof(Shop), nameof(Shop.OnHealthGlobeInteractionStart))]
    internal static class Shop_OnHealthGlobeInteractionStart
    {
        private static readonly MethodInfo HealthGlobeAcquired =
            AccessTools.Method(typeof(PlayerBase), nameof(PlayerBase.HealthGlobeAcquired));

        [ThreadStatic]
        private static bool healed_;

        private static void Prefix() => healed_ = false;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(HealthGlobeAcquired))
                {
                    done = true;
                    yield return new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(Shop_OnHealthGlobeInteractionStart), nameof(HealAll)))
                        .MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError("Shop.OnHealthGlobeInteractionStart: heal call not found");
            }
        }

        // Replaces "buyer.HealthGlobeAcquired(heal, mana)" inside the per-player loop.
        private static void HealAll(PlayerBase buyer, float heal_value, float mana_value)
        {
            if (healed_)
            {
                return;
            }
            healed_ = true;
            foreach (var player in PlayerManager.sSingleton.GetPlayers())
            {
                if (player != null && !player.pIsInLimbo)
                {
                    player.HealthGlobeAcquired(heal_value, mana_value);
                }
            }
        }
    }
}
