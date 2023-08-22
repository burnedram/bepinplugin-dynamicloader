using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;

namespace TestPlugin;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BasePlugin
{
    public static new ManualLogSource Log { get; private set; }

    public override void Load()
    {
        Log = base.Log;
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }

    public override bool Unload()
    {
        Log.LogInfo($"Plugin {MyPluginInfo.PLUGIN_GUID} is unloaded!");
        return true;
    }
}
