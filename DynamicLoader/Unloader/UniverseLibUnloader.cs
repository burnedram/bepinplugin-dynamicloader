using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DynamicLoader.Unloader;

public static class UniverseLibUnloader
{
    private static Type _ReflectionUtility;
    public static Type ReflectionUtility =>
        _ReflectionUtility ??= Type.GetType("UniverseLib.ReflectionUtility, UniverseLib.IL2CPP.Interop");

    private static readonly ManualLogSource Log = 
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(UniverseLibUnloader)}>");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadReflectionUtility(AssemblyLoadContext ctx)
    {
        if (ReflectionUtility == null)
            return;

        var f_allTypes = Traverse.Create(ReflectionUtility)
            .Field("AllTypes");
        if (f_allTypes == null)
        {
            Log.LogWarning("Could not find field [public static UniverseLib.ReflectionUtility.AllTypes]");
            return;
        }

        var allTypes = f_allTypes
            .GetValue<IDictionary<string, Type>>();
        var ourTypes = allTypes
            .Where(t => ctx.Assemblies.Contains(t.Value.Assembly))
            .ToList();
        Log.LogInfo($"AllTypes contains {ourTypes.Count} of our types, removing\n\t" +
            string.Join("\n\t", ourTypes?.Select(t => t.Value.FullDescription()).OrderBy(x => x)));
        foreach (var kv in ourTypes)
            allTypes.Remove(kv.Key);
    }
}
