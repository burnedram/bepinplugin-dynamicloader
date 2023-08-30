using UnityEngine;

namespace TestPlugin;

public class PluginBehaviour : MonoBehaviour
{
    public void Awake()
    {
        Plugin.Log.LogInfo("Awake");
        InflatedMethod("test");
    }

    public void OnDestroy()
    {
        Plugin.Log.LogInfo("OnDestroy");
    }

    public void InflatedMethod<T>(T obj)
    {
        Plugin.Log.LogInfo("InflatedMethod " + obj.ToString());
    }
}
