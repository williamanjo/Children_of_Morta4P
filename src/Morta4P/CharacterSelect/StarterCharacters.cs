using HarmonyLib;
using Zyklus.Managers;
using Zyklus.Network;
using Zyklus.Player;
using Zyklus.UI;
using Zyklus.UI.CharacterSelectMenu;

namespace Morta4P
{
    // With 3-4 players the save may not have enough characters unlocked to give everyone one.
    // Only the character select buttons are unlocked: LocalProfile.IsCharacterUnlocked stays
    // untouched, since the story uses it to trigger each character's unlock event.
    internal static class StarterCharacters
    {
        internal static readonly PlayerCharacterEnum[] Characters =
        {
            PlayerCharacterEnum.John,
            PlayerCharacterEnum.Linda,
            PlayerCharacterEnum.Mark,
            PlayerCharacterEnum.Kevin,
        };

        private static readonly AccessTools.FieldRef<CharacterSelect, CharacterButton[]> ButtonsField =
            AccessTools.FieldRefAccess<CharacterSelect, CharacterButton[]>("buttons_");

        internal static bool Active()
        {
            if (!Plugin.UnlockStarterCharacters.Value
                || LobbyManager.sSingleton.pLobbyMode != LobbyManager.LobbyMode.OnlineCoop)
            {
                return false;
            }
            return (PlayerManager.sSingleton?.GetActiveCapsule()?.pPlayerscount ?? 0) > 2;
        }

        internal static void Apply(CharacterSelect menu, bool initial)
        {
            if (!Active())
            {
                return;
            }
            CharacterButton[] buttons = ButtonsField(menu);
            if (buttons == null)
            {
                return;
            }
            foreach (var button in buttons)
            {
                if (button == null || System.Array.IndexOf(Characters, button.pCharacter) < 0)
                {
                    continue;
                }
                if (initial)
                {
                    button.SetInitialLockState(unlock: true);
                }
                else if (button.IsLocked())
                {
                    button.Unlock();
                }
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), "SetInitialCharactersLockStates")]
    internal static class CharacterSelect_SetInitialCharactersLockStates
    {
        private static void Postfix(CharacterSelect __instance)
        {
            try
            {
                StarterCharacters.Apply(__instance, initial: true);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Starter characters (initial): {e}");
            }
        }
    }

    [HarmonyPatch(typeof(CharacterSelect), "HandleCharactersLockStatus")]
    internal static class CharacterSelect_HandleCharactersLockStatus
    {
        private static void Postfix(CharacterSelect __instance)
        {
            try
            {
                StarterCharacters.Apply(__instance, initial: false);
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogError($"Starter characters: {e}");
            }
        }
    }
}
