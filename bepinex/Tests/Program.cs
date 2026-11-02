using MarsSpringTuner;
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
Near(Settings.Defaults()[3][0],5000,"torsion defaults");
Check(Damping.Kind(66)==3&&Damping.Kind(65)==4&&Damping.Kind(67)==5&&Damping.Kind(141)==0&&Damping.Kind(1)==-1,"part mapping");
Console.WriteLine("Managed physics, six-family mapping, default-value tests passed.");

PersistenceTests.Run();

for(int index=0;index<7;index++)for(int step=0;step<=10000;step++) {
    var values=Settings.Defaults()[0];values[index]=SliderScale.Decode(index,step/10000f);Settings.ValidateValues(values);
}
Near(SliderScale.Decode(5,1),100,"crossover maximum rounding");
Near(SliderScale.Decode(5,0),.001f,"crossover minimum");
var vehicle=new BulkNode(0,1,new[]{new BulkNode(1,59),new BulkNode(2,141),new BulkNode(3,58),new BulkNode(4,66),new BulkNode(5,1,new[]{new BulkNode(6,142)})});
var otherVehicle=new BulkNode(7,1,new[]{new BulkNode(8,59)});
var selected=BulkTuning.Matching(vehicle,59,n=>n.PartId,n=>n.Children,n=>n.Identity);
Check(selected.Select(n=>n.Identity).Order().SequenceEqual(new long[]{1,2,6}),"bulk excludes other types/vehicles and includes matching variants");
Check(BulkTuning.Matching(vehicle,66,n=>n.PartId,n=>n.Children,n=>n.Identity).Single().Identity==4,"torsion only bulk");
Console.WriteLine("All slider endpoints/intermediate values and vehicle-scoped Apply All selection tests passed.");
record BulkNode(long Identity,int PartId,BulkNode[] Children) {public BulkNode(long id,int part):this(id,part,Array.Empty<BulkNode>()) {}}
