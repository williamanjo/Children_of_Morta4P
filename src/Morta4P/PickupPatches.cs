using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Talents;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.Rune;
using Zyklus.UI;

namespace Morta4P
{
    // Pickup / item flows that broke with more than one client.

    // DRHungry consumable: the HUD animation RMC is broadcast and every client indexed
    // pPlayersHUD[player.pLocalNumber]; for another client's player that is NotLocal (254)
    // and threw. Only the owner has that HUD.
    [HarmonyPatch(typeof(DRHungryConsumable), "HandleDivineRelicAnimation")]
    internal static class DRHungryConsumable_HandleDivineRelicAnimation
    {
        private static readonly AccessTools.FieldRef<ConsumableBase, PlayerBase> PlayerField =
            AccessTools.FieldRefAccess<ConsumableBase, PlayerBase>("player_");

        private static bool Prefix(DRHungryConsumable __instance)
        {
            PlayerBase player = PlayerField(__instance);
            return player != null && player.pLocalNumber.GetIsLocal();
        }
    }

    // Rune mender: the host opened the mender panel for the interacting player's local
    // number; for a client's player that is NotLocal and indexed the HUD array out of range
    // on the host. The panel only exists for local players.
    [HarmonyPatch(typeof(MenderPanel), nameof(MenderPanel.Show))]
    internal static class MenderPanel_Show
    {
        private static bool Prefix(PlayerLocalNumber player_local_number) => player_local_number.GetIsLocal();
    }

    // Rune talents: TalentLearned adds the rune on every machine that learns the talent, and
    // on a client AddRune forwards to the host, which adds it again. With the talent now
    // relayed to every client, one talent became several runes. Only the host adds the rune;
    // its rune state is replicated to everyone.
    [HarmonyPatch(typeof(RuneTalentAsset), nameof(RuneTalentAsset.TalentLearned))]
    internal static class RuneTalentAsset_TalentLearned
    {
        private static readonly MethodInfo AddRune = AccessTools.Method(typeof(RuneManagerComponent), nameof(RuneManagerComponent.AddRune),
            new[] { typeof(RuneStoneHandle), typeof(float), typeof(bool) });

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            bool done = false;
            foreach (var code in instructions)
            {
                if (!done && code.Calls(AddRune))
                {
                    done = true;
                    yield return new CodeInstruction(System.Reflection.Emit.OpCodes.Call,
                        AccessTools.Method(typeof(RuneTalentAsset_TalentLearned), nameof(AddOnHost))).MoveLabelsFrom(code);
                    continue;
                }
                yield return code;
            }
            if (!done)
            {
                Plugin.Log.LogError("RuneTalentAsset.TalentLearned: AddRune call not found");
            }
        }

        private static void AddOnHost(RuneManagerComponent manager, RuneStoneHandle handle, float durability, bool is_talent)
        {
            if (LobbyManager.sSingleton == null || !LobbyManager.sSingleton.pIsSlave)
            {
                manager.AddRune(handle, durability, is_talent);
            }
        }
    }
}
