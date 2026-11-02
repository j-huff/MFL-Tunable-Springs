using System.Globalization;
namespace MarsSpringTuner;

public sealed record Settings(bool Enabled, float[][] Springs)
{
    public static float[][] Defaults() => new[] {
        new float[]{400,30,30,30,30,.5f,.5f}, new float[]{1000,60,60,60,60,.5f,.5f},
        new float[]{4000,200,200,200,200,.5f,.5f}, new float[]{5000,20,20,20,20,.5f,.5f},
        new float[]{20000,100,100,100,100,.5f,.5f}, new float[]{50000,100,100,100,100,.5f,.5f}};

    public static Settings Parse(string text)
    {
        var words=text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries);
        if(words.Length<2 || (words[1]!="0" && words[1]!="1")) throw new FormatException("Invalid enabled flag");
        int version=int.Parse(words[0],CultureInfo.InvariantCulture);
        int count=version switch {1=>11,2=>23,3=>44,_=>throw new FormatException("Unknown tuning version")};
        if(words.Length!=count) throw new FormatException("Wrong number of tuning values");
        var rows=Defaults(); int offset=2;
        for(int k=0;k<(version==3?6:3);k++)
        {
            if(version==1) {
                float stiffness=Read(words[offset++]),compression=Read(words[offset++]),rebound=Read(words[offset++]);
                rows[k]=new[]{stiffness,compression,compression,rebound,rebound,.5f,.5f};
            } else for(int j=0;j<7;j++) rows[k][j]=Read(words[offset++]);
        }
        foreach(var row in rows) for(int j=0;j<7;j++) {
            float min=j>=5?.001f:0, max=j==0?100000:j>=5?100:20000;
            if(!float.IsFinite(row[j])||row[j]<min||row[j]>max) throw new FormatException("Tuning value outside allowed range");
        }
        return new Settings(words[1]=="1",rows);
    }
    static float Read(string value)=>float.Parse(value,NumberStyles.Float,CultureInfo.InvariantCulture);
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
