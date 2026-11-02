using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
namespace MarsSpringTuner;

public static class Persistence
{
    sealed class StreamSnapshot : IDisposable {
        public nint Handle; public SaveTrailer.Snapshot Data;
        public StreamSnapshot(nint pointer,SaveTrailer.Snapshot data){Handle=IL2CPP.il2cpp_gchandle_new_weakref(pointer,false);if(Handle==0)throw new InvalidOperationException("Could not track undo stream lifetime");Data=data;}
        public void Dispose(){if(Handle!=0)IL2CPP.il2cpp_gchandle_free(Handle);Handle=0;}
    }
    sealed class EditorState : IDisposable {
        public nint Handle;public readonly Dictionary<nint,StreamSnapshot> Streams=new();public UndoContext? Context;
        public EditorState(nint pointer){Handle=IL2CPP.il2cpp_gchandle_new_weakref(pointer,false);if(Handle==0)throw new InvalidOperationException("Could not track editor lifetime");}
        public void Dispose(){foreach(var entry in Streams.Values)entry.Dispose();Streams.Clear();if(Handle!=0)IL2CPP.il2cpp_gchandle_free(Handle);Handle=0;Context=null;}
    }
    sealed class UndoContext {
        public EditorState Editor=null!;public int Index;public SaveTrailer.Snapshot Current=null!;public SaveTrailer.Snapshot? Target;
    }
    sealed class DesignContext {
        public nint Editor;public int Slot;public bool Resave;public SaveTrailer.Snapshot? Snapshot;
        public MemBuf? Buffer;public byte[]? Original;public int OriginalPosition;
    }
    [ThreadStatic] static List<DesignContext>? Designs;
    static readonly Dictionary<nint,EditorState> Editors=new();
    static void Warn(Exception ex)=>Plugin.Instance?.Log.LogWarning("Spring tuning persistence: "+ex.Message);
    static void Guard(Action action){try{action();}catch(Exception ex){Warn(ex);}}
    public static void Clear(){foreach(var editor in Editors.Values)editor.Dispose();Editors.Clear();Designs?.Clear();}
    public static void Install(Harmony harmony) {
        void Patch(Type type,string method,string? prefix=null,string? postfix=null,string? finalizer=null) {
            HarmonyMethod? Hook(string? name)=>name==null?null:new HarmonyMethod(typeof(Persistence),name);
            harmony.Patch(AccessTools.Method(type,method)??throw new MissingMethodException(type.FullName,method),Hook(prefix),Hook(postfix),null,Hook(finalizer),null);
        }
        Patch(typeof(Vehicle.Node),nameof(Vehicle.Node.WriteVehicleSave),postfix:nameof(WriteVehicle));
        Patch(typeof(Vehicle.Node),nameof(Vehicle.Node.ToBase64),postfix:nameof(ToBase64));
        Patch(typeof(Vehicle.Node),nameof(Vehicle.Node.ReadSavedVehicle),nameof(BeforeRead),nameof(ReadVehicle));
        Patch(typeof(Vehicle.Node),nameof(Vehicle.Node.Copy),postfix:nameof(Copy));
        Patch(typeof(Vehicle.Node),nameof(Vehicle.Node.CloneNoChildren),postfix:nameof(Clone));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.SaveVehicleToSlot),nameof(BeforeSaveDesign),finalizer:nameof(FinishDesign));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.ResaveVehicle),nameof(BeforeResaveDesign),finalizer:nameof(FinishDesign));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.WriteVehiclePNG),prefix:nameof(WriteDesign));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.PushUndoStack),postfix:nameof(PushUndo));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.Undo),nameof(BeforeUndo),nameof(AfterUndo),nameof(FinishUndo));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.Redo),nameof(BeforeRedo),nameof(AfterUndo),nameof(FinishUndo));
        Patch(typeof(VehicleEditor),nameof(VehicleEditor.ResetVehicle),prefix:nameof(BeforeReset));
    }
    static (Vehicle.Node[] Nodes,SaveTrailer.Node[] Shape) Flatten(Vehicle.Node root) {
        var nodes=new List<Vehicle.Node>();var shape=new List<SaveTrailer.Node>();var seen=new HashSet<nint>();
        var pending=new Stack<(Vehicle.Node Node,int Parent,int Slot,int Depth)>();pending.Push((root,-1,0,0));
        while(pending.Count>0){var item=pending.Pop();
            if(nodes.Count>=SaveTrailer.MaxNodes || item.Depth>512 || !seen.Add(item.Node.Pointer))throw new FormatException("Vehicle tree exceeds tuning limits or contains a cycle");
            int index=nodes.Count;nodes.Add(item.Node);shape.Add(new(item.Node.partId,item.Parent,item.Slot));
            var children=item.Node.children;if(children==null)continue;
            for(int i=children.Length-1;i>=0;i--){var child=children[i];if(child!=null)pending.Push((child,index,i,item.Depth+1));}
        }
        return (nodes.ToArray(),shape.ToArray());
    }
    static SaveTrailer.Snapshot Capture(Vehicle.Node root) {
        var tree=Flatten(root);var entries=new List<SaveTrailer.Entry>();
        for(int i=0;i<tree.Nodes.Length;i++){var node=tree.Nodes[i];if(!NodeSettings.IsSpring(node))continue;var values=NodeSettings.Get(node);if(values!=null)entries.Add(new(i,values));}
        return SaveTrailer.Capture(tree.Shape,entries);
    }
    static void Apply(Vehicle.Node root,SaveTrailer.Snapshot snapshot) {
        var tree=Flatten(root);
        if(!SaveTrailer.Matches(snapshot,tree.Shape))throw new FormatException("Vehicle topology does not match spring tuning");
        foreach(var e in snapshot.Entries)if(!NodeSettings.IsSpring(tree.Nodes[e.Index]))throw new FormatException("Tuning refers to a non-spring part");
        var values=snapshot.Entries.ToDictionary(e=>e.Index,e=>e.Values);
        var rollback=new List<(Vehicle.Node Node,float[]? Values)>();
        try {for(int i=0;i<tree.Nodes.Length;i++){var node=tree.Nodes[i];if(!NodeSettings.IsSpring(node))continue;
            rollback.Add((node,NodeSettings.Get(node)));NodeSettings.Set(node,values.TryGetValue(i,out var row)?(float[])row.Clone():null);
        }} catch {foreach(var item in rollback)NodeSettings.Set(item.Node,item.Values);throw;}
    }
    static void WriteVehicle(Vehicle.Node __instance,MemBuf buf)=>Guard(()=>{
        var snapshot=Capture(__instance);if(snapshot.Entries.Length==0)return;
        var trailer=SaveTrailer.Append(Array.Empty<byte>(),snapshot);
        buf.WriteBytes(new Il2CppStructArray<byte>(trailer),trailer.Length,0);
    });
    static void ToBase64(Vehicle.Node __instance,ref string __result) {
        try {var snapshot=Capture(__instance);if(snapshot.Entries.Length>0)__result=Convert.ToBase64String(SaveTrailer.Append(Convert.FromBase64String(__result),snapshot));}catch(Exception ex){Warn(ex);}
    }
    static void BeforeRead(MemBuf buf,out int __state)=>__state=buf.pos;
    static void ReadVehicle(MemBuf buf,Vehicle.Node __result,int __state)=>Guard(()=>{
        if(__result==null)return;
        if(buf.len>=52) {
            // PNG extraction adds arbitrary unused pixel bytes after the trailer. Read
            // forward from the native parser boundary, including its four-byte name probe.
            int offset=Math.Max(__state,buf.pos-4), length=(int)Math.Min((long)buf.len-offset,SaveTrailer.MaxPayload+56L);
            if(length>=52) {
                var bytes=new byte[length];var source=buf.bytes;for(int i=0;i<length;i++)bytes[i]=source[offset+i];
                if(SaveTrailer.TryReadPadded(bytes,buf.pos-offset,out var snapshot,out int nativeLength) && nativeLength+offset>=__state) {
                    Apply(__result,snapshot!);
                    Plugin.Instance?.Log.LogInfo($"Restored tuning for {snapshot!.Entries.Length} springs from vehicle data.");
                }
            }
        }
        // ResaveVehicle decodes the saved design before clearing and rewriting its
        // buffer. Keep this snapshot, rather than using the unrelated editor vehicle.
        var context=Designs?.LastOrDefault();
        if(context is {Resave:true,Snapshot:null} && context.Buffer?.Pointer==buf.Pointer)context.Snapshot=Capture(__result);
    });
    static DesignContext BeginDesign(VehicleEditor editor,int slot,bool resave) {
        var context=new DesignContext{Editor=editor.Pointer,Slot=slot,Resave=resave};
        var infos=editor.savedVehicleInfos;
        if(infos!=null && slot>0 && slot<=infos.Count) {
            var info=infos[slot-1];var buf=info?.buf;
            if(buf!=null) {
                context.Buffer=buf;context.OriginalPosition=buf.pos;
                context.Original=new byte[buf.len];var source=buf.bytes;
                for(int i=0;i<buf.len;i++)context.Original[i]=source[i];
            }
        }
        if(!resave)context.Snapshot=Capture(editor.vehicleTree);
        (Designs??=new()).Add(context);return context;
    }
    static bool BeforeSaveDesign(VehicleEditor __instance,int slot,out DesignContext? __state) {
        __state=null;try{__state=BeginDesign(__instance,slot,false);return true;}catch(Exception ex){Warn(ex);return false;}
    }
    static bool BeforeResaveDesign(VehicleEditor __instance,int slot,out DesignContext? __state) {
        __state=null;try{__state=BeginDesign(__instance,slot,true);return true;}catch(Exception ex){Warn(ex);return false;}
    }
    static void RestoreDesignBuffer(DesignContext context) {
        if(context.Buffer==null || context.Original==null)return;
        var buf=context.Buffer;buf.len=0;buf.pos=0;
        buf.WriteBytes(new Il2CppStructArray<byte>(context.Original),context.Original.Length,0);
        buf.pos=Math.Min(context.OriginalPosition,buf.len);
    }
    static bool WriteDesign(VehicleEditor __instance,int slot,VehicleEditor.SaveVehicleInfo info,UnityEngine.Texture2D tex) {
        var context=Designs?.LastOrDefault();
        if(context==null || context.Editor!=__instance.Pointer || context.Slot!=slot)return true;
        try {
            var snapshot=context.Snapshot;
            if(snapshot==null)throw new InvalidOperationException("Saved design tuning snapshot was unavailable");
            if(snapshot.Entries.Length==0)return true;
            var trailer=SaveTrailer.Append(Array.Empty<byte>(),snapshot);var buf=info.buf;
            var pixels=tex.GetRawTextureData();int capacity=0;
            for(int i=0;i+3<pixels.Length;i+=4)if(pixels[i]<5)capacity+=3;
            if((long)buf.len+trailer.Length>capacity)throw new InvalidOperationException($"Saved design slot {slot} was not written: tuning exceeds its PNG capacity ({capacity} bytes)");
            buf.pos=buf.len;buf.WriteBytes(new Il2CppStructArray<byte>(trailer),trailer.Length,0);
            Plugin.Instance?.Log.LogInfo($"Saving tuning for {snapshot.Entries.Length} springs in saved design slot {slot}.");
            return true;
        } catch(Exception ex) {
            Guard(()=>RestoreDesignBuffer(context));Warn(ex);return false;
        }
    }
    static void FinishDesign(DesignContext? __state) {if(__state!=null)Designs?.Remove(__state);}
    static void Copy(Vehicle.Node __instance,Vehicle.Node __result)=>Guard(()=>{if(__result!=null)Apply(__result,Capture(__instance));});
    static void Clone(Vehicle.Node __instance,Vehicle.Node __result)=>Guard(()=>{
        if(__result!=null && __instance.partId==__result.partId && NodeSettings.IsSpring(__instance))NodeSettings.Set(__result,NodeSettings.Get(__instance));
    });
    static EditorState State(VehicleEditor editor) {
        foreach(var key in Editors.Where(e=>IL2CPP.il2cpp_gchandle_get_target(e.Value.Handle)!=e.Key).Select(e=>e.Key).ToArray()){Editors[key].Dispose();Editors.Remove(key);}
        if(Editors.TryGetValue(editor.Pointer,out var state))return state;
        if(Editors.Count>=8){var first=Editors.First();first.Value.Dispose();Editors.Remove(first.Key);}
        state=new EditorState(editor.Pointer);Editors.Add(editor.Pointer,state);return state;
    }
    static void Prune(VehicleEditor editor,EditorState state) {
        var active=new HashSet<nint>();var stack=editor.undoStack;
        if(stack!=null)for(int i=0;i<stack.Count;i++){var stream=stack[i];if(stream!=null)active.Add(stream.Pointer);}
        foreach(var key in state.Streams.Where(e=>!active.Contains(e.Key) || IL2CPP.il2cpp_gchandle_get_target(e.Value.Handle)!=e.Key).Select(e=>e.Key).ToArray()){state.Streams[key].Dispose();state.Streams.Remove(key);}
    }
    static void Store(VehicleEditor editor,EditorState state,int index,SaveTrailer.Snapshot snapshot) {
        Prune(editor,state);var stack=editor.undoStack;if(stack==null || index<0 || index>=stack.Count)return;
        var stream=stack[index];if(stream==null)return;
        if(state.Streams.Remove(stream.Pointer,out var prior))prior.Dispose();
        // Native undo limit is 100; protect against a changed or malformed game structure.
        if(state.Streams.Count>=256)return;
        state.Streams[stream.Pointer]=new StreamSnapshot(stream.Pointer,snapshot);
    }
    static void PushUndo(VehicleEditor __instance)=>Guard(()=>{
        var state=State(__instance);Store(__instance,state,__instance.undoStackPos-1,Capture(__instance.vehicleTree));
    });
    static UndoContext? BeginUndo(VehicleEditor editor,bool redo) {
        int pos=editor.undoStackPos;if(redo?pos>=editor.undoStackSize:pos<=0)return null;
        int index=redo?pos:pos-1;var stack=editor.undoStack;if(stack==null || index<0 || index>=stack.Count)return null;
        var state=State(editor);Prune(editor,state);var target=stack[index];
        var context=new UndoContext{Editor=state,Index=index,Current=Capture(editor.vehicleTree)};
        if(target!=null && state.Streams.TryGetValue(target.Pointer,out var saved))context.Target=saved.Data;
        state.Context=context;return context;
    }
    static void BeforeUndo(VehicleEditor __instance,out UndoContext? __state){__state=null;try{__state=BeginUndo(__instance,false);}catch(Exception ex){Warn(ex);}}
    static void BeforeRedo(VehicleEditor __instance,out UndoContext? __state){__state=null;try{__state=BeginUndo(__instance,true);}catch(Exception ex){Warn(ex);}}
    static void BeforeReset(VehicleEditor __instance,Vehicle.Node veh)=>Guard(()=>{
        if(Editors.TryGetValue(__instance.Pointer,out var state) && state.Context?.Target is { } target)Apply(veh,target);
    });
    static void AfterUndo(VehicleEditor __instance,UndoContext? __state)=>Guard(()=>{
        if(__state==null)return;Store(__instance,__state.Editor,__state.Index,__state.Current);
    });
    static void FinishUndo(UndoContext? __state){if(__state!=null && ReferenceEquals(__state.Editor.Context,__state))__state.Editor.Context=null;}
}
