using HarmonyLib;
using UnityEngine;
using Zyklus.Loot;
using Zyklus.Player;

namespace Morta4P
{
    // Drops meant for every player were flagged for P1 | P2 only; P3 and P4 could not see
    // or pick them up. Flag them for all players.

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropDivineRelic),
        new[] { typeof(DivineRelicHandle), typeof(Vector2), typeof(Vector2), typeof(PlayerNumberFlag), typeof(bool), typeof(bool) })]
    internal static class Loot_DropDivineRelic
    {
        private static void Prefix(ref PlayerNumberFlag player_flags)
        {
            if (player_flags == (PlayerNumberFlag.P1 | PlayerNumberFlag.P2))
            {
                player_flags = PlayerNumberExtensions.sAllPlayers;
            }
        }
    }

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropConsumable),
        new[] { typeof(Vector2), typeof(Vector2), typeof(ConsumableInvetoryItemAsset), typeof(PlayerNumberFlag), typeof(bool) })]
    internal static class Loot_DropConsumable
    {
        private static void Prefix(ref PlayerNumberFlag player_number_flag)
        {
            if (player_number_flag == (PlayerNumberFlag.P1 | PlayerNumberFlag.P2))
            {
                player_number_flag = PlayerNumberExtensions.sAllPlayers;
            }
        }
    }

    [HarmonyPatch(typeof(LootStaticDataContainer), nameof(LootStaticDataContainer.DropRune),
        new[] { typeof(int), typeof(Vector2), typeof(Vector2), typeof(PlayerNumber) })]
    internal static class Loot_DropRune
    {
        private static void Prefix(ref PlayerNumber players_flag)
        {
            if (players_flag == (PlayerNumber.P1 | PlayerNumber.P2))
            {
                players_flag = PlayerNumber.All;
            }
        }
    }
}
