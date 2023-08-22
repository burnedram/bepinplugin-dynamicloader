using BepInEx.Unity.IL2CPP.UnityEngine;
using DynamicLoader.LoadContext;
using System.Collections.Generic;
using UnityEngine;
using KeyCode = BepInEx.Unity.IL2CPP.UnityEngine.KeyCode;

namespace DynamicLoader;

public class PluginComponent : MonoBehaviour
{
    private static readonly HashSet<KeyCode> keys = new();

    private static bool IsKeyPressed(KeyCode key)
    {
        if (Input.GetKeyInt(key))
            return keys.Add(key);
        keys.Remove(key);
        return false;
    }

    public void Update()
    {
        if (IsKeyPressed(KeyCode.F5))
            Plugin.LoadIt();
        else if (IsKeyPressed(KeyCode.F6))
            PluginLoadContext.UnloadAll();

        PluginUnloadContext.Process();
    }
}