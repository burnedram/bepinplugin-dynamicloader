using Basic.Reference.Assemblies;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using DynamicLoader.LoadContext;
using HarmonyLib;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using Mono.Cecil;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace DynamicLoader.Compiler;
public class Il2CppPluginCompiler
{

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{MyPluginInfo.PLUGIN_NAME}<{nameof(Il2CppPluginCompiler)}>");

    private static Il2CppPluginCompiler _instance;
    public static Il2CppPluginCompiler Instance => _instance ??= new Il2CppPluginCompiler();

    private readonly SlimAssemblyResolver _assemblyResolver;
    private readonly PortableExecutableReference[] _frameworkReferences = (PortableExecutableReference[]) Net60.References.All;
    private readonly List< PortableExecutableReference> _bepInExReferences;

    private Il2CppPluginCompiler()
    {
        _assemblyResolver = new SlimAssemblyResolver();
        _assemblyResolver.AddSearchDirectory(Paths.BepInExAssemblyDirectory);
        /// <see cref="Il2CppInteropManager.IL2CPPInteropAssemblyPath" />
        _assemblyResolver.AddSearchDirectory(Path.Combine(Paths.BepInExRootPath, "interop"));

        using var baseAssemblyDefinition = AssemblyDefinition.ReadAssembly(typeof(BasePlugin).Assembly.Location);
        var referencedAssemblyDefinitions = baseAssemblyDefinition.MainModule.AssemblyReferences
            .Select(r =>
            {
                try
                {
                    return _assemblyResolver.Resolve(r);
                }
                catch (AssemblyResolutionException ex)
                {
                    Log.LogWarning(ex.Message);
                    return null;
                }
            })
            .Where(d => d != null)
            .ToList();
        _assemblyResolver.Trim();

        var frameworkRefSet = _frameworkReferences
            .Select(r => r.FilePath)
            .ToHashSet();
        _bepInExReferences = Enumerable.Repeat(baseAssemblyDefinition.MainModule.FileName, 1)
            .Concat(referencedAssemblyDefinitions
                .Select(r => r.MainModule.FileName)
                .Where(f => !frameworkRefSet.Contains(Path.GetFileName(f))))
            .Select(f => MetadataReference.CreateFromFile(f))
            .ToList();
    }

    private static bool TryFindSourceFiles(string path, out string assemblyName, out string[] csFiles)
    {
        assemblyName = null;
        csFiles = null;
        if (File.Exists(path))
        {
            if (!path.EndsWith(".cs"))
                return false;
            csFiles = new[] { path };
            assemblyName = Path.GetFileNameWithoutExtension(path);
        }
        else
        {
            csFiles = Directory.GetFiles(path, "*.cs", SearchOption.AllDirectories);
            if (csFiles.Length == 0)
                return false;
            assemblyName = Path.GetFileName(path);
        }
        return true;
    }

    public void Compile(string path, bool debug = true)
    {
        Log.LogInfo($"Searching {path}");
        if (!TryFindSourceFiles(path, out var assemblyName, out var csFiles))
            return;
        Log.LogInfo($"Found {assemblyName} with {csFiles.Length} source files");

        if (PluginLoadContext.Exists(assemblyName) || PluginUnloadContext.Exists(assemblyName))
        {
            Log.LogWarning($"{assemblyName} already exists");
            return;
        }

        var syntaxTrees = csFiles
            .Select(csFile => {
                using var csStream = File.OpenRead(csFile);
                var sourceText = SourceText.From(csStream, canBeEmbedded: debug);
                return CSharpSyntaxTree.ParseText(sourceText, path: csFile);
            })
            .ToList();

        Log.LogInfo("Creating compilation");
        var compilation = CSharpCompilation.Create(assemblyName)
            .WithOptions(options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithOptimizationLevel(debug ? OptimizationLevel.Debug : OptimizationLevel.Release))
            .AddSyntaxTrees(syntaxTrees)
            .WithReferences(_frameworkReferences)
            .AddReferences(_bepInExReferences);

        Log.LogInfo("Compiling");
        using MemoryStream assemblyStream = new();
        var emitResult = compilation.Emit(assemblyStream,
            options: !debug ? null : new EmitOptions(
                debugInformationFormat: DebugInformationFormat.Embedded));

        foreach (var diag in emitResult.Diagnostics)
        {
            if (!Enum.TryParse<LogLevel>(Enum.GetName(diag.Severity), true, out var level))
                level = LogLevel.Info;
            Log.Log(level, diag.ToString());
        }
        if (!emitResult.Success)
            return;

        Log.LogInfo("Finding PluginInfo");
        assemblyStream.Seek(0, SeekOrigin.Begin);
        using var assemblyDefinition = AssemblyDefinition.ReadAssembly(assemblyStream,
            new() { AssemblyResolver = _assemblyResolver });

        var pluginInfo = ToPluginInfo(assemblyDefinition);
        _assemblyResolver.Trim();
        if (pluginInfo == null)
        {
            Log.LogWarning("Could not find any PluginInfo");
            return;
        }
        Log.LogInfo($"Found {nameof(pluginInfo.Metadata.GUID)}=[{pluginInfo.Metadata.GUID}] " +
            $"{nameof(pluginInfo.Metadata.Name)}=[{pluginInfo.Metadata.Name}] " +
            $"{nameof(pluginInfo.Metadata.Version)}=[{pluginInfo.Metadata.Version}]");

        if (PluginLoadContext.Exists(pluginInfo) || PluginUnloadContext.Exists(pluginInfo))
        {
            Log.LogWarning($"Plugin {pluginInfo.Metadata.GUID} already dynamically loaded");
            return;
        }

        if (IL2CPPChainloader.Instance.Plugins.ContainsKey(pluginInfo.Metadata.GUID))
        {
            Log.LogError($"Plugin {pluginInfo.Metadata.GUID} was statically loaded by chainloader, remove the plugin from {Path.GetRelativePath(Paths.GameDataPath, Paths.PluginPath)} and restart");
            return;
        }

        var ctx = PluginLoadContext.Create(pluginInfo, assemblyName, assemblyStream);
        if (!ctx.TryLoadPlugin())
        {
            Log.LogError($"Could not load {pluginInfo.Metadata.GUID}");
            ctx.Unload();
        }
    }

    /// <summary>
    /// Tries to find a <see cref="PluginInfo"/> in <paramref name="assemblyDefinition"/> and sets its <see cref="PluginInfo.Location"/> to an impossible value.<br/>
    /// <br/>
    /// When the <see cref="PluginInfo"/> is loaded by <see cref="IL2CPPChainloader"/> it will use <see cref="Assembly.LoadFrom(string)"/><br/>
    /// (See the <see langword="private"/> method <see cref="BaseChainloader{TPlugin}"/>.LoadPlugins(<see cref="IList{PluginInfo}"/>))<br/>
    /// which would normally cause a <see cref="FileNotFoundException"/>.<br/>
    /// <br/>
    /// A <see cref="HarmonyPrefix"/> intercepts the call to <see cref="Assembly.LoadFrom(string)"/> to act on the impossible <see cref="PluginInfo.Location"/><br></br>
    /// See <seealso cref="PluginLoadContext.Patches.Assembly_LoadFrom_Prefix(string, ref Assembly)"/>
    /// </summary>
    /// <returns><see cref="PluginInfo"/> with an impossible <see cref="PluginInfo.Location"/></returns>
    private static PluginInfo ToPluginInfo(AssemblyDefinition assemblyDefinition)
    {
        foreach (var t in assemblyDefinition.MainModule.Types)
        {
            var info = IL2CPPChainloader.ToPluginInfo(t, null);
            if (info == null)
                continue;

            Traverse.Create(info)
                .Property<string>(nameof(info.Location)).Value =
                    Path.Combine(typeof(Plugin).Assembly.Location, info.Metadata.GUID);
            return info;
        }
        return null;
    }
}
