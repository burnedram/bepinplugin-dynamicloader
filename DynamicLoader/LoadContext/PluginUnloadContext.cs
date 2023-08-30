using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using BepInEx;
using BepInEx.Logging;

namespace DynamicLoader.LoadContext;

public class PluginUnloadContext
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(PluginUnloadContext)}>");

    private static readonly SortedDictionary<KeyType, PluginUnloadContext> UnloadingContexts = new(KeyType.COMPARER);
    private static readonly List<PluginUnloadContext> OrphanedContexts = new();
    private static readonly Stopwatch UnloadingStopwatch = new();

    public static void Process()
    {
        if (UnloadingContexts.Count == 0)
            return;

        Log.LogInfo($"Processing {UnloadingContexts.Count} unloading and {OrphanedContexts.Count} orphaned contexts");

        UnloadingStopwatch.Restart();
        Il2CppSystem.GC.Collect();
        Il2CppSystem.GC.InternalCollect(Il2CppSystem.GC.MaxGeneration);
        UnloadingStopwatch.Stop();
        Log.LogInfo(FormattableString.Invariant($"Internal garbage collecting took {UnloadingStopwatch.Elapsed.TotalSeconds:0.###}s"));

        UnloadingStopwatch.Restart();
        GC.Collect();
        GC.WaitForPendingFinalizers();
        UnloadingStopwatch.Stop();
        Log.LogInfo(FormattableString.Invariant($"Garbage collecting took {UnloadingStopwatch.Elapsed.TotalSeconds:0.###}s"));

        var unloaded = UnloadingContexts
            .Where(kv => kv.Value.IsUnloaded(incGC: true))
            .ToList()
            .Select(kv =>
            {
                UnloadingContexts.Remove(kv.Key);
                return kv.Value;
            })
            .Concat(OrphanedContexts
                .Where(ctx => ctx.IsUnloaded(incGC: true))
                .ToList()
                .Select(ctx =>
                {
                    OrphanedContexts.Remove(ctx);
                    return ctx;
                }))
            .ToList();

        if (unloaded.Count > 0)
            Log.LogInfo($"Unloaded {unloaded.Count} contexts\n\t{string.Join("\n\t", unloaded.Select(ctx => ctx.ToString()))}");

        var orphaned = UnloadingContexts
            .Where(kv => kv.Value.GCs >= 10)
            .ToList()
            .Select(kv =>
            {
                UnloadingContexts.Remove(kv.Key);
                return kv.Value;
            })
            .ToList();
        if (orphaned.Count > 0)
        {
            OrphanedContexts.AddRange(orphaned);
            Log.LogWarning($"Orphaned {orphaned.Count} contexts\n\t{string.Join("\n\t", orphaned.Select(ctx => ctx.ToString()))}");
        }
    }

    public static bool Exists(PluginInfo info)
    {
        return UnloadingContexts.Values
            .Any(ctx => ctx.PluginInfo.Location == info.Location || ctx.PluginInfo.Metadata.GUID == info.Metadata.GUID);
    }

    public static bool Exists(string assName)
    {
        return UnloadingContexts.Values.Any(ctx => ctx.AssemblyName == assName);
    }

    public static void Create(PluginInfo info, string assName, WeakReference<AssemblyLoadContext> ctxRef, List<GCHandle> gcHandles)
    {
        if (Exists(info))
            throw new InvalidOperationException($"{info.Metadata.GUID} already exists in dict");
        var ctx = new PluginUnloadContext(info, assName, ctxRef, gcHandles);
        UnloadingContexts.Add(ctx.Key, ctx);
    }

    public PluginInfo PluginInfo { get; }
    public string AssemblyName { get; }
    public readonly DateTimeOffset UnloadStart = DateTimeOffset.UtcNow;
    public DateTimeOffset? UnloadDone { get; private set; } = null;
    public TimeSpan UnloadTime
    {
        get
        {
            if (UnloadDone.HasValue)
                return UnloadDone.Value - UnloadStart;
            return DateTimeOffset.UtcNow - UnloadStart;
        }
    }
    public bool Unloaded => UnloadDone != null;
    public int GCs { get; private set; }
    public readonly KeyType Key;

    private readonly WeakReference<AssemblyLoadContext> ctxRef;
    private readonly List<GCHandle> gcHandles;

    private PluginUnloadContext(PluginInfo info, string assName, WeakReference<AssemblyLoadContext> ctxRef, List<GCHandle> gcHandles)
    {
        PluginInfo = info;
        AssemblyName = assName;
        this.ctxRef = ctxRef;
        this.gcHandles = gcHandles;
        Key = new(PluginInfo.Metadata.GUID, UnloadStart);
    }

    public bool IsUnloaded(bool incGC = false)
    {
        if (Unloaded)
            return true;

        if (!TryUseCtx())
        {
            UnloadDone = DateTimeOffset.UtcNow;
            return true;
        }

        if (!incGC)
            return false;
        GCs++;

        if (gcHandles.Count == 0)
            return false;

        var handle = gcHandles[gcHandles.Count - 1];
        gcHandles.RemoveAt(gcHandles.Count - 1);
        if (!handle.IsAllocated)
            return false;

        // TODO Free multiple handles at once.
        // Some order/batching is needed, e.g. the finalizer can't be freed before
        // Il2CppSystem.GC has invoked it.
        /// <see cref="Il2CppInterop.Runtime.Injection.ClassInjector.Finalize"/>
        /// <see cref="Il2CppInterop.Runtime.Injection.ClassInjector.ProcessNewObject"/>
        /// <see cref="Il2CppInterop.Runtime.Injection.ClassInjector.AssignGcHandle"/>
        Log.LogWarning($"Freeing handle {handle.Target}");
        handle.Free();
        return false;
    }

    public List<string> GetLoadedAssemblies()
    {
        var ret = new List<string>();
        TryUseCtx(ctx => ret
            .AddRange(ctx.Assemblies
                .Select(ass => ass.FullName)
                .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)));
        return ret;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private bool TryUseCtx(Action<AssemblyLoadContext> action = null)
    {
        if (!ctxRef.TryGetTarget(out var ctx))
            return false;
        action?.Invoke(ctx);
        return true;
    }

    public override string ToString()
    {
        string asses = "";
        if (!Unloaded)
        {
            var names = GetLoadedAssemblies();
            asses = $"\n\t\tLoaded assemblies: {names.Count}";
            if (names.Count > 0)
                asses = $"{asses}\n\t\t\t{string.Join("\n\t\t\t", names)}";
        }
        return FormattableString.Invariant($"{PluginInfo.Metadata.GUID}:\n\t\tName: {PluginInfo.Metadata.Name}\n\t\tGCs survived: {GCs}\n\t\tUnloadTime: {UnloadTime.TotalSeconds:0.###}s{asses}");
    }

    public readonly struct KeyType
    {
        public static readonly IComparer<KeyType> COMPARER =
            Comparer<KeyType>.Create((left, right) =>
            {
                var cmp = StringComparer.OrdinalIgnoreCase.Compare(left.GUID, right.GUID);
                if (cmp != 0)
                    return cmp;
                return left.UnloadStart.CompareTo(right.UnloadStart);
            });

        public readonly string GUID;
        public readonly DateTimeOffset UnloadStart;

        public KeyType(string guid, DateTimeOffset unloadStart)
        {
            GUID = guid;
            UnloadStart = unloadStart;
        }
    }
}
