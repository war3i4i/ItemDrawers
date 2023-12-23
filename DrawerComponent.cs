using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using JetBrains.Annotations;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ItemDrawers;

public class DrawerComponent : MonoBehaviour, Interactable, Hoverable
{
    public static readonly List<DrawerComponent> AllDrawers = [];
    public ZNetView _znv;
    private Image _image;
    private static Sprite _defaultSprite;
    private TMP_Text _text;

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

    public bool ItemValid => !string.IsNullOrEmpty(CurrentPrefab) && ObjectDB.instance.m_itemByHash.ContainsKey(CurrentPrefab.GetStableHashCode());
    private int ItemMaxStack => ObjectDB.instance.m_itemByHash[CurrentPrefab.GetStableHashCode()].GetComponent<ItemDrop>().m_itemData.m_shared.m_maxStackSize;
    private string LocalizedName => ObjectDB.instance.GetItemPrefab(CurrentPrefab).GetComponent<ItemDrop>().m_itemData.m_shared.m_name.Localize();

    private void OnDestroy() => AllDrawers.Remove(this);

    private void Awake()
    {
        _znv = GetComponent<ZNetView>();
        if (!_znv.IsValid()) return;
        AllDrawers.Add(this);
        _image = transform.Find("Cube/Canvas/Image").GetComponent<Image>();
        _defaultSprite ??= _image.sprite;
        _text = transform.Find("Cube/Canvas/Text").GetComponent<TMP_Text>();
        _znv.Register<string, int>("AddItem_Request", RPC_AddItem);
        _znv.Register<string, int>("AddItem_Player", RPC_AddItem_Player);
        _znv.Register<int>("WithdrawItem_Request", RPC_WithdrawItem_Request);
        _znv.Register<string, int>("UpdateIcon", RPC_UpdateIcon);
        _znv.Register<int>("ForceRemove", RPC_ForceRemove);
        RPC_UpdateIcon(0, CurrentPrefab, CurrentAmount);
        InvokeRepeating(nameof(Repeat_1s), 1f, 1f);
    }

    public void ForceRemove()
    {
        _znv.ClaimOwnership();
        _znv.InvokeRPC("ForceRemove", CurrentAmount);
    }

    private void RPC_ForceRemove(long sender, int amount)
    {
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
    }

    private void RPC_WithdrawItem_Request(long sender, int amount)
    {
        if (CurrentAmount <= 0 || !ItemValid)
        {
            CurrentPrefab = "";
            CurrentAmount = 0;
            _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", "", 0);
            return;
        }
        if (amount <= 0) return;
        amount = Mathf.Min(amount, CurrentAmount);
        CurrentAmount -= amount;
        _znv.InvokeRPC(sender, "AddItem_Player", CurrentPrefab, amount);
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
    }

    private void RPC_AddItem_Player(long _, string prefab, int amount) => Utils.InstantiateItem(ZNetScene.instance.GetPrefab(prefab), amount, 1);
    private void RPC_UpdateIcon(long _, string prefab, int amount)
    {
        if (!ItemValid)
        {
            _image.sprite = _defaultSprite;
            _text.gameObject.SetActive(false);
            return;
        }

        _image.sprite = ObjectDB.instance.GetItemPrefab(prefab).GetComponent<ItemDrop>().m_itemData.GetIcon();
        _text.text = amount.ToString();
        _text.gameObject.SetActive(true);
    }

    private void RPC_AddItem(long sender, string prefab, int amount)
    {
        if (!_znv.IsOwner()) return;
        if (amount <= 0) return;
        if (ItemValid && CurrentPrefab != prefab)
        {
            _znv.InvokeRPC(sender, "AddItem_Player", prefab, amount);
            return;
        }

        int newAmount = CurrentAmount + amount;
        CurrentAmount = newAmount;
        if (CurrentPrefab != prefab) CurrentPrefab = prefab;
        _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", prefab, newAmount);
    }

    private void Repeat_1s()
    {
        if (!_znv.IsOwner()) return;
        if (!ItemValid || !Player.m_localPlayer) return;

        Vector3 vector = transform.position + Vector3.up;
        foreach (Collider collider in Physics.OverlapSphere(vector, ItemDrawers.DrawerPickupRange.Value, Player.m_localPlayer.m_autoPickupMask))
        {
            if (collider.attachedRigidbody)
            {
                ItemDrop component = collider.attachedRigidbody.GetComponent<ItemDrop>();
                string goName = global::Utils.GetPrefabName(component.gameObject);
                if (goName == CurrentPrefab)
                {
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
                    _znv.InvokeRPC(ZNetView.Everybody, "UpdateIcon", CurrentPrefab, CurrentAmount);
                }
            }
        }
    }

    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (!ItemValid) return false;
        if (Input.GetKey(KeyCode.LeftAlt))
        {
            _znv.InvokeRPC("WithdrawItem_Request", 1);
            return true;
        }

        if (Input.GetKey(KeyCode.LeftShift))
        {
            int amount = Utils.CustomCountItems(CurrentPrefab, 1);
            if (amount <= 0) return true;
            Utils.CustomRemoveItems(CurrentPrefab, amount, 1);
            _znv.InvokeRPC("AddItem_Request", CurrentPrefab, amount);
            return true;
        }
        
        _znv.InvokeRPC("WithdrawItem_Request", ItemMaxStack);
        return true;
    }


    public bool UseItem(Humanoid user, ItemDrop.ItemData item)
    {
        string dropPrefab = item.m_dropPrefab?.name;
        if (string.IsNullOrEmpty(dropPrefab)) return false;

        if (item.IsEquipable()) return false;

        if (item.m_shared.m_maxStackSize <= 1 && !ItemDrawers.IncludeSet.Contains(dropPrefab)) return false;

        if (!string.IsNullOrEmpty(CurrentPrefab) && CurrentPrefab != dropPrefab) return false;

        int amount = item.m_stack;
        if (amount <= 0) return false;
        user.m_inventory.RemoveItem(item);
        _znv.InvokeRPC("AddItem_Request", dropPrefab, amount);
        return true;
    }

    public string GetHoverText()
    {
        StringBuilder sb = new StringBuilder();
        if (!ItemValid)
        {
            sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"<color=yellow><b>{LocalizedName}</b></color> ({CurrentAmount})");
        sb.AppendLine("<color=yellow><b>Use Hotbar to add item</b></color>\n");
        if (CurrentAmount <= 0)
        {
            sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] or [<color=yellow><b>Left Alt + $KEY_Use</b></color>] to clear");
            sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}</b></color> ({Utils.CustomCountItems(CurrentPrefab, 1)})");
            return sb.ToString().Localize();
        }

        sb.AppendLine($"[<color=yellow><b>$KEY_Use</b></color>] to withdraw stack ({ItemMaxStack})");
        sb.AppendLine($"[<color=yellow><b>Left Alt + $KEY_Use</b></color>] to withdraw single item");
        sb.AppendLine($"[<color=yellow><b>Left Shift + $KEY_Use</b></color>] to deposit all <color=yellow><b>{LocalizedName}</b></color> ({Utils.CustomCountItems(CurrentPrefab, 1)})");
        return sb.ToString().Localize();
    }

    public string GetHoverName()
    {
        return "Item Drawer";
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