
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

    public class Drawer(ZNetView znv)
    {
        public string Prefab = znv.m_zdo.GetString("Prefab");
        public int Amount = znv.m_zdo.GetInt("Amount");
        public void Remove(int amount) { znv.ClaimOwnership(); znv.InvokeRPC("ForceRemove", amount); }
        public void Withdraw(int amount) => znv.InvokeRPC("WithdrawItem_Request", amount);
        public void Add(int amount) => znv.InvokeRPC("AddItem_Request", Prefab, amount);
    }

    public static List<Drawer> AllDrawers => _IsInstalled ? 
        ((List<ZNetView>)MI_GetAllDrawers.Invoke(null, null)).Select(znv => new Drawer(znv)).ToList() 
        : new();
    
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
