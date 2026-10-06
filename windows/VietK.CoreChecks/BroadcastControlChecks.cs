using System.Text.Json;
using VietK.Core;

internal static class BroadcastControlChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"vietk-broadcast-control-"+Guid.NewGuid().ToString("N"));
        try
        {
            var list=new OriginalBroadcastPlaylist(folder);var ordered=new List<int>();
            LocalSong? Lookup(int id)=>id is 1 or 2?new(id,"Database song "+id,"",2,"Database singer",[0,0,0,0],[0,0,0,0],[8,0,0,0],0,0,0,null,null,1,null,1,0):null;
            var control=new OriginalBroadcastControl(list,Lookup,id=>id==1,ordered.Add,list.Import,(text,page)=>page==0?[Lookup(1)!]:[]);
            using var request=JsonDocument.Parse("""{"publishMusicOfLocal":[{"songid":2,"songname":"client name","singername":"client singer"},{"songid":1},{"songid":2},{"songid":99},{"songid":-1},{}]}""");
            var reply=control.Save(OriginalBroadcastControl.ParseRequest(request.RootElement));
            Require(list.Entries.Select(e=>e.SongId).SequenceEqual(new[]{2,1,2,99,-1,0}),"Original raw entries changed");
            Require(reply.PublishMusicOfLocal.Select(s=>s.Songid).SequenceEqual(new[]{2,1,2})&&reply.PublishMusicOfLocal.All(s=>s.Songname.StartsWith("Database song")),"Saved reply deduplicated, invented missing songs or trusted client metadata");
            Require(control.State(false).PublishMusicOfLocal.Select(s=>s.Songid).SequenceEqual(new[]{2,1})&&control.State(false).PublishMusicMode=="0","Runtime status did not use generated unique positive IDs");
            var usb=control.State(true);Require(usb.PublishMusicMode=="1"&&usb.PublishMusicOfLocal.Count==0&&usb.PublishMusicOfUSB.Single()==new OriginalBroadcastUsb("-1",""),"Original USB path-only item fields differ");
            Require(ordered.SequenceEqual(new[]{2,2,99,-1,0}),"Original nonlocal order side effects changed");
            Require(control.Search("A",0).Single().Songid==1&&control.Search("A",1).Count==0,"Search page mapping differs");
            foreach(var bad in new[]{"[]","{\"publishMusicOfLocal\":{}}","{\"publishMusicOfLocal\":[null]}","{\"publishMusicOfLocal\":[{\"songid\":\"1\"}]}","{\"publishMusicOfLocal\":[{\"songid\":1.5}]}","{\"publishMusicOfLocal\":[{\"songid\":1,\"songname\":{}}]}"})
            { var rejected=false;try { using var json=JsonDocument.Parse(bad);OriginalBroadcastControl.ParseRequest(json.RootElement); }catch(ArgumentException) { rejected=true; }Require(rejected&&list.Entries.Count==6,"Invalid body mutated the saved list"); }
            using var empty=JsonDocument.Parse("{}");Require(OriginalBroadcastControl.ParseRequest(empty.RootElement).Count==0,"Original missing-list getter did not return empty");
            control.Save([]);Require(control.State(false).PublishMusicOfLocal.Count==0&&new OriginalBroadcastPlaylist(folder).Entries.Count==0,"Empty list did not persist");
            Console.WriteLine("Original phone broadcast commands: raw/saved/runtime list distinctions, authoritative database names, duplicate side effects, USB path fields and request validation verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-broadcast-control-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected broadcast control fixture cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
