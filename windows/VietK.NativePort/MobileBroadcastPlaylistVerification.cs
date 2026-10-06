using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using VietK.Core;

namespace VietK.NativePort;

internal static class MobileBroadcastPlaylistVerification
{
    internal static LocalSong? Lookup(int id)=>id switch {
        101001=>Song(id,"Playlist fixture A"),101002=>Song(id,"Playlist fixture B"),101003=>Song(id,"<img src=x onerror=alert(1)>"),
        >=101100 and <101160=>Song(id,"Page fixture "+id),_=>null };
    internal static IReadOnlyList<LocalSong> Search(string text,int page)=>Enumerable.Range(101001,3).Concat(Enumerable.Range(101100,60))
        .Select(Lookup).OfType<LocalSong>().Where(song=>song.Name.StartsWith(text,StringComparison.OrdinalIgnoreCase)).Skip(page*50).Take(50).ToArray();
    private static LocalSong Song(int id,string name)=>new(id,name,"",2,"Playlist singer",[0,0,0,0],[0,0,0,0],[8,0,0,0],0,0,0,null,null,1,null,1,0);
    internal static async Task<string> Run(NativePlayback playback,OriginalBroadcastControl control,HttpClient client,string output)
    {
        var original=JsonSerializer.Serialize(new { play_list=playback.IdlePlaylist.Entries.Select(e=>new { song_id=e.SongId.ToString(System.Globalization.CultureInfo.InvariantCulture),type=e.Type.ToString(System.Globalization.CultureInfo.InvariantCulture) }) });
        var source=playback.Source;var path=playback.Player.Source;var flow=playback.CurrentFlowId;var liveVolume=playback.Decoder.OutputVolumeStep;
        using(var unpaired=new HttpClient { BaseAddress=client.BaseAddress })
        {
            foreach(var route in new[]{"api/settings/broadcast-playlist","api/settings/broadcast-playlist/list","api/settings/broadcast-playlist/search?q=Playlist&page=0"})
                Require((await unpaired.GetAsync(route)).StatusCode==HttpStatusCode.Unauthorized,"Unpaired playlist read/search accepted");
            using var write=await unpaired.PostAsJsonAsync("api/settings/broadcast-playlist",new { publishMusicOfLocal=Array.Empty<object>() });Require(write.StatusCode==HttpStatusCode.Unauthorized,"Unpaired playlist write accepted");
        }
        using(var cross=new HttpRequestMessage(HttpMethod.Post,"api/settings/broadcast-playlist"))
        { cross.Headers.Add("Origin","https://example.invalid");cross.Content=JsonContent.Create(new { publishMusicOfLocal=Array.Empty<object>() });Require((await client.SendAsync(cross)).StatusCode==HttpStatusCode.Forbidden,"Cross-origin playlist write accepted"); }
        using(var response=await client.PostAsJsonAsync("api/settings/broadcast-playlist",new { publishMusicOfLocal=new[]{new { songid=101002,songname="Client spoof name",singername="Client spoof singer" },new { songid=101001,songname="",singername="" },new { songid=101002,songname="",singername="" },new { songid=999999,songname="Missing",singername="" }} }))
        {
            Require(response.IsSuccessStatusCode,"Original playlist request rejected");using var reply=JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var songs=reply.RootElement.GetProperty("publishMusicOfLocal").EnumerateArray().ToArray();
            Require(songs.Select(s=>s.GetProperty("songid").GetInt32()).SequenceEqual(new[]{101002,101001,101002})&&songs.All(s=>s.GetProperty("songname").GetString()!.StartsWith("Playlist fixture")),"Saved reply lost duplicates, trusted client metadata or invented an unknown song");
        }
        Require(playback.IdlePlaylist.Entries.Count==4&&control.State(false).PublishMusicOfLocal.Select(s=>s.Songid).SequenceEqual(new[]{101002,101001}),"Runtime generation or raw missing-ID persistence differs");
        using(var state=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-playlist")))Require(state.RootElement.GetProperty("publishMusicMode").GetString()=="0"&&state.RootElement.GetProperty("publishMusicOfLocal").GetArrayLength()==2,"Status did not use the runtime list");
        using(var first=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-playlist/search?q=Page&page=0")))Require(first.RootElement.GetArrayLength()==50,"HTTP search did not expose original 50-result pages");
        using(var second=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-playlist/search?q=Page&page=1")))Require(second.RootElement.GetArrayLength()==10,"HTTP search next-page mapping differs");
        using(var rank=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-playlist/search?q=&page=0")))Require(rank.RootElement.EnumerateArray().Select(s=>s.GetProperty("songid").GetInt32()).SequenceEqual(new[]{101002,101001,101002}),"Blank HTTP search did not use original rank-cache order/duplicates");
        using(var nextRank=JsonDocument.Parse(await client.GetStringAsync("api/settings/broadcast-playlist/search?q=&page=1")))Require(nextRank.RootElement.GetArrayLength()==10,"Rank pagination shifted after filtering missing database rows");
        var saved=string.Join(',',playback.IdlePlaylist.Entries.Select(e=>e.SongId));
        foreach(var json in new[]{"[]","{\"publishMusicOfLocal\":{}}","{\"publishMusicOfLocal\":[null]}","{\"publishMusicOfLocal\":[{\"songid\":\"101001\"}]}","{\"publishMusicOfLocal\":[{\"songid\":101001.5}]}","{\"publishMusicOfLocal\":[{\"songid\":101001,\"singername\":{}}]}"})
        { using var response=await client.PostAsync("api/settings/broadcast-playlist",new StringContent(json,Encoding.UTF8,"application/json"));Require(response.StatusCode==HttpStatusCode.BadRequest&&string.Join(',',playback.IdlePlaylist.Entries.Select(e=>e.SongId))==saved,"Invalid playlist request changed saved configuration"); }
        foreach(var route in new[]{"?q=Page&page=-1","?q=Page&page=x","?q=Page&page=2147483647"})Require((await client.GetAsync("api/settings/broadcast-playlist/search"+route)).StatusCode==HttpStatusCode.BadRequest,"Invalid playlist search admitted");
        var large=new { publishMusicOfLocal=Enumerable.Range(101100,150).Select(id=>new { songid=id,songname=new string('x',100),singername="Library" }).ToArray() };
        using(var response=await client.PostAsJsonAsync("api/settings/broadcast-playlist",large))Require(response.IsSuccessStatusCode&&playback.IdlePlaylist.Entries.Count==150,"Legitimate list above generic 8KB action limit rejected");
        using(var empty=await client.PostAsJsonAsync("api/settings/broadcast-playlist",new { publishMusicOfLocal=Array.Empty<object>() }))Require(empty.IsSuccessStatusCode&&playback.IdlePlaylist.Entries.Count==0,"Explicit empty list did not persist");
        Require(playback.Source==source&&playback.Player.Source==path&&playback.CurrentFlowId==flow&&playback.Decoder.OutputVolumeStep==liveVolume,"Playlist configuration changed live decoder/source/flow/volume");
        using(var seed=await client.PostAsJsonAsync("api/settings/broadcast-playlist",new { publishMusicOfLocal=new[]{new { songid=101001,songname="",singername="" }} }))Require(seed.IsSuccessStatusCode,"Browser playlist seed failed");
        File.WriteAllText(Path.Combine(output,"broadcast-playlist-http-verification.json"),JsonSerializer.Serialize(new {
            originalSavedAndRuntimeFields=true,duplicateAndMissingIdSemantics=true,databaseMetadataAuthoritative=true,
            searchPagination=true,blankRankCacheOrderAndDuplicates=true,invalidRequestsPreserveConfig=true,pairedAuthorizationAndOrigin=true,largeListAccepted=true,
            explicitEmptyListPersists=true,liveDecoderSourceFlowAndGainUnchanged=true
        },new JsonSerializerOptions { WriteIndented=true }));return original;
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
