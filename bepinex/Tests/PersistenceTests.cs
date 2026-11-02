using System.Buffers.Binary;
namespace MarsSpringTuner;
public static class PersistenceTests
{
    public static void Run() {
        static void Check(bool ok,string name){if(!ok)throw new Exception("Persistence: "+name);}
        static bool Reject(Action action){try{action();return false;}catch(FormatException){return true;}}
        var shape=new[]{new SaveTrailer.Node(1,-1,0),new SaveTrailer.Node(59,0,0),new SaveTrailer.Node(59,0,3),new SaveTrailer.Node(66,2,0)};
        float[] a={400,10,30,50,90,.5f,1},b={800,20,40,60,100,.6f,2};
        var snapshot=SaveTrailer.Capture(shape,new[]{new SaveTrailer.Entry(1,a),new SaveTrailer.Entry(2,b)});
        a[0]=999;b[0]=999;
        Check(snapshot.Entries[0].Values[0]==400 && snapshot.Entries[1].Values[0]==800,"immutable independent capture");
        byte[] native={5,0,1,8,0,0,255,255,255,255,4,0,78,97,109,101};
        var bytes=SaveTrailer.Append(native,snapshot);
        Check(bytes.AsSpan(0,native.Length).SequenceEqual(native),"vanilla bytes/name preserved");
        Check(SaveTrailer.TryRead(bytes,out var decoded,out int originalLength) && originalLength==native.Length,"footer roundtrip");
        Check(SaveTrailer.Matches(decoded,shape),"valid topology accepted");
        Check(decoded!.Entries[0].Values[0]==400 && decoded.Entries[1].Values[0]==800,"same part ID distinct settings");
        decoded.Entries[0].Values[0]=500;
        Check(decoded.Entries[1].Values[0]==800 && snapshot.Entries[0].Values[0]==400,"decoded settings do not alias");
        var rearranged=(SaveTrailer.Node[])shape.Clone();rearranged[2]=new(59,0,2);
        Check(!SaveTrailer.Matches(decoded,rearranged),"child-slot mismatch rejected");
        rearranged=(SaveTrailer.Node[])shape.Clone();rearranged[2]=new(66,0,3);
        Check(!SaveTrailer.Matches(decoded,rearranged),"part-ID mismatch rejected");
        rearranged=(SaveTrailer.Node[])shape.Clone();rearranged[3]=new(66,1,0);
        Check(!SaveTrailer.Matches(decoded,rearranged),"parent topology mismatch rejected");
        Check(!SaveTrailer.Matches(decoded,shape.Take(3).ToArray()),"node count mismatch rejected");
        Check(!SaveTrailer.TryRead(native,out _,out _),"legacy vehicle without trailer");
        var stock=SaveTrailer.Capture(shape,Array.Empty<SaveTrailer.Entry>());
        Check(SaveTrailer.Append(native,stock).SequenceEqual(native),"stock vehicle remains byte-identical");
        for(int i=0;i<bytes.Length;i++) Check(!SaveTrailer.TryRead(bytes.Take(i).ToArray(),out _,out _),"truncated trailer "+i);
        var corrupt=(byte[])bytes.Clone();corrupt[native.Length+5]^=1;
        Check(!SaveTrailer.TryRead(corrupt,out _,out _),"payload corruption checksum");
        corrupt=(byte[])bytes.Clone();corrupt[^1]^=1;
        Check(!SaveTrailer.TryRead(corrupt,out _,out _),"unknown footer");
        foreach(int size in new[]{-1,0,int.MaxValue,SaveTrailer.MaxPayload+1}) {
            corrupt=(byte[])bytes.Clone();BinaryPrimitives.WriteInt32LittleEndian(corrupt.AsSpan(corrupt.Length-20),size);
            Check(!SaveTrailer.TryRead(corrupt,out _,out _),"invalid footer length "+size);
        }
        foreach(var bad in new[]{snapshot with{Version=99},snapshot with{NodeCount=-1},snapshot with{Topology="bad"},snapshot with{Entries=new[]{new SaveTrailer.Entry(999,new float[]{1,1,1,1,1,1,1})}},snapshot with{Entries=new[]{snapshot.Entries[0],snapshot.Entries[0]}},snapshot with{Entries=new[]{new SaveTrailer.Entry(1,new float[]{1,1})}}})
            Check(!SaveTrailer.TryRead(SaveTrailer.Append(native,bad),out _,out _),"malformed authenticated payload rejected");
        foreach(float value in new[]{float.NaN,float.PositiveInfinity,-1f,100001f}) {
            var row=new float[]{value,1,1,1,1,1,1};Check(!SaveTrailer.ValidValues(row),"invalid float rejected");
        }
        Check(!SaveTrailer.ValidValues(new float[]{1,1,1,1,1,0,1}),"zero crossover rejected");
        Check(Reject(()=>SaveTrailer.Capture(shape,new[]{new SaveTrailer.Entry(1,new float[]{1,1,1,1,1,0,1})})),"invalid capture rejected");
        Check(Reject(()=>SaveTrailer.Signature(new[]{new SaveTrailer.Node(1,0,0)})),"cyclic topology rejected");
        Check(Reject(()=>SaveTrailer.Signature(new SaveTrailer.Node[SaveTrailer.MaxNodes+1])),"node bound");
        // Native PNG storage writes three bytes per eligible pixel and later reads
        // every transparent pixel, including untouched and partially filled pixels.
        byte[] CarrierRoundtrip(byte[] input,int sparePixels) {
            var raw=new byte[((input.Length+2)/3+sparePixels)*4];int source=0;
            for(int pixel=0;pixel<raw.Length && source<input.Length;pixel+=4) {
                raw[pixel]=1;for(int channel=1;channel<=3 && source<input.Length;channel++)raw[pixel+channel]=input[source++];
            }
            return Enumerable.Range(0,raw.Length/4).Where(pixel=>raw[pixel*4]<=1).SelectMany(pixel=>new[]{raw[pixel*4+1],raw[pixel*4+2],raw[pixel*4+3]}).ToArray();
        }
        var padded=CarrierRoundtrip(bytes,222020);
        Check(padded.Length>bytes.Length+600000,"PNG transport has substantial padding");
        Check(!SaveTrailer.TryRead(padded,out _,out _),"strict EOF decoder cannot recover PNG carrier");
        Check(SaveTrailer.TryReadPadded(padded,native.Length,out var named,out int namedLength) && namedLength==native.Length && SaveTrailer.Matches(named,shape),"named PNG carrier restored");
        Check(SaveTrailer.TryReadPadded(padded,native.Length+4,out var unnamed,out int unnamedLength) && unnamedLength==native.Length && SaveTrailer.Matches(unnamed,shape),"unnamed four-byte probe restored");
        var stale=bytes.Concat(Enumerable.Repeat((byte)0x91,131071)).Concat(bytes).ToArray();
        Check(SaveTrailer.TryReadPadded(stale,native.Length,out _,out int staleLength) && staleLength==native.Length,"nonzero stale PNG padding ignored");
        Check(!SaveTrailer.TryReadPadded(stale,native.Length+1,out _,out _),"arbitrary payload offset rejected");
        Check(!SaveTrailer.TryReadPadded(stale,-1,out _,out _) && !SaveTrailer.TryReadPadded(stale,int.MaxValue,out _,out _),"invalid native cursor rejected");
        var badPadded=(byte[])padded.Clone();badPadded[native.Length+5]^=1;
        Check(!SaveTrailer.TryReadPadded(badPadded,native.Length,out _,out _),"padded corrupt checksum rejected");
        Check(SaveTrailer.TryReadPadded(bytes,native.Length,out _,out _),"EOF trailer remains compatible");
        Console.WriteLine("Persistence codec: roundtrip, independent per-part values, topology validation, legacy preservation, PNG padding and malformed payload tests passed. Native save/load, copy/paste and undo/redo require in-game validation.");
    }
}
