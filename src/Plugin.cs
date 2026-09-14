using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace McAutoStore
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("valheim.exe")]
    public class McAutoStorePlugin : BaseUnityPlugin
    {
        public const string Guid = "joaorodrigues.valheim.mcautostore";
        public const string Name = "McAutoStore";
        public const string Version = "1.2.0";

        internal static ManualLogSource Log;

        internal static ConfigEntry<KeyboardShortcut> StoreKey;
        internal static ConfigEntry<float> Range;
        internal static ConfigEntry<bool> OnlyMatchingContainers;
        internal static ConfigEntry<bool> SkipHotbar;
        internal static ConfigEntry<bool> SkipEquipped;
        internal static ConfigEntry<string> IgnoredItems;
        internal static ConfigEntry<bool> ShowMessage;

        internal static ConfigEntry<bool> PullFromGround;
        internal static ConfigEntry<float> GroundRange;
        internal static ConfigEntry<float> GroundScanInterval;
        internal static ConfigEntry<float> GroundMinAge;

        private float _nextScan;

        private void Awake()
        {
            Log = Logger;

            StoreKey = Config.Bind("1 - General", "StoreKey", new KeyboardShortcut(KeyCode.Period),
                "Key that stores your inventory into nearby containers.");

            Range = Config.Bind("1 - General", "Range", 12f,
                new ConfigDescription("Search radius for containers, in meters.",
                    new AcceptableValueRange<float>(2f, 64f)));

            OnlyMatchingContainers = Config.Bind("2 - Rules", "OnlyMatchingContainers", true,
                "true: only store into a container that already holds that item (quick stack behaviour). " +
                "false: store into any container with free space.");

            SkipHotbar = Config.Bind("2 - Rules", "SkipHotbar", true,
                "Never store items from the hotbar (first inventory row).");

            SkipEquipped = Config.Bind("2 - Rules", "SkipEquipped", true,
                "Never store equipped items.");

            IgnoredItems = Config.Bind("2 - Rules", "IgnoredItems", "Hammer,Hoe,Cultivator,SledgeStagbreaker",
                "Comma separated prefab names that are never stored.");

            ShowMessage = Config.Bind("3 - Interface", "ShowMessage", true,
                "Show a centered message with how many items were stored.");

            PullFromGround = Config.Bind("4 - Ground pickup", "PullFromGround", true,
                "Automatically pull items lying on the ground into nearby containers. " +
                "An item is only pulled when a container already holds that same item; otherwise it stays on the ground.");

            GroundRange = Config.Bind("4 - Ground pickup", "GroundRange", 12f,
                new ConfigDescription("Radius around you scanned for dropped items, in meters.",
                    new AcceptableValueRange<float>(2f, 64f)));

            GroundScanInterval = Config.Bind("4 - Ground pickup", "GroundScanInterval", 2f,
                new ConfigDescription("Seconds between ground scans.",
                    new AcceptableValueRange<float>(0.5f, 30f)));

            GroundMinAge = Config.Bind("4 - Ground pickup", "GroundMinAgeSeconds", 3f,
                new ConfigDescription("How long an item must lie on the ground before it can be pulled. " +
                    "Keeps items you just dropped on purpose from being vacuumed instantly.",
                    new AcceptableValueRange<float>(0f, 60f)));

            SlotCompat.Init();
            new Harmony(Guid).PatchAll();
            Log.LogInfo(Name + " " + Version + " loaded. Key: " + StoreKey.Value
                        + " Range: " + Range.Value + "m GroundPickup: " + PullFromGround.Value);
        }

        private void Update()
        {
            if (!Player.m_localPlayerExists) return;

            bool uiBusy = InventoryGui.IsVisible() || Menu.IsVisible() || Chat.instance?.HasFocus() == true;

            if (!uiBusy && StoreKey.Value.IsDown())
                StoreRunner.StoreInventory();

            if (PullFromGround.Value && Time.time >= _nextScan)
            {
                _nextScan = Time.time + GroundScanInterval.Value;
                StoreRunner.PullGroundItems();
            }
        }
    }
}
