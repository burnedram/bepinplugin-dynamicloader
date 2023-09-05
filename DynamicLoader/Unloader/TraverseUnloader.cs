using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DynamicLoader.Unloader;

public static class TraverseUnloader
{
    public static readonly object Cache =
        Traverse.Create(typeof(Traverse))
            .Field(nameof(Cache))
            .GetValue();

    public static readonly Dictionary<Type, Dictionary<string, FieldInfo>> declaredFields =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, FieldInfo>>>(nameof(declaredFields))
            .Value;

    public static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> declaredProperties =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, PropertyInfo>>>(nameof(declaredProperties))
            .Value;

    public static readonly Dictionary<Type, Dictionary<string, Dictionary<int, MethodBase>>> declaredMethods =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, Dictionary<int, MethodBase>>>>(nameof(declaredMethods))
            .Value;

    public static readonly Dictionary<Type, Dictionary<string, FieldInfo>> inheritedFields =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, FieldInfo>>>(nameof(inheritedFields))
            .Value;

    public static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> inheritedProperties =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, PropertyInfo>>>(nameof(inheritedProperties))
            .Value;

    public static readonly Dictionary<Type, Dictionary<string, Dictionary<int, MethodBase>>> inheritedMethods =
        Traverse.Create(Cache)
            .Field<Dictionary<Type, Dictionary<string, Dictionary<int, MethodBase>>>>(nameof(inheritedMethods))
            .Value;

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(TraverseUnloader)}>");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadCache(AssemblyLoadContext ctx)
    {
        var assemblies = ctx.Assemblies.ToHashSet();

        var unloaded = new List<List<Type>>
        {
            UnloadDict(assemblies, declaredFields),
            UnloadDict(assemblies, declaredProperties),
            UnloadDict(assemblies, declaredMethods),
            UnloadDict(assemblies, inheritedFields),
            UnloadDict(assemblies, inheritedProperties),
            UnloadDict(assemblies, inheritedMethods),
        }
            .SelectMany(x => x)
            .Distinct()
            .Select(x => x.FullDescription())
            .OrderBy(x => x)
            .ToList();
        if (unloaded.Count > 0)
            Log.LogInfo($"Removed {unloaded.Count} types from cache\n\t" +
                string.Join("\n\t", unloaded));
    }

    private static List<Type> UnloadDict<TVal>(HashSet<Assembly> assemblies, Dictionary<Type, TVal> dict)
    {
        return dict.Keys
            .Where(t => assemblies.Contains(t.Assembly)
                && dict.Remove(t))
            .ToList();
    }
}
