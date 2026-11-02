using BepInEx;
namespace MarsSpringTuner;

// Developer-only, opt-in checks on detached copies. Never modifies a live node or disk save.
internal static class IntegrationChecks
{
    static Vehicle.Node? pending;
    static bool checkedRequest;
    static readonly string Request=Path.Combine(Paths.GameRootPath,"spring-tuner",".run-integration-checks");
    public static void Queue(Vehicle.Node node){if(!checkedRequest&&pending==null)pending=node;}
    public static void RunPending()
    {
        if(checkedRequest||pending==null)return;
        checkedRequest=true;var source=pending;pending=null;
        if(!File.Exists(Request))return;
        File.Delete(Request);
        var temporary=new List<Vehicle.Node>();
        try {
            void Assert(bool value,string label){if(!value)throw new Exception(label);}
            var a=source.CloneNoChildren();var b=source.CloneNoChildren();temporary.Add(a);temporary.Add(b);
            var first=Settings.Defaults()[Damping.Kind(a.partId)];first[0]=1234;
            var second=(float[])first.Clone();second[0]=2345;
            NodeSettings.Set(a,first);NodeSettings.Set(b,second);
            Assert(NodeSettings.Get(a)![0]==1234&&NodeSettings.Get(b)![0]==2345,"same-ID instances were linked");
            var copy=a.Copy();temporary.Add(copy);Assert(NodeSettings.Get(copy)?[0]==1234,"Node.Copy lost settings");
            var clone=a.CloneNoChildren();temporary.Add(clone);Assert(NodeSettings.Get(clone)?[0]==1234,"CloneNoChildren lost settings");
            string encoded=a.ToBase64();var loaded=Vehicle.Node.FromBase64(encoded);temporary.Add(loaded);
            Assert(NodeSettings.Get(loaded)?[0]==1234,"base64 save roundtrip lost settings");
            var buf=new MemBuf();a.WriteVehicleSave(buf,"Spring tuning test");buf.pos=0;
            var named=Vehicle.Node.ReadSavedVehicle(buf,out string name);temporary.Add(named);
            Assert(name=="Spring tuning test"&&NodeSettings.Get(named)?[0]==1234,"named vehicle save roundtrip failed");
            // PNG transport returns all eligible pixels, including unused/nonzero padding.
            void CheckPadded(string? savedName) {
                var padded=new MemBuf();a.WriteVehicleSave(padded,savedName!);
                var padding=new byte[65537];for(int i=0;i<padding.Length;i++)padding[i]=(byte)(i%251);
                padded.WriteBytes(new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(padding),padding.Length,0);
                padded.pos=0;var restored=Vehicle.Node.ReadSavedVehicle(padded,out string restoredName);temporary.Add(restored);
                Assert(NodeSettings.Get(restored)?[0]==1234,"PNG padded vehicle roundtrip lost settings");
                if(savedName!=null)Assert(restoredName==savedName,"PNG padded vehicle lost name");
            }
            CheckPadded("Padded spring tuning test");CheckPadded(null);
            NodeSettings.Set(copy,null);
            Assert(NodeSettings.Get(a)?[0]==1234&&NodeSettings.Get(copy)==null,"reset affected original instance");
            Plugin.Instance?.Log.LogInfo("INTEGRATION CHECKS PASSED: independent same-ID nodes, copy, clone, base64, named/unnamed vehicle saves with PNG padding, reset isolation. Live nodes and disk saves unchanged.");
        } catch(Exception ex){Plugin.Instance?.Log.LogError("INTEGRATION CHECKS FAILED: "+ex);}
        finally {foreach(var node in temporary)NodeSettings.Set(node,null);}
    }
}
