using Il2CppInterop.Runtime;

namespace MarsSpringTuner;

/// <summary>Settings belong to native vehicle nodes, never part definitions or body pointers.</summary>
public static class NodeSettings
{
    sealed record Entry(nint WeakHandle,float[] Values);
    static readonly Dictionary<IntPtr,Entry> entries=new();
    public static bool IsSpring(Vehicle.Node? node)=>node!=null&&Damping.Kind(node.partId)>=0;

    // Return a copy to UI/persistence callers so editing one node cannot mutate another.
    public static float[]? Get(Vehicle.Node node)=>GetLive(node)?.ToArray();
    internal static float[]? GetLive(Vehicle.Node node)
    {
        if(!entries.TryGetValue(node.Pointer,out var entry))return null;
        if(IL2CPP.il2cpp_gchandle_get_target(entry.WeakHandle)==node.Pointer)return entry.Values;
        Remove(node.Pointer,entry);return null;
    }
    public static void Set(Vehicle.Node node,float[]? values)
    {
        if(!IsSpring(node))throw new ArgumentException("Only spring nodes can be tuned",nameof(node));
        // Validate and clone before changing the previous setting.
        float[]? copy=values?.ToArray();if(copy!=null)Settings.ValidateValues(copy);
        if(entries.TryGetValue(node.Pointer,out var old))Remove(node.Pointer,old);
        if(copy==null)return;
        nint handle=IL2CPP.il2cpp_gchandle_new_weakref(node.Pointer,false);
        if(handle==0)throw new InvalidOperationException("Could not track spring node lifetime");
        entries.Add(node.Pointer,new Entry(handle,copy));
    }
    static void Remove(IntPtr key,Entry entry){entries.Remove(key);IL2CPP.il2cpp_gchandle_free(entry.WeakHandle);}
    public static void Prune()
    {
        foreach(var pair in entries.ToArray())
            if(IL2CPP.il2cpp_gchandle_get_target(pair.Value.WeakHandle)==IntPtr.Zero)Remove(pair.Key,pair.Value);
    }
    public static void Clear()
    {
        foreach(var entry in entries.Values)IL2CPP.il2cpp_gchandle_free(entry.WeakHandle);
        entries.Clear();
    }
}
