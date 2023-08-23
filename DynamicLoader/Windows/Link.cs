using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DynamicLoader.Windows;

public static class Link
{
    public static IEnumerable<string> Resolve(string[] links)
    {
        if (!OperatingSystem.IsWindows())
            return Enumerable.Empty<string>();

        if (links.Length == 0)
            return Enumerable.Empty<string>();

        var shell = COMObject.Create("Shell.Application");
        return links.Select(lnkFile =>
            shell.Invoke("NameSpace", Path.GetDirectoryName(lnkFile))
                .Invoke("Items")
                .Invoke("Item", Path.GetFileName(lnkFile))
                .Get("GetLink")
                .Get("Target")
                .Get<string>("Path"))
            .Where(p => Directory.Exists(p) || File.Exists(p))
            .Distinct();
    }
}
