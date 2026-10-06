using System.Text.Json;
using VietK.Core;

internal static class BroadcastPlaylistChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"vietk-broadcast-list-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var playlist=new OriginalBroadcastPlaylist(folder);
            playlist.Import(List(1,1,99,2,3,0,-1));
            Require(playlist.Entries.Count==7,"Raw original entries were lost");
            string? Resolve(int id)=>id is 1 or 3?"song"+id:null;
            bool Exists(int id)=>id!=99;
            Require(playlist.Next(Resolve,Exists)=="song1"&&playlist.Next(Resolve,Exists)=="song3"&&playlist.Next(Resolve,Exists)=="song1","Order, deduplication, unavailable skip or wrap differs");
            playlist.Import(List(3,1));
            Require(playlist.Next(Resolve,Exists)=="song1","Import reset the original cursor");
            var restored=new OriginalBroadcastPlaylist(folder);
            Require(restored.Next(Resolve)=="song3","Restart did not reload config with a fresh cursor");
            foreach(var bad in new[]{"{}","{\"play_list\":[{\"song_id\":1,\"type\":\"1\"}]}","{\"play_list\":[{\"song_id\":\" 1\",\"type\":\"1\"}]}","{"})
            {
                var rejected=false;try { playlist.Import(bad); }catch(Exception error) when(error is ArgumentException or JsonException) { rejected=true; }
                Require(rejected&&File.ReadAllText(Path.Combine(folder,"localbroadcastlist.init"))==List(3,1),"Malformed import changed saved config");
            }
            var attempts=new List<int>();playlist.Import(List(99,1,2));
            playlist.Next<string>(id=> { attempts.Add(id);return null; },id=>id!=99);
            Require(attempts.Count==3&&!attempts.Contains(99),"Missing database songs or size+1 retry limit differs");
            playlist.Import(List());Require(playlist.Next(Resolve) is null,"Empty list selected a song");
            Console.WriteLine("Original idle playlist: persisted string fields, ordering, duplicate and missing-song filtering, cursor retention, wrap and bounded unavailable retries verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-broadcast-list-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected playlist fixture path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static string List(params int[] ids)=>JsonSerializer.Serialize(new { play_list=ids.Select(id=>new { song_id=id.ToString(System.Globalization.CultureInfo.InvariantCulture),type="1" }) });
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
