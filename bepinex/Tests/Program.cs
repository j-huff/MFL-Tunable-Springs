using MarsSpringTuner;
using System.Globalization;
void Check(bool ok,string name){if(!ok)throw new Exception(name);}
void Near(float value,float expected,string name)=>Check(MathF.Abs(value-expected)<.001f,name);
var row=new float[]{400,10,30,50,90,.5f,1};
Near(Damping.Speed(1,.99f,.02,false),.5f,"linear speed");
Near(Damping.Speed(-3.13f,3.13f,.02,true),1.15927f,"angular wrap");
Near(Damping.Speed(1,float.NaN,.02,false),0,"first sample");
Near(Damping.Speed(1,0,0,true),0,"zero time");
Near(Damping.Speed(1,0,2,true),0,"gap");
foreach(var direction in new[]{-1f,1f}) {
 Check(Damping.Compression(direction*.4f,0,direction,true,false),"winding");
 Check(!Damping.Compression(direction*.4f,0,-direction,true,true),"unwinding");
}
Near(Damping.Coefficient(row,.1f,true),10,"low compression");
Near(Damping.Coefficient(row,1,true),30,"high compression");
Near(Damping.Coefficient(row,.5f,true),20,"compression transition");
Near(Damping.Coefficient(row,.1f,false),50,"low rebound");
Near(Damping.Coefficient(row,2,false),90,"high rebound");
Near(Damping.Coefficient(row,1,false),70,"rebound transition");
var legacy=Settings.Parse("1 1 411.1 30 368.6 1000 60 60 4000 200 200");
Near(legacy.Springs[0][2],30,"v1 compression migration");Near(legacy.Springs[0][4],368.6f,"v1 rebound migration");
Near(legacy.Springs[3][0],5000,"torsion defaults");
string version3="3 1 "+string.Join(" ",legacy.Springs.SelectMany(x=>x).Select(x=>x.ToString(CultureInfo.InvariantCulture)));
Check(Settings.Parse(version3).Enabled,"v3 roundtrip");
foreach(string bad in new[]{"3 1",version3+" 1",version3.Replace("411.1","NaN"),version3.Replace("411.1","100001"),version3.Replace("0.5","0"),version3.Replace("3 1 ","3 2 ")}) {
 bool failed=false;try{Settings.Parse(bad);}catch(FormatException){failed=true;}Check(failed,"invalid config accepted");
}
Check(Damping.Kind(66)==3&&Damping.Kind(65)==4&&Damping.Kind(67)==5&&Damping.Kind(141)==0&&Damping.Kind(1)==-1,"part mapping");
Console.WriteLine("Managed physics, six-family mapping, config migration and validation tests passed.");
