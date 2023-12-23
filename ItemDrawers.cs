using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using JetBrains.Annotations;
using PieceManager;
using ServerSync;
using UnityEngine;

namespace ItemDrawers
{
    [BepInPlugin(GUID, GUID, VERSION)]
    public class ItemDrawers : BaseUnityPlugin
    {
        private const string GUID = "kg.ItemDrawers";
        private const string VERSION = "1.0.3";
        private static ConfigSync configSync = new(GUID) { DisplayName = GUID, CurrentVersion = VERSION, MinimumRequiredVersion = VERSION, ModRequired = true, IsLocked = true};
        public static ItemDrawers _thistype;
        private static AssetBundle asset;

        public static ConfigEntry<int> DrawerPickupRange;
        private static ConfigEntry<string> IncludeList;
        public static HashSet<string> IncludeSet = new();
        private static BuildPiece _drawer_wood;
        private static BuildPiece _drawer_stone;
        private static BuildPiece _drawer_marble;
        public static GameObject Explosion;
     
        
        private void Awake()
        {
            _thistype = this;
            
            IncludeList = config("General", "IncludeList", "DragonEgg", "List of items with max stack size 1 to include in the drawer. Leave blank to include all items.");
            IncludeList.SettingChanged += ResetList;
            ResetList(null, null);
            DrawerPickupRange = config("General", "DrawerPickupRange", 4, "Range at which you can pick up items from the drawer.");
            
            asset = GetAssetBundle("kg_itemdrawers");
            
            Explosion = asset.LoadAsset<GameObject>("kg_ItemDrawer_Explosion");
             
            _drawer_wood = new BuildPiece(asset, "kg_ItemDrawer_Wood"); 
            _drawer_wood.Name.English("Wooden Item Drawer");
            _drawer_wood.Prefab.AddComponent<DrawerComponent>();
            _drawer_wood.Category.Set("Item Drawers");
            _drawer_wood.Crafting.Set(CraftingTable.None);
            _drawer_wood.RequiredItems.Add("Wood", 10, true);
            
            _drawer_stone = new BuildPiece(asset, "kg_ItemDrawer_Stone");
            _drawer_stone.Name.English("Stone Item Drawer");
            _drawer_stone.Prefab.AddComponent<DrawerComponent>();
            _drawer_stone.Category.Set("Item Drawers");
            _drawer_stone.Crafting.Set(CraftingTable.None); 
            _drawer_stone.RequiredItems.Add("Stone", 10, true); 
             
            _drawer_marble = new BuildPiece(asset, "kg_ItemDrawer_Marble");
            _drawer_marble.Name.English("Marble Item Drawer");
            _drawer_marble.Prefab.AddComponent<DrawerComponent>();
            _drawer_marble.Category.Set("Item Drawers");
            _drawer_marble.Crafting.Set(CraftingTable.None);
            _drawer_marble.RequiredItems.Add("BlackMarble", 10, true); 
            
            new Harmony(GUID).PatchAll(); 
        }  
    
        [HarmonyPatch(typeof(ZNetScene),nameof(ZNetScene.Awake))]
        private static class ZNetScene_Awake_Patch
        {
            [UsedImplicitly]
            private static void Postfix(ZNetScene __instance) 
            {
                __instance.m_namedPrefabs[Explosion.name.GetStableHashCode()] = Explosion;

                _drawer_wood.Prefab.GetComponent<Piece>().m_placeEffect = __instance.GetPrefab("woodwall").GetComponent<Piece>().m_placeEffect;
                _drawer_stone.Prefab.GetComponent<Piece>().m_placeEffect = __instance.GetPrefab("stone_wall_1x1").GetComponent<Piece>().m_placeEffect;
                _drawer_marble.Prefab.GetComponent<Piece>().m_placeEffect = __instance.GetPrefab("blackmarble_1x1").GetComponent<Piece>().m_placeEffect;
            }
        }
        
        [HarmonyPatch(typeof(AudioMan), nameof(AudioMan.Awake))] 
        private static class AudioMan_Awake_Patch
        {
            [UsedImplicitly]
            private static void Postfix(AudioMan __instance) 
            {
                var SFXgroup = __instance.m_masterMixer.FindMatchingGroups("SFX")[0];
                foreach (GameObject go in asset.LoadAllAssets<GameObject>())
                {
                    foreach (AudioSource audioSource in go.GetComponentsInChildren<AudioSource>(true))
                        audioSource.outputAudioMixerGroup = SFXgroup;
                }
            }
        }

        private void ResetList(object sender, EventArgs eventArgs) => 
            IncludeSet = new HashSet<string>(IncludeList.Value.Replace(" ", "").Split(','));
        
        private static AssetBundle GetAssetBundle(string filename)
        {
            Assembly execAssembly = Assembly.GetExecutingAssembly();
            string resourceName = execAssembly.GetManifestResourceNames().Single(str => str.EndsWith(filename));
            using Stream stream = execAssembly.GetManifestResourceStream(resourceName)!;
            return AssetBundle.LoadFromStream(stream);
        }
        
        ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

    }
}