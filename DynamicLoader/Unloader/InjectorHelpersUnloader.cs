using BepInEx.Logging;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DynamicLoader.Unloader;

public static class InjectorHelpersUnloader
{
    static readonly Type t_InjectorHelpers = Type.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers, Il2CppInterop.Runtime");

#pragma warning disable IDE1006 // Naming Styles

    public static Dictionary<(string _namespace, string _class, IntPtr imagePtr), IntPtr> s_ClassNameLookup =>
        Traverse.Create(t_InjectorHelpers)
        .Field<Dictionary<(string _namespace, string _class, IntPtr imagePtr), IntPtr>>(nameof(s_ClassNameLookup))
        .Value;

#pragma warning restore IDE1006 // Naming Styles

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(InjectorHelpersUnloader)}>");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadClassNameLookup(AssemblyLoadContext ctx)
    {
        var lookup = s_ClassNameLookup;
        var ourTypeNames = ctx.Assemblies
            .SelectMany(ass => ass.GetTypes())
            .Select(t => (t.Namespace ?? string.Empty, t.Name))
            .ToHashSet();
        var ourLookups = lookup.Keys
            .Where(key => ourTypeNames.Contains((key._namespace, key._class))
                && lookup.Remove(key))
            .ToList();
        if (ourLookups.Count > 0)
            Log.LogInfo($"Removed {ourLookups.Count} lookups from {t_InjectorHelpers.Name}.{nameof(s_ClassNameLookup)}\n\t" +
                string.Join("\n\t", ourLookups
                    .Select(key => (key._namespace == string.Empty ? key._namespace : key._namespace + ".") + key._class)
                    .GroupBy(x => x)
                    .Select(x => $"{x.Key} x{x.Count()}")));
    }
}
