using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeBroadcastPlaylistVerification
{
    internal static async Task Run(Window host,string root,string fixtures,string output)
    {
        var directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-idle-list-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(directory);
        try
        {
            var contract=JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root,"bottom.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var fixture=Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"));
            using var playback=new NativePlayback(new BottomBar(root,contract),directory);
            playback.IdleSongExists=id=>id is 1 or 2 or 3;
            playback.ResolveIdleSong=id=>id is 1 or 2?new NativeIdleSong(fixture,new SongMedia(id,id,fixture,100,0,5,"","",0,"","","","",0,null,null,null)):null;
            const string list="{\"play_list\":[{\"song_id\":\"1\",\"type\":\"1\"},{\"song_id\":\"1\",\"type\":\"1\"},{\"song_id\":\"99\",\"type\":\"1\"},{\"song_id\":\"3\",\"type\":\"1\"},{\"song_id\":\"2\",\"type\":\"1\"}]}";
            playback.ImportIdlePlaylist(list);await Ready(playback,1);
            Require(playback.Source==PlaybackSource.Idle&&playback.CurrentFlowId==""&&playback.Decoder.PreserveStereo,"Idle playlist took queue ownership or lost stereo metadata");
            playback.Command("replay_imv");await Ready(playback,1);
            Require(playback.Player.Seek(playback.Player.Duration-350)==0,"End-of-media seek failed");
            await Ready(playback,2);
            Require(playback.StartIdleDemo(),"Playlist wrap rejected");await Ready(playback,1);
            Require(playback.PlayMedia(fixture,preserveStereo:true),"Karaoke interruption failed");
            Require(playback.IdleSongId is null&&playback.Source==PlaybackSource.LocalKaraoke,"Song transition retained idle identity");
            Require(playback.StartIdleDemo(),"Idle resume rejected");await Ready(playback,2);
            playback.SetIdleVideo(fixture);await Ready(playback,null);
            var rejected=false;try { playback.ImportIdlePlaylist("{}"); }catch(ArgumentException) { rejected=true; }
            Require(rejected&&playback.IdleVideoSource==fixture&&playback.IdlePlaylist.Entries.Count==5,"Invalid import changed custom video or list");
            playback.ImportIdlePlaylist(list);await Ready(playback,1);
            File.Copy(fixture,Path.Combine(directory,"Demo.mp4"));Require(playback.StartIdleDemo(),"Legacy Demo.mp4 rejected");await Ready(playback,null);
            Require(playback.IdleVideoSource==Path.Combine(directory,"Demo.mp4"),"Legacy Demo.mp4 lost original priority");
            playback.Player.Stop();File.Delete(Path.Combine(directory,"Demo.mp4"));
            playback.UseFactoryIdleVideo();await Ready(playback,null);
            Require(playback.IdlePlaylist.Entries.Count==0&&playback.IdleVideoSource!=fixture,"Factory reset retained configured songs");
            File.WriteAllText(Path.Combine(output,"broadcast-playlist-verification.json"),JsonSerializer.Serialize(new {
                realDecodedPlaylist=true,completionAdvances=true,replayKeepsCurrentSong=true,duplicateAndUnavailableSkip=true,
                wrapsInOrder=true,songInterruptionPreservesCursor=true,customAndLegacyDemoPriority=true,
                malformedImportPreservesState=true,factoryResetClearsPlaylist=true,originalListEditorPorted=false,cloudPlaylistTested=false
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-idle-list-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected idle playlist fixture path");
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
    private static async Task Ready(NativePlayback playback,int? id)
    {
        var before=playback.DecodedPreviewFrames;var timer=Stopwatch.StartNew();
        while(timer.Elapsed<TimeSpan.FromSeconds(12))
        {
            if(playback.IdleSongId==id&&playback.Player.State==OriginalVideoState.Play&&playback.Player.Position>0&&playback.DecodedPreviewFrames>before)return;
            await Task.Delay(30);
        }
        throw new InvalidDataException($"Idle playlist failed to decode song {id}: current={playback.IdleSongId}, state={playback.Player.State}, source={playback.IdleVideoSource}");
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
