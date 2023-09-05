using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;

namespace DynamicLoader.Unloader;

public static class UnityExplorerUnloader
{
    private static readonly ManualLogSource Log = 
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(UnityExplorerUnloader)}>");

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UnloadConsoleReferences(AssemblyLoadContext ctx)
    {
        var unityExplorer = IL2CPPChainloader.Instance.Plugins.GetValueOrDefault("com.sinai.unityexplorer")?.Instance;
        if (unityExplorer == null)
            return;

        var t_ConsoleController = unityExplorer.GetType().Assembly
            .GetType("UnityExplorer.CSConsole.ConsoleController");
        if (t_ConsoleController == null)
        {
            Log.LogWarning("Could not find type [UnityExplorer.CSConsole.ConsoleController]");
            return;
        }

        var m_resetConsole = Traverse.Create(t_ConsoleController)
            .Method("ResetConsole");
        if (!m_resetConsole.MethodExists())
        {
            Log.LogWarning("Could not find method [public static ConsoleController.ResetConsole()]");
            return;
        }

        var t_ScriptEvaluator = unityExplorer.GetType().Assembly
            .GetType("UnityExplorer.CSConsole.ScriptEvaluator");
        if (t_ScriptEvaluator == null)
        {
            Log.LogWarning("Could not find type [UnityExplorer.CSConsole.ScriptEvaluator]");
            return;
        }

        var p_stdLib = Traverse.Create(t_ScriptEvaluator)
            .Field("StdLib");
        if (!p_stdLib.FieldExists())
        {
            Log.LogWarning("Could not find field [private static ScriptEvaluator.StdLib]");
            return;
        }
        var stdLib = p_stdLib.GetValue<HashSet<string>>();

        var addedAssemblies = ctx.Assemblies
            .Select(ass => ass.GetName().Name)
            .Where(stdLib.Add)
            .ToList();
        if (addedAssemblies.Count == 0)
            return;

        Log.LogInfo($"Adding {addedAssemblies.Count} assemblies to UnityExplorer's ignore list\n\t" +
            string.Join("\n\t", addedAssemblies.OrderBy(x => x)));
        m_resetConsole.GetValue();

        foreach (var name in addedAssemblies)
            stdLib.Remove(name);
        Log.LogInfo("UnityExplorer's ignore list reset");
    }
}
