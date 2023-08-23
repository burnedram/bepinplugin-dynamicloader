using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DynamicLoader.LoadContext;
using DynamicLoader.Compiler;
using DynamicLoader.Windows;
using HarmonyLib;

namespace DynamicLoader;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static new ManualLogSource Log { get; private set; }

    public static void LoadIt()
    {
        var searchPaths = new List<string>(Directory.GetFiles(
            Paths.PluginPath, "*.cs", SearchOption.TopDirectoryOnly));
        searchPaths.AddRange(Directory.GetDirectories(Paths.PluginPath));
        var links = Directory.GetFiles(Paths.PluginPath, "*.lnk", SearchOption.TopDirectoryOnly);
        searchPaths.AddRange(Link.Resolve(links));

        Log.LogInfo($"Searching\n\t{string.Join("\n\t", searchPaths)}");

        foreach (var path in searchPaths)
            Il2CppPluginCompiler.Instance.Compile(path);
    }

    private static readonly Harmony patches = new(MyPluginInfo.PLUGIN_GUID);

    public override void Load()
    {
        Log = base.Log;
        patches.PatchAll();
        AddComponent<PluginComponent>();

        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    public override bool Unload()
    {
        PluginLoadContext.UnloadAll();
        patches.UnpatchSelf();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is unloaded!");
        return true;
    }
}
