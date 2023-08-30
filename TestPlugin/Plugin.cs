using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using UnityEngine;

namespace TestPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static new ManualLogSource Log { get; private set; }
    private PluginBehaviour behaviour;

    public override void Load()
    {
        Log = base.Log;
        behaviour = AddComponent<PluginBehaviour>();
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    public override bool Unload()
    {
        if (behaviour != null)
        {
            Object.DestroyImmediate(behaviour);
            behaviour = null;
        }
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is unloaded!");
        return true;
    }
}
