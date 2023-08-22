using Mono.Cecil;
using System;
using System.Collections.Generic;

namespace DynamicLoader.Compiler;
public class SlimAssemblyResolver : BaseAssemblyResolver
{
    private readonly Dictionary<string, AssemblyDefinition> cache = new(StringComparer.Ordinal);

    public override AssemblyDefinition Resolve(AssemblyNameReference name)
    {
        if (name == null)
            throw new ArgumentNullException(nameof(name));

        if (cache.TryGetValue(name.FullName, out var assembly))
            return assembly;

        return cache[name.FullName] = base.Resolve(name);
    }

    public void Trim()
    {
        foreach (var assembly in cache.Values)
            assembly.Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        foreach (var assembly in cache.Values)
            assembly.Dispose();
        cache.Clear();
        base.Dispose (disposing);
    }
}
