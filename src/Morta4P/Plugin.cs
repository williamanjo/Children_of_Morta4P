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
        public const string Version = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigEntry<int> MaxPlayers;
        internal static ConfigEntry<float> PanelScale;
        internal static ConfigEntry<float> PanelSpacing;
        internal static ConfigEntry<float> HudSpacing;
        internal static ConfigEntry<HudDirection> HudStackDirection;
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
            MaxPlayers = Config.Bind("General", "MaxPlayers", 4,
                new ConfigDescription("Total online players including the host. Every player must use the same value.",
                    new AcceptableValueRange<int>(2, 4)));
            PanelScale = Config.Bind("CharacterSelect", "PanelScale", 0.5f,
                new ConfigDescription("Scale of the character panels when more than 2 players are in the lobby.",
                    new AcceptableValueRange<float>(0.3f, 1f)));
            PanelSpacing = Config.Bind("CharacterSelect", "PanelSpacing", 4f,
                "Vertical gap between a panel and the P3/P4 panel under it (UI units).");
            HudSpacing = Config.Bind("HUD", "Spacing", 14f,
                "Gap between stacked partner HUDs (UI units).");
            HudStackDirection = Config.Bind("HUD", "StackDirection", HudDirection.Auto,
                "Where extra partner HUDs go relative to the first one. Auto: down if the HUD is in the upper half of the screen, otherwise up.");
            PreviewKey = Config.Bind("Debug", "PreviewKey", KeyCode.F9,
                "Toggles a solo preview of the P3/P4 panels (character select) and the partner HUDs (in a run).");
            ReloadKey = Config.Bind("Debug", "ReloadKey", KeyCode.F10,
                "Reloads this config file and re-applies the layout.");

            var harmony = new Harmony(Guid);
            harmony.PatchAll(typeof(Plugin).Assembly);

            foreach (var method in harmony.GetPatchedMethods())
            {
                Log.LogInfo($"Patched {method.DeclaringType?.FullName}.{method.Name}");
            }
            Log.LogInfo($"{Name} {Version} loaded. MaxPlayers={MaxPlayers.Value}");
        }

        private void Update()
        {
            if (Input.GetKeyDown(ReloadKey.Value))
            {
                Config.Reload();
                LayoutDirty = true;
                HudStack.Relayout();
                Log.LogInfo("Config reloaded");
            }
            if (Input.GetKeyDown(PreviewKey.Value))
            {
                PanelLayout.Preview = !PanelLayout.Preview;
                LayoutDirty = true;
                HudStack.TogglePreview();
                Log.LogInfo($"Panel preview {(PanelLayout.Preview ? "on" : "off")}");
            }

            PanelLayout.Update();
            LayoutDirty = false;
        }
    }
}
