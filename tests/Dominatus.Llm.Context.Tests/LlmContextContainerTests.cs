using System.Text.Json.Nodes;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Dominatus.Llm.Context;

namespace Dominatus.Llm.Context.Tests;

public class LlmContextContainerTests
{
    private static readonly DateTimeOffset Now = new(2026, 01, 01, 0, 0, 0, TimeSpan.Zero);
    [Fact] public void ContextContainer_WriteRead_RoundTripsStore(){var s=NewStore();s.Upsert(Chunk("1"));Assert.NotNull(LlmContextContainer.ReadStore(LlmContextContainer.WriteToBytes(s)).Find("1"));}
    [Fact] public void ContextContainer_SaveLoad_RoundTripsStore(){var s=NewStore();var p=Path.GetTempFileName();LlmContextContainer.Save(p,s);Assert.Equal(s.Id,LlmContextContainer.Load(p).Id);}    
    [Fact] public void ContextContainer_ReadManifest_ReturnsStoreChunk(){var m=LlmContextContainer.ReadManifest(LlmContextContainer.WriteToBytes(NewStore()));Assert.Equal(LlmContextContainer.DefaultStoreChunkId,Assert.Single(m.Chunks).Id);}    
    [Fact] public void ContextContainer_StoreChunkPayloadMatchesJsonCodec(){var s=NewStore();var b=LlmContextContainer.WriteToBytes(s);var c=Assert.Single(LlmContextContainer.ReadManifest(b).Chunks);Assert.Equal(LlmContextStoreJson.Serialize(s),Encoding.UTF8.GetString(b.AsSpan((int)c.Offset,(int)c.Length)));}
    [Fact] public void ContextContainer_WriteToBytes_IsDeterministicForFixedStore(){var s=NewStore();Assert.Equal(LlmContextContainer.WriteToBytes(s),LlmContextContainer.WriteToBytes(s));}
    [Fact] public void ContextContainer_WritesExpectedMagic(){Assert.Equal("DCTX",Encoding.ASCII.GetString(LlmContextContainer.WriteToBytes(NewStore()),0,4));}
    [Fact] public void ContextContainer_RejectsTooShortFile()=>Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore([1,2,3]));
    [Fact] public void ContextContainer_RejectsBadMagic(){var b=LlmContextContainer.WriteToBytes(NewStore());b[0]=(byte)'X';Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsUnsupportedVersion(){var b=LlmContextContainer.WriteToBytes(NewStore());BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(4,4),99);Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsMissingStoreChunk(){var b=MutateDirectory(LlmContextContainer.WriteToBytes(NewStore()),d=>d[0]["id"]="missing");Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsDuplicateChunkIds(){var b=MutateDirectory(LlmContextContainer.WriteToBytes(NewStore()),d=>d.Add(new Dictionary<string,object?>(d[0])));Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsChunkOutsideFileBounds(){var b=MutateDirectory(LlmContextContainer.WriteToBytes(NewStore()),d=>d[0]["length"]=99999999L);Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsMalformedDirectoryJson(){var b=LlmContextContainer.WriteToBytes(NewStore());b[31]=(byte)'{';Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsUnsupportedStoreChunkFormat(){var b=MutateDirectory(LlmContextContainer.WriteToBytes(NewStore()),d=>d[0]["format"]="application/json");Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RejectsMalformedStorePayload(){var b=LlmContextContainer.WriteToBytes(NewStore());b[^1]=0xFF;Assert.Throws<InvalidDataException>(()=>LlmContextContainer.ReadStore(b));}
    [Fact] public void ContextContainer_RoundTripsStoreWithLoadouts(){var s=NewStore();s.UpsertLoadout(new LlmContextLoadout{Id="planner",Title="Planner"});Assert.NotNull(LlmContextContainer.ReadStore(LlmContextContainer.WriteToBytes(s)).FindLoadout("planner"));}
    [Fact] public void ContextContainer_ReadStorePayload_UsesExistingJsonCompatibility(){var s=NewStore();var b=LlmContextContainer.WriteToBytes(s);var c=Assert.Single(LlmContextContainer.ReadManifest(b).Chunks);Assert.Equal(s.Id,LlmContextStoreJson.Deserialize(Encoding.UTF8.GetString(b.AsSpan((int)c.Offset,(int)c.Length))).Id);}    

    private static byte[] MutateDirectory(byte[] bytes, Action<List<Dictionary<string, object?>>> mutate)
    {
        var offset=(int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(16,8));
        var length=(int)BinaryPrimitives.ReadInt64LittleEndian(bytes.AsSpan(24,8));
        var node=JsonNode.Parse(bytes.AsSpan(offset,length))!.AsObject();
        var chunks=node["chunks"]!.AsArray();
        var list=chunks.Select(x=>x!.Deserialize<Dictionary<string,object?>>()!).ToList();
        mutate(list);
        var dir=JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object?> { ["chunks"] = list });
        var outBytes=new byte[32+dir.Length+(bytes.Length-(offset+length))];
        Array.Copy(bytes,0,outBytes,0,32);
        BinaryPrimitives.WriteInt64LittleEndian(outBytes.AsSpan(16,8),32);
        BinaryPrimitives.WriteInt64LittleEndian(outBytes.AsSpan(24,8),dir.Length);
        Array.Copy(dir,0,outBytes,32,dir.Length);
        Array.Copy(bytes,offset+length,outBytes,32+dir.Length,bytes.Length-(offset+length));
        return outBytes;
    }

    private static LlmContextStore NewStore()=>new("PROJECT.dominatus","Dominatus Project Context",Now);
    private static LlmContextChunk Chunk(string id)=>new(){Id=id,Kind="doctrine",Title="t",Content="c",CreatedUtc=Now,UpdatedUtc=Now};
}
