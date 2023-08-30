using BepInEx.Bootstrap;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace DynamicLoader.LoadContext;

[HarmonyPatch]
static class LoadPatches
{
    /// <summary>
    /// When a <see cref="BepInEx.PluginInfo"/> is loaded by <see cref="IL2CPPChainloader"/> it will use <see cref="Assembly.LoadFrom(string)"/><br/>
    /// (See the <see langword="private"/> method <see cref="BaseChainloader{TPlugin}"/>.LoadPlugins(<see cref="IList{PluginInfo}"/>))<br/>
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch(typeof(Assembly), nameof(Assembly.LoadFrom), typeof(string))]
    static bool Assembly_LoadFrom_Prefix(string assemblyFile, ref Assembly __result)
    {
        return !PluginLoadContext.TryLoadPluginAssembly(assemblyFile, out __result);
    }

    public static event EventHandler<(string source, object value, GCHandleType type, GCHandle result)> OnAlloc;
    private static readonly ThreadLocal<int> _allocDepth = new();

#pragma warning disable IDE0060 // Remove unused parameter

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GCHandle), MethodType.Constructor, typeof(object), typeof(GCHandleType))]
    static void GCHandle_ctor_Prefix(object value, GCHandleType type)
    {
        _allocDepth.Value++;
    }

    [HarmonyPrefix]
    [HarmonyPatch(typeof(GCHandle), "InternalAlloc", typeof(object), typeof(GCHandleType))]
    static void GCHandle_InternalAlloc_Prefix(object value, GCHandleType type)
    {
        _allocDepth.Value++;
    }

    /// <summary>
    /// <see cref="GCHandle"/>.InternalAlloc(<see cref="object"/> value, <see cref="GCHandleType"/> type)<br/>
    /// Can _not_ be inlined, but we prefer the constructor patch
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GCHandle), "InternalAlloc", typeof(object), typeof(GCHandleType))]
    static void GCHandle_InternalAlloc_Postfix(object value, GCHandleType type, IntPtr __result)
    {
        if (--_allocDepth.Value == 0)
            OnAlloc?.Invoke(null, ("GCHandle.InternalAlloc", value, type, GCHandle.FromIntPtr(__result)));
    }

    /// <summary>
    /// <see cref="GCHandle"/>..ctor(<see cref="object"/> value, <see cref="GCHandleType"/> type)<br/>
    /// Can be inlined, so this patch is not reliable
    /// </summary>
    [HarmonyPostfix]
    [HarmonyPatch(typeof(GCHandle), MethodType.Constructor, typeof(object), typeof(GCHandleType))]
    static void GCHandle_ctor_Postfix(object value, GCHandleType type, GCHandle __instance)
    {
        if (--_allocDepth.Value == 0)
            OnAlloc?.Invoke(null, ("GCHandle..ctor", value, type, __instance));
    }

#pragma warning restore IDE0060 // Remove unused parameter
}
