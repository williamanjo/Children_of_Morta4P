using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Morta4P
{
    [BepInPlugin(Guid, Name, Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.william.morta4p";
        public const string Name = "Morta4P";
        public const string Version = "0.9.4";

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> MaxPlayers;
        internal static ConfigEntry<float> PanelScale;
        internal static ConfigEntry<int> XPSplitMax;
        internal static ConfigEntry<float> PanelSpacing;
        internal static ConfigEntry<bool> UnlockStarterCharacters;
        internal static ConfigEntry<float> HudSpacing;
        internal static ConfigEntry<HudDirection> HudStackDirection;
        internal static ConfigEntry<float> LobbySpacing;
        internal static ConfigEntry<KeyCode> PreviewKey;
        internal static ConfigEntry<KeyCode> ReloadKey;

        internal static readonly Color P3Color = new Color(0.45f, 0.85f, 0.45f);
        internal static readonly Color P4Color = new Color(0.95f, 0.75f, 0.3f);

        // Set for one frame after a config reload so layouts are recomputed.
        internal static bool LayoutDirty;

        // Max number of remote clients (slaves) the host accepts.
        internal static int MaxSlaves => MaxPlayers.Value - 1;

        private void Awake()
        {
            Log = Logger;
            settings_ = OpenSettings();
            MaxPlayers = settings_.Bind("General", "MaxPlayers", 4,
                new ConfigDescription("Total online players including the host. Every player must use the same value.",
                    new AcceptableValueRange<int>(2, 4)));
            XPSplitMax = settings_.Bind("Gameplay", "XPSplitMax", 2,
                "XP from a kill is divided by the number of players, capped at this value, and every player gets that share. 2: 3-4 players level like 2-player co-op. 0: divide by every player (original).");
            PanelScale = settings_.Bind("CharacterSelect", "PanelScale", 0.5f,
                new ConfigDescription("Scale of the character panels when more than 2 players are in the lobby.",
                    new AcceptableValueRange<float>(0.3f, 1f)));
            PanelSpacing = settings_.Bind("CharacterSelect", "PanelSpacing", 4f,
                "Vertical gap between a panel and the P3/P4 panel under it (UI units).");
            UnlockStarterCharacters = settings_.Bind("CharacterSelect", "UnlockStarterCharacters", true,
                "With 3 or more players online, John, Linda, Mark and Kevin can be picked even if the save has not unlocked them yet. The save itself is not changed.");
            HudSpacing = settings_.Bind("HUD", "Spacing", 14f,
                "Gap between stacked partner HUDs (UI units).");
            HudStackDirection = settings_.Bind("HUD", "StackDirection", HudDirection.Auto,
                "Where extra partner HUDs go relative to the first one. Auto: down if the HUD is in the upper half of the screen, otherwise up.");
            LobbySpacing = settings_.Bind("Lobby", "Spacing", 4f,
                "Gap between player entries in the lobby widget of the pause menu (UI units).");
            PreviewKey = settings_.Bind("Debug", "PreviewKey", KeyCode.F9,
                "Toggles a solo preview of the P3/P4 panels (character select), the partner HUDs (in a run) and the lobby widget (pause menu).");
            ReloadKey = settings_.Bind("Debug", "ReloadKey", KeyCode.F10,
                "Reloads this config file and re-applies the layout.");

            var harmony = new Harmony(Guid);
            // Patch class by class so one failing patch (e.g. after a game update) only
            // disables that fix instead of the whole mod.
            int failed = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                }
                catch (System.Exception e)
                {
                    failed++;
                    Log.LogError($"Patch {type.Name} failed: {e.InnerException?.Message ?? e.Message}");
                }
            }
            if (failed > 0)
            {
                Log.LogError($"{failed} patch(es) failed; the rest of the mod is active");
            }

            foreach (var method in harmony.GetPatchedMethods())
            {
                Log.LogInfo($"Patched {method.DeclaringType?.FullName}.{method.Name}");
            }
            Log.LogInfo($"{Name} {Version} loaded. MaxPlayers={MaxPlayers.Value}");
        }

        public const string SettingsFileName = "morta4p.cfg";

        private ConfigFile settings_;

        private static bool updateErrorLogged_;

        // Settings live in BepInEx\config\morta4p.cfg instead of the GUID-named default.
        // A file from older versions is moved over so existing values are kept.
        private ConfigFile OpenSettings()
        {
            string path = System.IO.Path.Combine(Paths.ConfigPath, SettingsFileName);
            string legacy = System.IO.Path.Combine(Paths.ConfigPath, Guid + ".cfg");
            try
            {
                if (!System.IO.File.Exists(path) && System.IO.File.Exists(legacy))
                {
                    System.IO.File.Move(legacy, path);
                    Logger.LogInfo($"Moved {Guid}.cfg to {SettingsFileName}");
                }
            }
            catch (System.Exception e)
            {
                Logger.LogWarning($"Could not move old config: {e.Message}");
            }
            return new ConfigFile(path, saveOnInit: true, Info.Metadata);
        }

        private void Update()
        {
            if (Input.GetKeyDown(ReloadKey.Value))
            {
                settings_.Reload();
                LayoutDirty = true;
                HudStack.Relayout();
                MiniLobbyWidget.Place();
                Log.LogInfo("Config reloaded");
            }
            if (Input.GetKeyDown(PreviewKey.Value))
            {
                PanelLayout.Preview = !PanelLayout.Preview;
                LayoutDirty = true;
                HudStack.TogglePreview();
                MiniLobbyWidget.TogglePreview();
                Log.LogInfo($"Panel preview {(PanelLayout.Preview ? "on" : "off")}");
            }

            try
            {
                PanelLayout.Update();
                ProfileManager_FetchingHostSaveBufferDone.Update();
            }
            catch (System.Exception e)
            {
                if (!updateErrorLogged_)
                {
                    updateErrorLogged_ = true;
                    Log.LogError($"Panel layout update: {e}");
                }
            }
            LayoutDirty = false;
        }
    }
}
