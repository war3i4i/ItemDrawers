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

[UsedImplicitly]
public class DrawerComponent : Container, Interactable, Hoverable
{
    private const string ZDO_Prefab = "Prefab";
    private const string ZDO_Amount = "Amount";
    private const string ZDO_Color = "Color";
    private const string ZDO_PickupRange = "PickupRange";
    public static readonly List<DrawerComponent> AllDrawers = new();

    private static Sprite _defaultSprite;
    private static bool ShowUI;
    private static DrawerOptions CurrentOptions;
    private Image _image;
    private TMP_Text _text;
    private Color m_color;
    private int m_pickupRange;
    internal int m_storedAmount;

    // === Persistent fields ===
    internal string m_storedPrefab;

    // === Unity / Container Initialization ===
    private new void Awake()
    {
        base.Awake();
        m_name = "Item Drawer";

        if (!m_nview || m_nview.GetZDO() == null)
            return;

        AllDrawers.Add(this);

        // remove normal container RPCs that open GUI
        m_nview.Unregister("RequestOpen");
        m_nview.Unregister("OpenRespons");
        m_nview.Unregister("RPC_RequestStack");
        m_nview.Unregister("RPC_StackResponse");
        m_nview.Unregister("RequestTakeAll");
        m_nview.Unregister("TakeAllRespons");

        // register our own RPCs
        m_nview.Register<string, int>("AddItem_Request", RPC_AddItem);
        m_nview.Register<int>("WithdrawItem_Request", RPC_WithdrawItem_Request);
        m_nview.Register<DrawerOptions>("ApplyOptions", RPC_ApplyOptions);
        m_nview.Register<int>("ForceRemove", RPC_ForceRemove); // required for CraftFromContainers

        // initialize fields
        m_storedPrefab = m_nview.GetZDO().GetString(ZDO_Prefab);
        m_storedAmount = m_nview.GetZDO().GetInt(ZDO_Amount);
        m_pickupRange = m_nview.GetZDO().GetInt(ZDO_PickupRange, ItemDrawers.DrawerPickupRange.Value);
        m_color = global::Utils.Vec3ToColor(m_nview.GetZDO().GetVec3(ZDO_Color, ItemDrawers.DefaultColor.Value));

        // inventory setup
        if (m_inventory == null)
            m_inventory = new Inventory(m_name, null, 1, 1);

        // UI refs
        _image = transform.Find("Cube/Canvas/Image").GetComponent<Image>();
        _defaultSprite ??= _image.sprite;
        _text = transform.Find("Cube/Canvas/Text").GetComponent<TMP_Text>();
        _text.color = m_color;

        SyncInventory();
        UpdateIcon();

        var randomTime = Random.Range(2.5f, 3f);
        InvokeRepeating(nameof(RepeatPickup), randomTime, randomTime);
    }

    private void OnDestroy()
    {
        AllDrawers.Remove(this);
    }

    public new string GetHoverText()
    {
        if (m_checkGuardStone && !PrivateArea.CheckAccess(transform.position, flash: false))
        {
            return Localization.instance.Localize(m_name + "\n$piece_noaccess");
        }
        
        StringBuilder sb = new StringBuilder();

        if (Player.m_localPlayer.IsCrouching())
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] open settings");
            return sb.ToString().Localize();
        }

        bool itemValid = !string.IsNullOrEmpty(m_storedPrefab) &&
                         ObjectDB.instance.m_itemByHash.ContainsKey(m_storedPrefab.GetStableHashCode());
        string localizedName = itemValid
            ? ObjectDB.instance.m_itemByHash[m_storedPrefab.GetStableHashCode()]
                .GetComponent<ItemDrop>().m_itemData.m_shared.m_name.Localize()
            : "";

        if (!itemValid)
        {
            sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"<color=yellow><b>{localizedName}</b></color> ({m_storedAmount})");
        sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>\n");
        if (m_storedAmount <= 0)
        {
            sb.AppendLine(
                $"[<color=yellow><b>$KEY_Use</b></color>] or [<color=yellow><b>Left Alt + $KEY_Use</b></color>] to clear");
            sb.AppendLine(
                $"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{localizedName}</b></color> ({Utils.CustomCountItems(m_storedPrefab, 1)})");
            return sb.ToString().Localize();
        }

        int maxStack = ItemMaxStack();
        sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] to withdraw stack ({maxStack})");
        sb.AppendLine($"[<color=yellow><b>Left Alt + $KEY_Use</b></color>] to withdraw single item");
        sb.AppendLine(
            $"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{localizedName}</b></color> ({Utils.CustomCountItems(m_storedPrefab, 1)})");
        return sb.ToString().Localize();
    }

    public new string GetHoverName()
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
        Rect centerOfScreen = new(Screen.width / 2f - halfWindowWidth, Screen.height / 2f - halfWindowHeight,
            windowWidth, windowHeight);
        GUI.Window(218102318, centerOfScreen, Window, "Item Drawer Options");
    }

    private static void Window(int id)
    {
        if (CurrentOptions.drawer == null || !CurrentOptions.drawer.m_nview.IsValid())
        {
            ShowUI = false;
            return;
        }

        string localizedName = "";
        if (!string.IsNullOrEmpty(CurrentOptions.drawer.m_storedPrefab))
        {
            var prefab = ObjectDB.instance.GetItemPrefab(CurrentOptions.drawer.m_storedPrefab);
            if (prefab)
                localizedName = prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_name.Localize();
        }

        GUILayout.Label(
            $"Current Drawer: <color=yellow><b>{localizedName}</b></color> ({CurrentOptions.drawer.m_storedAmount})");

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
            CurrentOptions.drawer.m_nview.InvokeRPC(ZNetView.Everybody, "ApplyOptions", CurrentOptions);
            ShowUI = false;
        }
    }

    // === Interaction ===
    public new bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold) return false;
        if (m_checkGuardStone && !PrivateArea.CheckAccess(transform.position)) return false;

        if (user.IsCrouching())
        {
            CurrentOptions.drawer = this;
            CurrentOptions.color = m_color;
            CurrentOptions.pickupRange = m_pickupRange;
            ShowUI = true;
            return true;
        }

        if (Input.GetKey(KeyCode.LeftAlt))
        {
            m_nview.InvokeRPC("WithdrawItem_Request", 1);
            return true;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            var amount = Utils.CustomCountItems(m_storedPrefab, 1);
            if (amount <= 0) return true;
            Utils.CustomRemoveItems(m_storedPrefab, amount, 1);
            m_nview.InvokeRPC("AddItem_Request", m_storedPrefab, amount);
            return true;
        }

        m_nview.InvokeRPC("WithdrawItem_Request", ItemMaxStack());
        return true;
    }

    public new bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        var dropPrefab = item.m_dropPrefab?.name;
        if (string.IsNullOrEmpty(dropPrefab)) return false;

        // block equipment and non-stackables unless explicitly included
        if ((item.IsEquipable() || item.m_shared.m_maxStackSize <= 1) &&
            !ItemDrawers.IncludeSet.Contains(dropPrefab))
            return false;

        // disallow mixing different prefabs
        if (!string.IsNullOrEmpty(m_storedPrefab) && m_storedPrefab != dropPrefab)
            return false;

        int amt = item.m_stack;
        if (amt <= 0) return false;

        // transfer from player to drawer
        user.m_inventory.RemoveItem(item);
        m_nview.InvokeRPC("AddItem_Request", dropPrefab, amt);
        return true;
    }


    // === Sync between ZDO and inventory ===
    private void SyncInventory()
    {
        m_inventory.RemoveAll();
    }

    private void SaveDrawer()
    {
        m_nview.GetZDO().Set(ZDO_Prefab, m_storedPrefab);
        m_nview.GetZDO().Set(ZDO_Amount, m_storedAmount);
        m_nview.GetZDO().Set(ZDO_PickupRange, m_pickupRange);
        m_nview.GetZDO().Set(ZDO_Color, global::Utils.ColorToVec3(m_color));
        Save(); // container.Save()
    }

    // === RPCs ===
    private void RPC_AddItem(long sender, string prefab, int amount)
    {
        if (!m_nview.IsOwner() || amount <= 0) return;

        if (!string.IsNullOrEmpty(m_storedPrefab) && m_storedPrefab != prefab)
        {
            Utils.InstantiateAtPos(ZNetScene.instance.GetPrefab(prefab), amount, 1,
                transform.position + Vector3.up);
            return;
        }

        m_storedPrefab = prefab;
        m_storedAmount += amount;
        SyncInventory();
        SaveDrawer();
        UpdateIcon();
    }

    private void RPC_WithdrawItem_Request(long sender, int amount)
    {
        if (!m_nview.IsOwner() || amount <= 0) return;

        if (string.IsNullOrEmpty(m_storedPrefab) || m_storedAmount <= 0)
        {
            m_storedPrefab = "";
            m_storedAmount = 0;
            UpdateIcon();
            return;
        }

        var withdraw = Mathf.Min(amount, m_storedAmount);
        var prefab = ZNetScene.instance.GetPrefab(m_storedPrefab);
        if (!prefab) return;

        Utils.InstantiateAtPos(prefab, withdraw, 1, transform.position + Vector3.up);
        m_storedAmount -= withdraw;
        if (m_storedAmount <= 0) m_storedPrefab = "";

        SyncInventory();
        SaveDrawer();
        UpdateIcon();
    }

    private void RPC_ApplyOptions(long sender, DrawerOptions options)
    {
        if (m_nview.IsOwner())
        {
            m_color = options.color;
            m_pickupRange = Mathf.Min(ItemDrawers.MaxDrawerPickupRange.Value, options.pickupRange);
        }

        _text.color = m_color;
        SaveDrawer();
    }

    private void RPC_ForceRemove(long sender, int amount)
    {
        if (!m_nview.IsOwner() || amount <= 0)
        {
            return;
        }

        m_storedAmount -= amount;
        if (m_storedAmount <= 0)
            m_storedPrefab = "";

        SyncInventory();
        SaveDrawer();
        UpdateIcon();

        m_nview.InvokeRPC(ZNetView.Everybody, "ApplyOptions", new DrawerOptions
        {
            drawer = this,
            color = m_color,
            pickupRange = m_pickupRange
        });
    }


    // === Display ===
    private void UpdateIcon()
    {
        if (string.IsNullOrEmpty(m_storedPrefab))
        {
            _image.sprite = _defaultSprite;
            _text.gameObject.SetActive(false);
            return;
        }

        var prefab = ObjectDB.instance.GetItemPrefab(m_storedPrefab);
        if (prefab == null)
        {
            _image.sprite = _defaultSprite;
            _text.gameObject.SetActive(false);
            return;
        }

        _image.sprite = prefab.GetComponent<ItemDrop>().m_itemData.GetIcon();
        _text.text = m_storedAmount.ToString();
        _text.gameObject.SetActive(true);
    }

    // === Auto pickup loop ===
    private void RepeatPickup()
    {
        if (!m_nview.IsOwner() || string.IsNullOrEmpty(m_storedPrefab) || m_pickupRange <= 0)
            return;

        var pos = transform.position + Vector3.up;
        foreach (var drop in ItemDrop.s_instances.Where(d =>
                     Vector3.Distance(d.transform.position, pos) < m_pickupRange))
        {
            var prefab = global::Utils.GetPrefabName(drop.gameObject);
            if (prefab != m_storedPrefab) continue;
            if (!drop.CanPickup(false))
            {
                drop.RequestOwn();
                continue;
            }

            Instantiate(ItemDrawers.Explosion, drop.transform.position, Quaternion.identity);
            var amt = drop.m_itemData.m_stack;
            drop.m_nview.ClaimOwnership();
            ZNetScene.instance.Destroy(drop.gameObject);
            m_storedAmount += amt;
            SyncInventory();
            SaveDrawer();
            UpdateIcon();
        }
    }

    private int ItemMaxStack()
    {
        if (string.IsNullOrEmpty(m_storedPrefab)) return 0;
        var prefab = ObjectDB.instance.GetItemPrefab(m_storedPrefab);
        return prefab?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_maxStackSize ?? 0;
    }

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
            if (!string.IsNullOrEmpty(drawer.m_storedPrefab) && drawer.m_storedAmount > 0)
            {
                Utils.InstantiateAtPos(
                    ZNetScene.instance.GetPrefab(drawer.m_storedPrefab),
                    drawer.m_storedAmount,
                    1,
                    __instance.transform.position + Vector3.up
                );
            }
        }
    }
}