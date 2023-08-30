using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DynamicLoader.Unloader;

public static class ClassInjectorUnloader {
#pragma warning disable IDE1006 // Naming Styles

    public static HashSet<string> InjectedTypes =>
        Traverse.Create(typeof(ClassInjector))
        .Field<HashSet<string>>(nameof(InjectedTypes))
        .Value;

    public static Dictionary<IntPtr, (MethodInfo, Dictionary<IntPtr, IntPtr>)> InflatedMethodFromContextDictionary =>
        Traverse.Create(typeof(ClassInjector))
        .Field<Dictionary<IntPtr, (MethodInfo, Dictionary<IntPtr, IntPtr>)>>(nameof(InflatedMethodFromContextDictionary))
        .Value;

    public static ConcurrentDictionary<(Type type, FieldAttributes attrs), IntPtr> _injectedFieldTypes =>
        Traverse.Create(typeof(ClassInjector))
        .Field<ConcurrentDictionary<(Type type, FieldAttributes attrs), IntPtr>>(nameof(_injectedFieldTypes))
        .Value;

#pragma warning restore IDE1006 // Naming Styles

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(ClassInjectorUnloader)}>");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadInjectedTypes(AssemblyLoadContext ctx)
    {
        var injectedTypes = InjectedTypes;
        var typesByName = ctx.Assemblies
                .SelectMany(ass => ass.GetTypes())
                .GroupBy(t => t.FullName)
                .ToList();
        List<Type> types;
        lock (injectedTypes)
        {
            types = typesByName
                .Where(kv => injectedTypes.Remove(kv.Key))
                .SelectMany(kv => kv)
                .ToList();
        }
        if (types.Count > 0)
            Log.LogInfo($"Removed {types.Count} types from {nameof(ClassInjector)}.{nameof(InjectedTypes)}\n\t" +
                string.Join("\n\t", types.Select(t => t.FullDescription())));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadInflatedMethods(AssemblyLoadContext ctx)
    {
        var inflatedMethods = InflatedMethodFromContextDictionary;
        var assemblies = ctx.Assemblies.ToHashSet();
        var methods = inflatedMethods
            .Where(kv => assemblies.Contains(kv.Value.Item1.DeclaringType.Assembly)
                && inflatedMethods.Remove(kv.Key))
            .ToList();
        if (methods.Count > 0)
            Log.LogInfo($"Removed {methods.Count} methods from {nameof(ClassInjector)}.{nameof(InflatedMethodFromContextDictionary)}\n\t" +
                string.Join("\n\t", methods.Select(kv => kv.Value.Item1.FullDescription())));
    }
}
