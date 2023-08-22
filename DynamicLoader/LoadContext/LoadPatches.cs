using BepInEx.Bootstrap;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;

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
}
