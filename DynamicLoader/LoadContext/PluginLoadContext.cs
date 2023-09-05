using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DynamicLoader.Unloader;
using HarmonyLib;

namespace DynamicLoader.LoadContext;

public class PluginLoadContext : IDisposable
{
    public enum LoadStates
    {
        READY,
        LOADING,
        LOADED,
        DISPOSED,
    }

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(PluginLoadContext)}>");

    private static Dictionary<string, PluginLoadContext> ContextsByLocation { get; } = new();

    public static bool TryLoadPluginAssembly(string assemblyFile, out Assembly assembly)
    {
        if (!ContextsByLocation.TryGetValue(assemblyFile, out var ctx))
        {
            assembly = null;
            return false;
        }
        assembly = ctx.GetPluginAssembly(assemblyFile);
        return true;
    }

    public static void UnloadAll()
    {
        foreach (var ctx in ContextsByLocation.Values)
            ctx.Unload();
        ContextsByLocation.Clear();
    }

    public static bool Exists(PluginInfo info)
    {
        if (ContextsByLocation.ContainsKey(info.Location))
            return true;
        return ContextsByLocation.Values.Any(ctx => ctx.PluginInfo.Metadata.GUID == info.Metadata.GUID);
    }

    public static bool Exists(string assName)
    {
        return ContextsByLocation.Values.Any(ctx => ctx.AssemblyName == assName);
    }

    public static PluginLoadContext Create(PluginInfo info, string assName, MemoryStream assemblyStream, MemoryStream symbolsStream = null)
    {
        if (Exists(info))
            throw new InvalidOperationException($"{info.Metadata.GUID} already exists in dict");
        var ctx = new PluginLoadContext(info, assName, assemblyStream, symbolsStream);
        ContextsByLocation[info.Location] = ctx;
        return ctx;
    }

    private delegate IList<PluginInfo> IL2CPPChainLoader_LoadPlugins(IList<PluginInfo> plugins);
    private static readonly IL2CPPChainLoader_LoadPlugins LoadPlugins;

    static PluginLoadContext()
    {
        var m_LoadPlugins = typeof(IL2CPPChainloader).BaseType
            .GetMethod("LoadPlugins",
                BindingFlags.NonPublic | BindingFlags.Instance,
                new[] { typeof(IList<PluginInfo>) });
        LoadPlugins = m_LoadPlugins.CreateDelegate<IL2CPPChainLoader_LoadPlugins>(IL2CPPChainloader.Instance);
    }

    public PluginInfo PluginInfo { get; private set; }
    public string AssemblyName { get; }
    public LoadStates LoadState { get; private set; } = LoadStates.READY;
    public string Name { get; }

    private MemoryStream assemblyStream, symbolsStream;
    private AssemblyLoadContext ctx;
    private Assembly assembly;
    private List<GCHandle> gcHandles = new();

    private PluginLoadContext(PluginInfo info, string assName, MemoryStream assemblyStream, MemoryStream symbolsStream = null)
    {
        PluginInfo = info;
        AssemblyName = assName;
        this.assemblyStream = assemblyStream;
        this.symbolsStream = symbolsStream;

        ctx = new AssemblyLoadContext(info.Metadata.GUID, isCollectible: true);
        Name = ctx.Name;
        ctx.Unloading += OnUnloading;
    }

    public bool TryLoadPlugin()
    {
        if (LoadState != LoadStates.READY)
            throw new InvalidOperationException($"Can not load plugin {PluginInfo.Metadata.GUID} "
                + $"in state {Enum.GetName(LoadState)}, expected {Enum.GetName(LoadStates.READY)}");
        if (PluginInfo.Instance != null)
            throw new InvalidOperationException($"Can not load plugin {PluginInfo.Metadata.GUID} "
                + $"because it already has an instance associated with it ({PluginInfo.Instance.GetType().FullName})");

        Log.LogInfo($"Loading plugin {PluginInfo.Metadata.Name} ({PluginInfo.Metadata.Version})");
        LoadState = LoadStates.LOADING;
        LoadPatches.OnAlloc += GCHandle_OnAlloc;
        try
        {
            LoadPlugins(new List<PluginInfo>() { PluginInfo });
        }
        catch
        {
            ctx?.Unload();
        }
        finally
        {
            LoadPatches.OnAlloc -= GCHandle_OnAlloc;
        }

        if (LoadState != LoadStates.LOADED)
        {
            var state = LoadState;
            if (state != LoadStates.DISPOSED)
                ctx?.Unload();
            if (state == LoadStates.LOADING)
                Log.LogError($"{nameof(IL2CPPChainloader)} did not load plugin {PluginInfo.Metadata.GUID}, "
                    + "is Harmony patch working?");
            else
                Log.LogError($"Unexpected load state {Enum.GetName(state)} after loading "
                + $"plugin {PluginInfo.Metadata.GUID}, expected {Enum.GetName(LoadStates.LOADED)}");
            return false;
        }

        return PluginInfo.Instance is BasePlugin;
    }

    private void GCHandle_OnAlloc(object sender, (string source, object value, GCHandleType type, GCHandle result) e)
    {
        // TODO Check StackFrame to ensure we're only yanking il2cpp handles
        Log.LogInfo($"Intercepted {e.source}({e.value}, {e.type})");
        gcHandles.Add(e.result);
    }

    public Assembly GetPluginAssembly(string assemblyFile)
    {
        if (PluginInfo.Location != assemblyFile)
            return null;

        if (LoadState == LoadStates.LOADED && assembly != null)
            return assembly;

        if (LoadState != LoadStates.LOADING)
            return null;

        assemblyStream.Seek(0, SeekOrigin.Begin);
        symbolsStream?.Seek(0, SeekOrigin.Begin);
        assembly = ctx.LoadFromStream(assemblyStream, symbolsStream);
        assemblyStream = null;
        symbolsStream = null;
        LoadState = LoadStates.LOADED;
        Log.LogInfo($"Assembly {assembly.GetName().Name} [{PluginInfo.Metadata.GUID}] loaded from in-memory stream");
        return assembly;
    }

    public void Unload()
    {
        ctx?.Unload();
    }

    private void OnUnloading(AssemblyLoadContext unloadingCtx)
    {
        if (unloadingCtx != ctx)
            return;
        ctx.Unloading -= OnUnloading;

        Log.LogInfo("OnUnloading " + ctx.Name);
        var oldState = LoadState;
        LoadState = LoadStates.DISPOSED;
        assemblyStream = symbolsStream = null;
        ContextsByLocation.Remove(PluginInfo.Location);

        PluginUnloadContext.Create(PluginInfo, AssemblyName, new (ctx), gcHandles);

        if (PluginInfo.Instance is BasePlugin plugin)
        {
            try
            {
                if (!plugin.Unload())
                    Log.LogWarning($"{PluginInfo.Metadata.GUID} indicated that it is not unloadable, "
                        + $"{nameof(AssemblyLoadContext)} might not unload properly");
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Exception thrown while unloading plugin {PluginInfo.Metadata.Name} [{PluginInfo.Metadata.GUID}], "
                    + $"{nameof(AssemblyLoadContext)} might not unload properly\n{ex.ToString()}");
            }
        }

        ClassInjectorUnloader.UnloadInjectedTypes(ctx);
        ClassInjectorUnloader.UnloadInflatedMethods(ctx);
        InjectorHelpersUnloader.UnloadClassNameLookup(ctx);
        UnityExplorerUnloader.UnloadConsoleReferences(ctx);
        UniverseLibUnloader.UnloadReflectionUtility(ctx);

        // Release references to the context, important for the GC to actually unload us
        assembly = null;
        ctx = null;

        Traverse.Create(PluginInfo).Property<object>(nameof(PluginInfo.Instance)).Value = null;
        IL2CPPChainloader.Instance.Plugins.Remove(PluginInfo.Metadata.GUID);
    }

    public void Dispose()
    {
        Unload();
        GC.SuppressFinalize(this);
    }
}
