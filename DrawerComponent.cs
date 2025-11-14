using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

namespace kg_ItemDrawers;

public class DrawerComponent : MonoBehaviour, Interactable, Hoverable
{
    public static readonly List<DrawerComponent> AllDrawers = [];
    private static Sprite _defaultSprite;
    public ZNetView _znv { private set; get; }
    private Image _image;
    private TMP_Text _text;
    private Transform _stars;
    private TMP_Text _starsText;
    private Transform _starIcon;
    
    //UI
    private static bool ShowUI;
    private static DrawerOptions CurrentOptions;
    //

    public string CurrentPrefab
    {
        get => _znv.m_zdo.GetString("Prefab");
        set => _znv.m_zdo.Set("Prefab", value);
    }

    public int CurrentAmount
    {
        get => _znv.m_zdo.GetInt("Amount");
        set => _znv.m_zdo.Set("Amount", value);
    }

    public int PickupRange
    {
        get => _znv.m_zdo.GetInt("PickupRange", ItemDrawers.DrawerPickupRange.Value);
        set => _znv.m_zdo.Set("PickupRange", value);
    }

    public int Quality
    {
        get => _znv.m_zdo.GetInt("Quality", 1);
        set => _znv.m_zdo.Set("Quality", value);
    }
    
    private Color CurrentColor 
    {
        get => global::Utils.Vec3ToColor(_znv.m_zdo.GetVec3("Color", ItemDrawers.DefaultColor.Value));
        set => _znv.m_zdo.Set("Color", global::Utils.ColorToVec3(value));
    }


    public bool ItemValid => !string.IsNullOrEmpty(CurrentPrefab) && ObjectDB.instance.m_itemByHash.ContainsKey(CurrentPrefab.GetStableHashCode());
    public bool ItemValidCheck(string prefab) => !string.IsNullOrEmpty(prefab) && ObjectDB.instance.m_itemByHash.ContainsKey(prefab.GetStableHashCode());
    private int ItemMaxStack => ObjectDB.instance.m_itemByHash[CurrentPrefab.GetStableHashCode()].GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
    private string LocalizedName => ObjectDB.instance.m_itemByHash[CurrentPrefab.GetStableHashCode()].GetComponent<ItemDrop>().m_itemData.m_shared.m_name.Localize();

    private struct DrawerOptions : ISerializableParameter
    {
        public DrawerComponent drawer;
        public Color32 color;
        public int pickupRange;

        public void Serialize(ref ZPackage pkg)
        {
            pkg.Write(global::Utils.ColorToVec3(color));
            pkg.Write(pickupRange);
        }

        public void Deserialize(ref ZPackage pkg)
        {
            color = global::Utils.Vec3ToColor(pkg.ReadVector3());
            pickupRange = pkg.ReadInt();
        }
    }

    private void OnDestroy() => AllDrawers.Remove(this);
    private void Awake()
    {
        _znv = GetComponent<ZNetView>();
        if (!_znv.IsValid()) return;
        AllDrawers.Add(this);
        _image = transform.Find("Cube/Canvas/Image").GetComponent<Image>();
        _defaultSprite ??= _image.sprite;
        _text = transform.Find("Cube/Canvas/Text").GetComponent<TMP_Text>();
        _text.color = CurrentColor;
        _stars = transform.Find("Cube/Canvas/Stars");
        _starsText = _stars.Find("Quality").GetComponent<TMP_Text>();
        _starIcon = _stars.Find("Img");
        _znv.Register<string, int, int>("AddItem_Request", RPC_AddItem);
        _znv.Register<string, int, int>("AddItem_Player", RPC_AddItem_Player);
        _znv.Register<int>("WithdrawItem_Request", RPC_WithdrawItem_Request);
        _znv.Register<string, int, int>("UpdateIcon", RPC_UpdateIcon);
        _znv.Register<int>("ForceRemove", RPC_ForceRemove);
        _znv.Register<DrawerOptions>("ApplyOptions", RPC_ApplyOptions);
        RPC_UpdateIcon(0, CurrentPrefab, CurrentAmount, Quality);
        float randomTime = Random.Range(2.5f, 3f);
        InvokeRepeating(nameof(Repeat), randomTime, randomTime);
    }

    private void RPC_ApplyOptions(long sender, DrawerOptions options)
    {
        if (_znv.IsOwner())
        {
            CurrentColor = options.color;
            PickupRange = Mathf.Min(ItemDrawers.MaxDrawerPickupRange.Value, options.pickupRange);
        }
        _text.color = options.color;
    }

    private void RPC_ForceRemove(long sender, int amount)
    {
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount, Quality);
    }

    private void RPC_WithdrawItem_Request(long sender, int amount)
    {
        if (CurrentAmount <= 0 || !ItemValid)
        {
            CurrentPrefab = "";
            CurrentAmount = 0;
            Quality = 1;
            _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", "", 0, 0);
            return;
        }

        if (amount <= 0) return;
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        _znv.InvokeRPC(sender, "AddItem_Player", CurrentPrefab, amount, Quality);
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount, Quality);
    }

    private void RPC_AddItem_Player(long _, string prefab, int amount, int quality) => Utils.InstantiateItem(ZNetScene.instance.GetPrefab(prefab), amount, quality);

    private void RPC_UpdateIcon(long _, string prefab, int amount, int quality)
    {
        if (!ItemValidCheck(prefab))
        {
            _image.sprite = _defaultSprite;
            _stars.gameObject.SetActive(false);
            _text.gameObject.SetActive(false);
            return; 
        }
        _image.sprite = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.GetIcon();
        _text.text = amount.ToString();
        _text.gameObject.SetActive(true);
        
        _stars.gameObject.SetActive(quality > 1);
        if (quality > 1)
        {
            _starsText.text = quality.ToString();
            RectTransform starRect = _starIcon.GetComponent<RectTransform>();
            _starIcon.localPosition = new Vector3(quality >= 10 ? 9f : 0f, 5.25f, 0f);
        }
    }

    private void RPC_AddItem(long sender, string prefab, int amount, int quality)
    {
        if (!_znv.IsOwner()) return;
        if (amount <= 0) return;
        quality = Math.Max(1, quality);
        string currentPrefab = CurrentPrefab;
        if (ItemValid && (currentPrefab != prefab || Quality != quality))
        {
            Utils.InstantiateAtPos(ZNetScene.instance.GetPrefab(currentPrefab), CurrentAmount, Quality, transform.position + Vector3.up * 1.5f);
            return;
        }
         
        int newAmount = ItemValid ? (CurrentAmount + amount) : amount;
        CurrentAmount = newAmount;
        if (currentPrefab != prefab)
        {
            CurrentPrefab = prefab;
            Quality = quality;
        }
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", prefab, newAmount, quality);
    }

    private bool DoRepeat => Player.m_localPlayer && ItemValid && PickupRange > 0;
    private void Repeat()
    {
        if (!_znv.IsOwner()) return;
        if (!DoRepeat) return;
        string prefab = CurrentPrefab;
        int quality = Quality;
        Vector3 vector = transform.position + Vector3.up;
        foreach (ItemDrop component in ItemDrop.s_instances.Where(drop => Vector3.Distance(drop.transform.position, vector) < PickupRange))
        {
            string goName = component.m_itemData.m_dropPrefab.name;
            if (goName != prefab) continue;
            if (component.m_itemData.m_quality != quality) continue;
            if (!component.CanPickup(false))
            { 
                component.RequestOwn();
                continue; 
            }

            Instantiate(ItemDrawers.Explosion, component.transform.position, Quaternion.identity);
            int amount = component.m_itemData.m_stack;
            component.m_nview.ClaimOwnership();
            ZNetScene.instance.Destroy(component.gameObject);
            CurrentAmount += amount;
            _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", prefab, CurrentAmount, quality);
        }
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (!PrivateArea.CheckAccess(transform.position))
            return true;
        
        if (user.IsCrouching())
        {
            CurrentOptions.drawer = this;
            CurrentOptions.color = CurrentColor;
            CurrentOptions.pickupRange = PickupRange;
            ShowUI = true;
            return true;
        }
        
        if (!ItemValid) return false;

        if (Input.GetKey(KeyCode.LeftAlt))
        {
            _znv.InvokeRPC("WithdrawItem_Request", 1);
            return true;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            int quality = Quality;
            int amount = Utils.CustomCountItems(CurrentPrefab, quality);
            if (amount <= 0) return true;
            Utils.CustomRemoveItems(CurrentPrefab, amount, quality);
            _znv.InvokeRPC("AddItem_Request", CurrentPrefab, amount, quality);
            return true;
        }

        _znv.InvokeRPC("WithdrawItem_Request", ItemMaxStack);
        return true;
    }


    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        if (item.m_customData.Count > 0)
        {
            MessageHud.instance.ShowMessage(MessageHud.MessageType.Center, "<color=red><b>Cannot store items with custom data</b></color>");
            return false;
        }
        string dropPrefab = item.m_dropPrefab?.name;
        if (string.IsNullOrEmpty(dropPrefab)) return false;

        if (ItemDrawers.ExcludeSet.Contains(dropPrefab) && !Player.m_debugMode) return false;

        if (!string.IsNullOrEmpty(CurrentPrefab) && (CurrentPrefab != dropPrefab || Quality != item.m_quality)) return false;

        int amount = item.m_stack;
        if (amount <= 0) return false;
        user.m_inventory.RemoveItem(item);
        _znv.InvokeRPC("AddItem_Request", dropPrefab, amount, item.m_quality);
        return true;
    }

    public string GetHoverText()
    {
        StringBuilder sb = new StringBuilder();
        if (Player.m_localPlayer.IsCrouching())
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] open settings");
            return sb.ToString().Localize();
        }
        
        if (!ItemValid)
        {
            sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>");
            return sb.ToString().Localize();
        }
        int quality = Quality;
        string qualityString = quality > 1 ? $" (Quality: {quality})" : "";
        sb.AppendLine($"<color=yellow><b>{LocalizedName}{qualityString}</b></color> ({CurrentAmount})");
        sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>\n");
        if (CurrentAmount <= 0)
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] or [<color=yellow><b>Left Alt + $KEY_Use</b></color>] to clear");
            sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}</b></color> ({Utils.CustomCountItems(CurrentPrefab, quality)})");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] to withdraw stack ({ItemMaxStack})");
        sb.AppendLine($"[<color=yellow><b>Left Alt + $KEY_Use</b></color>] to withdraw single item");
        sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}{qualityString}</b></color> ({Utils.CustomCountItems(CurrentPrefab, quality)})");
        return sb.ToString().Localize();
    }

    public string GetHoverName()
    {
        return "Item Drawer";
    }
    
    private const int windowWidth = 300;
    private const int windowHeight = 300;
    private const int halfWindowWidth = windowWidth / 2;
    private const int halfWindowHeight = windowHeight / 2;
    public static void ProcessInput()
    {
        if (Input.GetKeyDown(KeyCode.Escape) && ShowUI)
        {
            ShowUI = false;
            Menu.instance.OnClose();
        }
    }
    public static void ProcessGUI()
    {
        if (!ShowUI) return;
        GUI.backgroundColor = Color.white;
        Rect centerOfScreen = new(Screen.width / 2f - halfWindowWidth, Screen.height / 2f - halfWindowHeight, windowWidth, windowHeight);
        GUI.Window(218102318, centerOfScreen, Window, "Item Drawer Options");
    }
    private static void Window(int id)
    {
        if (CurrentOptions.drawer == null || !CurrentOptions.drawer._znv.IsValid())
        {
            ShowUI = false;
            return;
        }
        GUILayout.Label($"Current Drawer: <color=yellow><b>{CurrentOptions.drawer.LocalizedName}</b></color> ({CurrentOptions.drawer.CurrentAmount})");
        byte r = CurrentOptions.color.r;
        byte g = CurrentOptions.color.g;
        byte b = CurrentOptions.color.b;
        GUILayout.Label($"Text Color: <color=#{r:X2}{g:X2}{b:X2}><b>0123456789</b></color>");
        GUILayout.Label($"R: {r}");
        r = (byte)GUILayout.HorizontalSlider(r, 0, 255);
        GUILayout.Label($"G: {g}");
        g = (byte)GUILayout.HorizontalSlider(g, 0, 255);
        GUILayout.Label($"B: {b}");
        b = (byte)GUILayout.HorizontalSlider(b, 0, 255);
        CurrentOptions.color = new Color32(r, g, b, 255);
        int pickupRange = CurrentOptions.pickupRange;
        GUILayout.Space(16f);
        GUILayout.Label($"Pickup Range: <color={(pickupRange > 0 ? "lime" : "red")}><b>{pickupRange}</b></color>"); 
        pickupRange = (int)GUILayout.HorizontalSlider(pickupRange, 0, ItemDrawers.MaxDrawerPickupRange.Value);
        CurrentOptions.pickupRange = pickupRange;
        GUILayout.Space(16f);
        if (GUILayout.Button("<color=lime>Apply</color>"))
        {
            CurrentOptions.drawer._znv.InvokeRPC(ZNetView.Everybody, "ApplyOptions", CurrentOptions);
            ShowUI = false;
        }
    }

    [HarmonyPatch]
    private static class IsVisible
    {
        [HarmonyTargetMethods, UsedImplicitly]
        private static IEnumerable<MethodInfo> Methods()
        {
            yield return AccessTools.Method(typeof(TextInput), nameof(TextInput.IsVisible));
            yield return AccessTools.Method(typeof(StoreGui), nameof(StoreGui.IsVisible));
        }
        
        [HarmonyPostfix, UsedImplicitly]
        private static void SetTrue(ref bool __result) => __result |= ShowUI;
    }
}

[HarmonyPatch(typeof(Piece), nameof(Piece.DropResources))]
public static class Piece_OnDestroy_Patch
{
    [UsedImplicitly]
    private static void Postfix(Piece __instance)
    {
        if (__instance.gameObject.GetComponent<DrawerComponent>() is { } drawer)
        {
            if (drawer.ItemValid && drawer.CurrentAmount > 0)
            {
                Utils.InstantiateAtPos(ZNetScene.instance.GetPrefab(drawer.CurrentPrefab), drawer.CurrentAmount, 1, __instance.transform.position + Vector3.up);
            }
        }
    }
}

