
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ItemDrawers;
using UnityEngine;

namespace API;

//class to copy
public static class ItemDrawers_API
{
    private static readonly bool _IsInstalled;
    private static readonly MethodInfo MI_GetAllDrawers;
    
    public static List<ZNetView> AllDrawers => _IsInstalled ? (List<ZNetView>) MI_GetAllDrawers.Invoke(null, null) : new();
    public static string GetDrawerPrefab(ZNetView drawer) => drawer.m_zdo.GetString("Prefab");
    public static int GetDrawerAmount(ZNetView drawer) => drawer.m_zdo.GetInt("Amount");
    public static void DrawerRemoveItem(ZNetView drawer, int amount)
    {
        drawer.ClaimOwnership();
        drawer.InvokeRPC("ForceRemove", amount);
    }

    public static void DrawerWithdraw(ZNetView drawer, int amount) =>
        drawer.InvokeRPC("WithdrawItem_Request", amount);
    
    static ItemDrawers_API()
    {
        if (Type.GetType("API.ClientSide, ItemDrawers") is not { } drawersAPI)
        {
            _IsInstalled = false;
            return;
        }

        _IsInstalled = true;
        MI_GetAllDrawers = drawersAPI.GetMethod("AllDrawers", BindingFlags.Public | BindingFlags.Static);
    }
}

//do not copy
public static class ClientSide
{
    public static List<ZNetView> AllDrawers() => DrawerComponent.AllDrawers.Select(d => d._znv).ToList();
}
