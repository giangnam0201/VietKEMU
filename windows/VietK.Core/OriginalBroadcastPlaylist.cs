using System.Globalization;
using System.Text.Json;

namespace VietK.Core;

public sealed record OriginalBroadcastEntry(int SongId,int Type);

// BroadcastListManager: raw config preserves duplicates; generated playback
// items are unique positive song IDs. Rebuilding does not reset playSequence.
public sealed class OriginalBroadcastPlaylist
{
    private readonly string file;
    private int sequence;
    public IReadOnlyList<OriginalBroadcastEntry> Entries { get; private set; }=[];
    public OriginalBroadcastPlaylist(string directory)
    {
        file=Path.Combine(directory,"localbroadcastlist.init");
        if(File.Exists(file))try { Entries=Parse(File.ReadAllText(file)); }catch(Exception error) when(error is JsonException or ArgumentException) { }
    }
    public static IReadOnlyList<OriginalBroadcastEntry> Parse(string json)
    {
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind!=JsonValueKind.Object||!doc.RootElement.TryGetProperty("play_list",out var list)||list.ValueKind!=JsonValueKind.Array)throw new ArgumentException("Danh sách VietK không hợp lệ.");
        var entries=new List<OriginalBroadcastEntry>();
        foreach(var entry in list.EnumerateArray())
        {
            if(entry.ValueKind!=JsonValueKind.Object||!entry.TryGetProperty("song_id",out var id)||id.ValueKind!=JsonValueKind.String||
                !entry.TryGetProperty("type",out var type)||type.ValueKind!=JsonValueKind.String||
                !int.TryParse(id.GetString(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var songId)||
                !int.TryParse(type.GetString(),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var kind))throw new ArgumentException("Mã bài hát VietK không hợp lệ.");
            entries.Add(new(songId,kind));
        }
        return entries.AsReadOnly();
    }
    public void Import(string json)
    {
        var entries=Parse(json);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,json);File.Move(temporary,file,true);Entries=entries; }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public T? Next<T>(Func<int,T?> resolve,Func<int,bool>? songExists=null) where T:class
    {
        var ids=Entries.Select(e=>e.SongId).Where(id=>id>0&&(songExists?.Invoke(id)??true)).Distinct().ToArray();
        if(ids.Length==0)return null;
        // Original getRandomCachedSong tries size+1 entries before falling back.
        for(var attempt=0;attempt<=ids.Length;attempt++)
        {
            if(sequence>=ids.Length)sequence=0;
            var item=resolve(ids[sequence++]);if(item is not null)return item;
        }
        return null;
    }
}
