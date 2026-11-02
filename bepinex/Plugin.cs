using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Attributes;
using System.Diagnostics;
using System.Security.Cryptography;
using UnityEngine;
using Object=UnityEngine.Object;

namespace MarsSpringTuner;

[BepInPlugin(Id,"Mars First Logistics Spring Tuner","2.1.1")]
[BepInProcess("Mars First Logistics.exe")]
public sealed class Plugin : BasePlugin
{
    public const string Id="local.marsfirstlogistics.springtuner";
    internal static Plugin? Instance;
    internal static TunerBehaviour? Runner;
    Harmony? harmony;
    public override void Load()
    {
        Instance=this;
        var binary=Path.Combine(Paths.GameRootPath,"GameAssembly.dll");
        using var input=File.OpenRead(binary);
        using var sha=SHA256.Create();
        string hash=Convert.ToHexString(sha.ComputeHash(input)).ToLowerInvariant();
        if(hash!="c687618a07b873008a1b045f39d1d8157de35737efaa7138ccc586026200fdc8") {
            WriteFailure("Unsupported game build; no patches installed");return;
        }
        try {
            Runner=AddComponent<TunerBehaviour>();
            Runner.Initialize(this);
            harmony=new Harmony(Id);
            // Observe completed creation; don't detour the shared empty HingeArticulation.FixedUpdate.
            var post=new HarmonyMethod(typeof(Plugin).GetMethod(nameof(PartCreated))!);
            harmony.Patch(AccessTools.Method(typeof(SpringSO),nameof(SpringSO.Init)),postfix:post);
            harmony.Patch(AccessTools.Method(typeof(HingeSO),nameof(HingeSO.Init)),postfix:post);
            Persistence.Install(harmony);
            MenuTuning.Install(harmony);
            Log.LogInfo("Spring Tuner 2.1.1 loaded: per-part tuning in Configure controls, vehicle saves, copy and undo/redo.");
        } catch(Exception ex) {
            harmony?.UnpatchSelf();
            if(Runner!=null) Object.Destroy(Runner);
            Runner=null;MenuTuning.Clear();Persistence.Clear();NodeSettings.Clear();WriteFailure("Plugin initialization failed: "+ex.Message);Log.LogError(ex);
        }
    }
    public static void PartCreated(Part __result)
    {
        try { Runner?.Register(__result); }
        catch(Exception ex) { Instance?.Log.LogError("Unable to register spring: "+ex); }
    }
    void WriteFailure(string message) {
        Log.LogError(message);

    }
    public override bool Unload()
    {
        harmony?.UnpatchSelf();
        Runner?.RestoreAll();
        if(Runner!=null) Object.Destroy(Runner);
        Runner=null;MenuTuning.Clear();Persistence.Clear();NodeSettings.Clear();Instance=null;return true;
    }
}

public sealed class TunerBehaviour : MonoBehaviour
{
    public TunerBehaviour(IntPtr pointer):base(pointer) {}
    sealed class SpringState {
        public ArticulationBody Body=null!;
        public Spring? Linear;
        public Vehicle.Node Node=null!;
        public int Id;
        public int Kind;
        public ArticulationDrive Stock;
        public float Position=float.NaN,Speed;
        public double PhysicsTime,LastSeen;
        public bool Compression,Modified;
        public float AppliedStiffness,AppliedDamping;
    }
    readonly Dictionary<IntPtr,SpringState> springs=new();
    Plugin? plugin;
    double nextPrune;
    static double Now=>(double)Stopwatch.GetTimestamp()/Stopwatch.Frequency;

    [HideFromIl2Cpp]
    public void Initialize(Plugin owner) {
        plugin=owner;
    }
    [HideFromIl2Cpp]
    public void Register(Part part) {
        if(part==null)return;
        int kind=Damping.Kind(part.id);if(kind<0)return;
        var articulation=part.TryCast<ArticulationBodyPart>();
        if(articulation==null) {plugin?.Log.LogWarning($"Part {part.id} uses non-articulation physics; unsupported");return;}
        var body=articulation.articulationBody;
        if(body==null)return;
        IntPtr key=body.Pointer;
        if(springs.ContainsKey(key))return;
        var linear=kind<3?part.TryCast<Spring>():null;
        if(kind<3&&linear==null)return;
        var state=new SpringState {Body=body,Linear=linear,Id=part.id,Kind=kind,Node=part.noderef,Stock=body.xDrive};
        springs[key]=state;
        IntegrationChecks.Queue(part.noderef);
        plugin?.Log.LogDebug($"Registered {(kind>=3?"torsion":"linear")} spring ID {part.id}");
    }
    public void Update() {
        if(plugin==null)return;
        IntegrationChecks.RunPending();
        double now=Now;
        if(now>=nextPrune){nextPrune=now+5;NodeSettings.Prune();}
    }
    public void FixedUpdate() {
        if(plugin==null)return;
        double time=Time.fixedTimeAsDouble,now=Now;
        foreach(var pair in springs.ToArray()) {
            var s=pair.Value;
            try {
                if(s.Body==null){springs.Remove(pair.Key);continue;}
                if(!s.Body.gameObject.activeInHierarchy){s.Position=float.NaN;continue;}
                var tuning=NodeSettings.GetLive(s.Node);
                if(tuning==null){Restore(s);s.Position=float.NaN;continue;}
                var drive=s.Body.xDrive;
                bool angular=s.Kind>=3;
                var position=angular?s.Body.jointPosition[0]:s.Linear!.CalcCurrDist();
                if(!float.IsFinite(position))continue;
                s.Speed=Damping.Speed(position,s.Position,time-s.PhysicsTime,angular);
                s.Compression=Damping.Compression(position,drive.target,s.Speed,angular,s.Compression);
                s.Position=position;s.PhysicsTime=time;s.LastSeen=now;
                float stiffness=tuning!=null?tuning[0]:s.Stock.stiffness;
                float damping=tuning!=null?Damping.Coefficient(tuning,s.Speed,s.Compression):s.Stock.damping;
                float force=tuning!=null?MathF.Max(s.Stock.forceLimit,stiffness):s.Stock.forceLimit;
                if(drive.stiffness!=stiffness||drive.damping!=damping||drive.forceLimit!=force) {
                    drive.stiffness=stiffness;drive.damping=damping;drive.forceLimit=force;
                    s.Body.xDrive=drive;
                }
                s.Modified=tuning!=null;
                var actual=s.Body.xDrive;s.AppliedStiffness=actual.stiffness;s.AppliedDamping=actual.damping;
            } catch(Exception ex) {
                Restore(s);springs.Remove(pair.Key);plugin.Log.LogError($"Stopped tuning part {s.Id}: {ex.Message}");
            }
        }
    }
    [HideFromIl2Cpp]
    void Restore(SpringState s) {
        if(!s.Modified)return;
        try {
            if(s.Body!=null){var d=s.Body.xDrive;d.stiffness=s.Stock.stiffness;d.damping=s.Stock.damping;d.forceLimit=s.Stock.forceLimit;s.Body.xDrive=d;}
            s.Modified=false;s.AppliedStiffness=s.Stock.stiffness;s.AppliedDamping=s.Stock.damping;
        } catch(Exception ex) {plugin?.Log.LogWarning("Could not restore destroyed spring: "+ex.Message);}
    }
    [HideFromIl2Cpp]
    public void RestoreAll(){foreach(var s in springs.Values)Restore(s);}
    public void OnDestroy(){RestoreAll();springs.Clear();}
}
