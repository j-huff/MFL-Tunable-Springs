namespace MarsSpringTuner;

public static class Settings
{
    public static float[][] Defaults() => new[] {
        new float[]{400,30,30,30,30,.5f,.5f}, new float[]{1000,60,60,60,60,.5f,.5f},
        new float[]{4000,200,200,200,200,.5f,.5f}, new float[]{5000,20,20,20,20,.5f,.5f},
        new float[]{20000,100,100,100,100,.5f,.5f}, new float[]{50000,100,100,100,100,.5f,.5f}};

    public static void ValidateValues(float[] row) {
        if(row.Length!=7)throw new FormatException("Each spring needs seven tuning values");
        for(int j=0;j<7;j++) {
            float min=j>=5?.001f:0, max=j==0?100000:j>=5?100:20000;
            if(!float.IsFinite(row[j])||row[j]<min||row[j]>max) throw new FormatException("Tuning value outside allowed range");
        }
    }
}
public static class Damping
{
    public static int Kind(int id)=>id switch {59 or 141 or 142=>0,58 or 139 or 140=>1,60 or 143 or 144=>2,66=>3,65=>4,67=>5,_=>-1};
    public static float Wrap(float radians)=>(float)Math.IEEERemainder(radians,Math.PI*2);
    public static float Speed(float position,float previous,double dt,bool angular) {
        if(!float.IsFinite(position)||!float.IsFinite(previous)||!double.IsFinite(dt)||dt<=0||dt>.25) return 0;
        float delta=angular?Wrap(position-previous):position-previous;
        float speed=(float)(delta/dt);return float.IsFinite(speed)?speed:0;
    }
    public static bool Compression(float position,float targetDegrees,float speed,bool angular,bool previous) {
        float direction=angular?Wrap(position-targetDegrees*(MathF.PI/180))*speed:-speed;
        return direction>0.00001f?true:direction< -0.00001f?false:previous;
    }
    public static float Coefficient(float[] row,float speed,bool compression) {
        float threshold=row[compression?5:6];
        float blend=Math.Clamp((MathF.Abs(speed)/threshold-.9f)/.2f,0,1);
        int low=compression?1:3;return row[low]+blend*(row[low+1]-row[low]);
    }
}

public static class SliderScale
{
    public static float Max(int index)=>index==0?100000:index<5?20000:100;
    public static float Decode(int index,float normalized) {
        float value=Math.Clamp(normalized,0,1);
        float decoded=index>=5?.001f*MathF.Pow(100000,value):MathF.Exp(value*MathF.Log(1+Max(index)))-1;
        return Math.Clamp(decoded,index>=5?.001f:0,Max(index));
    }
    public static float Encode(int index,float value)=>Math.Clamp(index>=5?MathF.Log(Math.Max(.001f,value)/.001f)/MathF.Log(100000):MathF.Log(1+Math.Max(0,value))/MathF.Log(1+Max(index)),0,1);
}

public static class BulkTuning
{
    // Pure tree selection: scope comes only from the supplied vehicle root.
    public static List<T> Matching<T>(T root,int sourceId,Func<T,int> id,Func<T,IEnumerable<T>> children,Func<T,long> identity) where T:class {
        int kind=Damping.Kind(sourceId);if(kind<0)throw new ArgumentException("Not a spring type");
        var matches=new List<T>();var stack=new Stack<T>();var seen=new HashSet<long>();stack.Push(root);
        while(stack.Count>0) {
            var node=stack.Pop();if(!seen.Add(identity(node)))continue;
            if(seen.Count>SaveTrailer.MaxNodes)throw new InvalidOperationException("Vehicle exceeds supported size");
            if(Damping.Kind(id(node))==kind)matches.Add(node);
            foreach(var child in children(node))if(child!=null)stack.Push(child);
        }
        return matches;
    }
}
