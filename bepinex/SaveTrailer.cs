using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
namespace MarsSpringTuner;

// Pure managed wire codec. Vanilla bytes are an untouched prefix.
public static class SaveTrailer
{
    public const int MaxNodes=16384, MaxPayload=2*1024*1024;
    static readonly byte[] Magic=Encoding.ASCII.GetBytes("MFLSPRINGTUNE001");
    public sealed record Node(int PartId,int Parent,int Slot);
    public sealed record Entry(int Index,float[] Values);
    public sealed record Snapshot(int Version,string Topology,int NodeCount,Entry[] Entries);
    static readonly System.Text.Json.JsonSerializerOptions Json=new(){MaxDepth=16};
    public static string Signature(Node[] nodes) {
        if(nodes.Length>MaxNodes) throw new FormatException("Too many vehicle nodes");
        byte[] bytes=new byte[nodes.Length*12];
        for(int i=0;i<nodes.Length;i++) {
            var n=nodes[i];
            if(n.Parent>=i || n.Parent< -1 || (i==0?n.Parent!=-1:n.Parent<0) || n.Slot<0) throw new FormatException("Invalid topology");
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*12),n.PartId);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*12+4),n.Parent);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(i*12+8),n.Slot);
        }
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
    public static bool ValidValues(float[]? values) {
        if(values==null || values.Length!=7)return false;
        for(int i=0;i<7;i++) if(!float.IsFinite(values[i]) || values[i]<(i>=5?.001f:0) || values[i]>(i==0?100000:i>=5?100:20000))return false;
        return true;
    }
    public static Snapshot Capture(Node[] nodes,IEnumerable<Entry> entries) {
        var result=new Snapshot(1,Signature(nodes),nodes.Length,entries.Select(e=>new Entry(e.Index,(float[])e.Values.Clone())).ToArray());
        if(!Matches(result,nodes))throw new FormatException("Invalid tuning snapshot");
        return result;
    }
    public static bool Matches(Snapshot? snapshot,Node[] nodes) {
        if(snapshot==null || snapshot.Version!=1 || snapshot.NodeCount!=nodes.Length || snapshot.Entries==null || snapshot.Entries.Length>nodes.Length || snapshot.Topology!=Signature(nodes))return false;
        var seen=new HashSet<int>();
        foreach(var e in snapshot.Entries) if(e==null || e.Index<0 || e.Index>=nodes.Length || !seen.Add(e.Index) || !ValidValues(e.Values))return false;
        return true;
    }
    public static byte[] Append(byte[] original,Snapshot snapshot) {
        if(snapshot.Entries.Length==0)return (byte[])original.Clone();
        byte[] payload=System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(snapshot,Json);
        if(payload.Length>MaxPayload)throw new FormatException("Tuning payload too large");
        byte[] output=new byte[checked(original.Length+payload.Length+52)];
        original.CopyTo(output,0);payload.CopyTo(output,original.Length);
        SHA256.HashData(payload).CopyTo(output,original.Length+payload.Length);
        BinaryPrimitives.WriteInt32LittleEndian(output.AsSpan(output.Length-20),payload.Length);
        Magic.CopyTo(output,output.Length-16);return output;
    }
    // The PNG carrier reconstructs extra bytes after the actual vehicle payload.
    // Search only from the native parser's endpoint, never from arbitrary file offsets.
    public static bool TryReadPadded(byte[] bytes,int nativePosition,out Snapshot? snapshot,out int nativeLength) {
        snapshot=null;nativeLength=bytes.Length;
        if(nativePosition<0 || nativePosition>bytes.Length)return false;
        foreach(int start in new[]{nativePosition,nativePosition-4}) {
            if(start<0)continue;
            int limit=(int)Math.Min(bytes.Length,(long)start+MaxPayload+52), cursor=start+37, candidates=0;
            while(cursor<=limit-16) {
                int found=bytes.AsSpan(cursor,limit-cursor).IndexOf(Magic);if(found<0)break;
                int marker=cursor+found;cursor=marker+16;
                int count=BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(marker-4));
                if(count<=0 || count!=marker-start-36 || count>MaxPayload)continue;
                if(++candidates>32)break;
                if(TryReadExact(bytes.AsSpan(0,marker+16),out var parsed,out int length) && length==start) {
                    snapshot=parsed;nativeLength=length;return true;
                }
            }
        }
        return false;
    }
    public static bool TryRead(byte[] bytes,out Snapshot? snapshot,out int nativeLength)=>TryReadExact(bytes,out snapshot,out nativeLength);
    static bool TryReadExact(ReadOnlySpan<byte> bytes,out Snapshot? snapshot,out int nativeLength) {
        snapshot=null;nativeLength=bytes.Length;
        if(bytes.Length<52 || !bytes.Slice(bytes.Length-16).SequenceEqual(Magic))return false;
        int count=BinaryPrimitives.ReadInt32LittleEndian(bytes.Slice(bytes.Length-20));
        if(count<=0 || count>MaxPayload || count>bytes.Length-52)return false;
        int start=bytes.Length-52-count;var payload=bytes.Slice(start,count);
        if(!SHA256.HashData(payload).AsSpan().SequenceEqual(bytes.Slice(start+count,32)))return false;
        try {var parsed=System.Text.Json.JsonSerializer.Deserialize<Snapshot>(payload,Json);
            if(parsed==null || parsed.Version!=1 || parsed.NodeCount<1 || parsed.NodeCount>MaxNodes || parsed.Entries==null || parsed.Entries.Length>parsed.NodeCount || parsed.Topology==null || parsed.Topology.Length!=64)return false;
            var seen=new HashSet<int>();foreach(var e in parsed.Entries)if(e==null || e.Index<0 || e.Index>=parsed.NodeCount || !seen.Add(e.Index) || !ValidValues(e.Values))return false;
            snapshot=parsed;nativeLength=start;return true;
        } catch(System.Text.Json.JsonException){return false;} catch(NotSupportedException){return false;}
    }
}
