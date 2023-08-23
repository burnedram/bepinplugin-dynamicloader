using System;
using System.Reflection;
using System.Runtime.Versioning;

namespace DynamicLoader.Windows;

public class COMObject
{
    [SupportedOSPlatform("windows")]
    public static COMObject Create(string progID)
    {
        var type = Type.GetTypeFromProgID(progID);
        var target = Activator.CreateInstance(type);
        return new COMObject(type, target);
    }

    public Type COMType { get; }
    public object Target { get; }

    private COMObject(Type type, object target)
    {
        COMType = type;
        Target = target;
    }

    public COMObject Invoke(string memberName, params object[] args)
    {
        var val = COMType.InvokeMember(memberName, BindingFlags.InvokeMethod, null, Target, args, null);
        return new COMObject(val.GetType(), val);
    }

    public COMObject Get(string memberName)
    {
        var val = COMType.InvokeMember(memberName, BindingFlags.GetProperty, null, Target, null, null);
        return new COMObject(val.GetType(), val);
    }

    public T Get<T>(string memberName)
    {
        return (T)COMType.InvokeMember(memberName, BindingFlags.GetProperty, null, Target, null, null);
    }
}