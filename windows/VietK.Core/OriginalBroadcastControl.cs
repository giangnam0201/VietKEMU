using System.Globalization;
using System.Text.Json;

namespace VietK.Core;

public sealed record OriginalBroadcastSong(int Songid,string Songname,string Singername);
public sealed record OriginalBroadcastUsb(string Id,string Name);
public sealed record OriginalBroadcastState(string PublishMusicMode,IReadOnlyList<OriginalBroadcastSong> PublishMusicOfLocal,IReadOnlyList<OriginalBroadcastUsb> PublishMusicOfUSB);
public sealed record OriginalBroadcastListReply(IReadOnlyList<OriginalBroadcastSong> PublishMusicOfLocal);

// SettingAction commands 102/103. Request metadata is not authoritative: the
// original saved-list reply resolves IDs back through Song.getSongById.
public sealed class OriginalBroadcastControl(OriginalBroadcastPlaylist playlist,Func<int,LocalSong?> lookup,
    Func<int,bool> isLocal,Action<int> order,Action<string> save,
    Func<string,int,IReadOnlyList<LocalSong>> search)
{
    private static OriginalBroadcastSong Info(LocalSong song)=>new(song.Id,song.Name,song.Singer);
    public OriginalBroadcastListReply Saved()=>new(playlist.Entries.Select(entry=>lookup(entry.SongId)).OfType<LocalSong>().Select(Info).ToArray());
    public OriginalBroadcastState State(bool usb)=>usb?new("1",[],[new("-1","")]):
        new("0",playlist.Entries.Select(entry=>entry.SongId).Where(id=>id>0).Distinct().Select(lookup).OfType<LocalSong>().Select(Info).ToArray(),[]);
    public IReadOnlyList<OriginalBroadcastSong> Search(string value,int page)
    {
        if(value.Length>200||page<0||page>int.MaxValue/50)throw new ArgumentException();
        return search(value,page).Select(Info).ToArray();
    }
    public OriginalBroadcastListReply Save(IReadOnlyList<int> ids)
    {
        var json=JsonSerializer.Serialize(new { play_list=ids.Select(id=>new { song_id=id.ToString(CultureInfo.InvariantCulture),type="1" }),is_need_reply="1" });
        foreach(var id in ids)if(!isLocal(id))order(id);
        save(json);return Saved();
    }
    public static IReadOnlyList<int> ParseRequest(JsonElement request)
    {
        if(request.ValueKind!=JsonValueKind.Object)throw new ArgumentException();
        // Gson's getter turns a missing/null list into an empty list.
        if(!request.TryGetProperty("publishMusicOfLocal",out var list)||list.ValueKind==JsonValueKind.Null)return [];
        if(list.ValueKind!=JsonValueKind.Array)throw new ArgumentException();
        var ids=new List<int>();
        foreach(var item in list.EnumerateArray())
        {
            if(item.ValueKind!=JsonValueKind.Object)throw new ArgumentException();
            var id=0;
            if(item.TryGetProperty("songid",out var value)&&(value.ValueKind!=JsonValueKind.Number||!value.TryGetInt32(out id)))throw new ArgumentException();
            foreach(var key in new[]{"songname","singername"})
                if(item.TryGetProperty(key,out var text)&&text.ValueKind is not (JsonValueKind.String or JsonValueKind.Null))throw new ArgumentException();
            ids.Add(id);
        }
        return ids;
    }
}
