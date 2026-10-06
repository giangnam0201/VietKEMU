using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using VietK.Core;
using Microsoft.Data.Sqlite;

namespace VietK.NativePort;

internal static class MobileRemoteVerification
{
    public static async Task Run(NativePlayback playback,BottomBar bottom,Canvas panel,string root,string fixtures,string output,Func<int,int,string,Task> checkTone)
    {
        var directory=Path.Combine(output,"mobile-remote");Directory.CreateDirectory(directory);
        void Checkpoint(string message)=>File.AppendAllText(Path.Combine(output,"mobile-checkpoint.txt"),message+Environment.NewLine);
        Checkpoint("Starting native mobile test");
        using var music=new YouTubeMusicScreen(root,directory,playback,bottom);
        var stereo=Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"));
        var metadata=new SongMedia(1,101000,stereo,100,0,1,"0","0",1,"","","","",0,null,null,"remote-vocal-fixture");
        playback.Player.SetSingMode(OriginalSingMode.Original);
        RequireMedia(playback.PlayMedia(stereo,metadata));
        var deadline=DateTime.UtcNow.AddSeconds(15);
        while(playback.Player.State!=OriginalVideoState.Play)
        { if(DateTime.UtcNow>=deadline)throw new TimeoutException("Remote vocal fixture did not start");await Task.Delay(50); }
        music.SeedRemoteFixture();
        var before=panel.Children.Count;
        bottom.CommandRequested+=playback.Command;
        try
        {
            bottom.RequestButton("order_bg");
            if(panel.Children.Count!=before+1)throw new InvalidDataException("Actual Đã chọn/order_bg button did not open selected queue");
            panel.Children.RemoveAt(panel.Children.Count-1);
        }
        finally { bottom.CommandRequested-=playback.Command; }
        Checkpoint("Original selected-queue button opened successfully");
        using var server=new MobileRemoteServer(Dispatcher.CurrentDispatcher,music,playback,0,true);
        Checkpoint("Native HTTP server constructed");
        server.SearchFixture=(query,ct)=>Task.FromResult<IReadOnlyList<YouTubeVideo>>([new("fixture0003","Remote search result","","")]);
        await server.StartAsync(false);
        Checkpoint("Native HTTP server listening");
        using var client=new HttpClient { BaseAddress=new Uri($"http://127.0.0.1:{server.Port}/"),Timeout=TimeSpan.FromSeconds(10) };
        void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
        Require((await client.GetAsync("api/state")).StatusCode==HttpStatusCode.Unauthorized,"Unpaired phone read queue");
        Require((await client.GetStringAsync("/")).Contains("sessionStorage.setItem"),"Mobile browser page missing");
        Checkpoint("Unauthorized request and mobile page checked");
        client.DefaultRequestHeaders.Authorization=new("Bearer",server.TestToken);
        using(var cross=new HttpRequestMessage(HttpMethod.Post,"api/action"))
        {
            cross.Headers.Add("Origin","https://example.invalid");cross.Content=JsonContent.Create(new { action="clear" });
            Require((await client.SendAsync(cross)).StatusCode==HttpStatusCode.Forbidden,"Cross-origin phone action was accepted");
        }
        async Task Send(object action) { using var response=await client.PostAsJsonAsync("api/action",action);Require(response.IsSuccessStatusCode,"Remote action failed: "+response.StatusCode); }
        async Task<string[]> Queue()
        {
            using var state=JsonDocument.Parse(await client.GetStringAsync("api/state"));
            return state.RootElement.GetProperty("queue").EnumerateArray().Select(v=>v.GetProperty("id").GetString()!).ToArray();
        }
        var results=await client.GetFromJsonAsync<YouTubeVideo[]>("api/search?q=fixture");
        Require(results is { Length:1 } && results[0].Id=="fixture0003","Remote search result lost");
        Checkpoint("Authenticated native search checked");
        if(Environment.GetEnvironmentVariable("VIETK_MOBILE_BROWSER_CHECK")=="1")
        {
            var start=new System.Diagnostics.ProcessStartInfo("python") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            start.ArgumentList.Add("tools/verify_mobile_browser.py");
            start.Environment["VIETK_REMOTE_TEST_URL"]=client.BaseAddress!.ToString();
            start.Environment["VIETK_REMOTE_TEST_TOKEN"]=server.TestToken;
            start.Environment["VIETK_REMOTE_TEST_OUTPUT"]=Path.GetFullPath(directory);
            using var browser=System.Diagnostics.Process.Start(start)!;
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
            catch(TimeoutException) { browser.Kill(true);throw new InvalidDataException("Phone browser verification exceeded 60 seconds"); }
            // Failed navigation diagnostics may include the secret fragment: redact before reporting.
            var browserOutput=await stdout;var browserError=await stderr;
            Require(browser.ExitCode==0,"Phone browser verification failed: "+(browserOutput+browserError).Replace(server.TestToken,"[redacted]",StringComparison.Ordinal));
            Checkpoint("Phone browser commands and responsive layout passed");
        }
        await Send(new { action="add",id="fixture0003" });Require((await Queue()).Length==3,"Phone add did not reach actual panel queue");
        await Send(new { action="top",id="fixture0003" });Require((await Queue())[1]=="fixture0003","Phone priority failed");
        await Send(new { action="move",id="fixture0003",target=2 });Require((await Queue())[2]=="fixture0003","Phone reorder failed");
        await Send(new { action="remove",id="fixture0003" });Require((await Queue()).Length==2,"Phone removal failed");
        await Send(new { action="shuffle" });Require((await Queue())[0]=="fixture0001","Phone shuffle changed the playing head");
        await Send(new { action="clear" });Require((await Queue()).SequenceEqual(new[]{"fixture0001"}),"Phone clear interrupted current song");
        var volume=playback.Decoder.OutputVolumeStep;
        await Send(new { action="command",id="mute" });Require(playback.Decoder.Muted,"Phone mute did not reach native decoder");
        await Send(new { action="command",id="voldec" });Require(playback.Decoder.OutputVolumeStep==Math.Max(0,volume-1)&&playback.Television.Overlay.LastControl=="play_ctrl_audio_bg","Phone volume/TV feedback failed");
        Require(!playback.Decoder.Muted,"Phone volume control failed to clear mute");
        await Send(new { action="command",id="volinc" });
        // Exercise the APK's phone play-control value 4 through the real HTTP
        // route, with the YouTube screen installed as the command override.
        playback.Player.Seek(0);playback.Player.SetSingMode(OriginalSingMode.Original);
        await checkTone(880,440,"Remote original vocal baseline missing");
        await Send(new { action="command",id="ori_imv" });
        await checkTone(440,880,"Phone accompaniment did not switch actual PCM");
        Require(!bottom.OriginalVocal&&playback.Television.Overlay.LastControl=="accompany","Phone accompaniment panel/TV feedback missing");
        await Send(new { action="command",id="accp_imv" });
        await checkTone(880,440,"Phone original vocal did not restore actual PCM");
        Require(bottom.OriginalVocal&&playback.Television.Overlay.LastControl=="original","Phone original vocal panel/TV feedback missing");
        await Send(new { action="screen" });Require(playback.Television.IsScreenMasked,"Phone blackout did not reach TV");
        await Send(new { action="screen" });Require(!playback.Television.IsScreenMasked,"Phone did not restore TV picture");
        await Send(new { action="command",id="pause_imv" });
        Require(playback.Player.State==OriginalVideoState.Pause,"Phone did not pause the native player");
        await Send(new { action="command",id="play_imv" });
        Require(playback.Player.State==OriginalVideoState.Play,"Phone did not resume native playback");
        await Send(new { action="command",id="replay_imv" });Require(playback.Television.Overlay.LastControl=="replay","Phone replay TV feedback missing");
        await Send(new { action="command",id="cut_song_imv" });Require((await Queue()).Length==0&&playback.IsPlayingIdle,"Phone next did not clear last song and restore idle video");
        using(var idleState=JsonDocument.Parse(await client.GetStringAsync("api/state")))
            Require(!idleState.RootElement.GetProperty("canSwitchVocal").GetBoolean(),"Idle phone state enabled unavailable vocals");
        var idleMode=playback.Player.SingMode;
        await Send(new { action="command",id="ori_imv" });
        Require(playback.Player.SingMode==idleMode,"Phone changed vocal mode during idle playback");
        await VerifyOriginalQueue(playback,music,server,client,stereo,directory,panel,bottom);
        using(var invalid=await client.PostAsJsonAsync("api/action",new { action="command",id="shutdown" }))Require(invalid.StatusCode==HttpStatusCode.BadRequest,"Unknown phone command accepted");
        using(var unknown=await client.PostAsJsonAsync("api/action",new { action="add",id="unsearched1" }))Require(unknown.StatusCode==HttpStatusCode.BadRequest,"Unsearched arbitrary media accepted");
        server.RePair();Require((await client.GetAsync("api/state")).StatusCode==HttpStatusCode.Unauthorized,"Old pairing secret remained active");
        Checkpoint("Native commands and pairing revocation passed");
        // RePair uses live adapters only to make a local QR; remove it before other tests/captures.
        playback.Television.Overlay.Qr.Configure(new());
        File.WriteAllText(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new { realHttp=true,pairedAuthorization=true,originRejection=true,searchFixture=true,
            nativeQueueAdd=true,priority=true,reorder=true,remove=true,clearProtectsPlaying=true,volumeAndTvFeedback=true,revocation=true,
            pauseResume=true,replay=true,nextRestoresIdle=true,blackout=true,muteAndVolumeUnmute=true,vocalActualPcmAndTvFeedback=true,
            originalQueueStableDuplicateIds=true,originalPriorityMoveDelete=true,originalClearAndShufflePreserveHead=true,
            localNextLeavesYouTubeQueueIntact=true,originalDownloadProgressAndCancel=true,
            phoneSizedBrowserTested=Environment.GetEnvironmentVariable("VIETK_MOBILE_BROWSER_CHECK")=="1",
            physicalPhoneWifiTested=false,manufacturerCloudCompatibility=false }));
    }
    private static async Task VerifyOriginalQueue(NativePlayback playback,YouTubeMusicScreen music,MobileRemoteServer server,HttpClient client,string stereo,string directory,Canvas panel,BottomBar bottom)
    {
        void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
        using var database=new SqliteConnection("Data Source=:memory:");database.Open();
        using(var schema=database.CreateCommand())
        { schema.CommandText="CREATE TABLE tblSelectedList(id INTEGER NOT NULL PRIMARY KEY,songid INT,canscore INT,sequence INT,customerId TEXT,tableid INT,stage INT)";schema.ExecuteNonQuery(); }
        var store=new SelectedListStore(database);OriginalQueueRemote? remote=null;OriginalSelectedQueue? selected=null;
        LocalSong Song(int id)=>new(id,"Original fixture "+(id==1?"A":"B"),"",0,"",new int[4],new int[4],new int[4],0,1,0,"","",0,"",1,0);
        SelectedPlaylistItem Item(int id)=>new(Song(id),0,null,new SongMedia(id,id,stereo,100,0,1,"0","0",1,"","","","",0,null,null,"original-phone-fixture"))
            { PlayName=Song(id).Name,PlayId=id.ToString(),PlayUrl=stereo };
        void Start()
        {
            var head=selected!.Snapshot().FirstOrDefault();
            if(head is null)playback.StartIdleDemo();else RequireMedia(playback.PlayMedia(head.PlayUrl,head.VideoMedia));
        }
        selected=new OriginalSelectedQueue(Song,command=>command.Apply(store,_=>false),()=>remote?.Refresh(),Start);
        selected.Initialize(store,()=>[]);
        var cancelled=0;var retried=0;
        var downloads=new OriginalDownloadQueue(_=>{},()=>remote?.Refresh(),()=>{},()=>cancelled++,_=>{},_=>{});
        downloads.Initialize(()=>{},()=>[],true);
        remote=new OriginalQueueRemote(selected,downloads,playback,bottom,()=>retried++);music.OriginalQueue=remote;
        void Next()=>selected.DeleteByIndex(0);
        playback.NextRequested+=Next;playback.Player.Played+=remote.Refresh;
        try
        {
            music.SeedRemoteFixture();selected.Add(Item(1));selected.Add(Item(1));selected.Add(Item(2));downloads.Add(Item(2));
            downloads.SetProgressBySong(2,100,45);
            var deadline=DateTime.UtcNow.AddSeconds(10);
            while(playback.Player.State!=OriginalVideoState.Play) { if(DateTime.UtcNow>deadline)throw new TimeoutException("Original phone queue did not start");await Task.Delay(50); }
            remote.Refresh();
            Require(bottom.QueueCount==4,"Original selected/download badge differs");
            var local=selected.Snapshot();var first="local:"+local[0].FlowId;var repeated="local:"+local[1].FlowId;var third="local:"+local[2].FlowId;
            Require(first!=repeated,"Repeated original song orders lost their distinct flow IDs");
            using(var state=JsonDocument.Parse(await client.GetStringAsync("api/state")))
            {
                var original=state.RootElement.GetProperty("original");
                Require(original.GetProperty("active").GetBoolean()&&original.GetProperty("queue").GetArrayLength()==4,"Original phone state does not show actual lists");
                Require(original.GetProperty("transfers").GetProperty("download:"+downloads.Snapshot()[0].FlowId).GetProperty("received").GetInt64()==45,"Original download percentage missing");
            }
            var before=panel.Children.Count;playback.Command("order_bg");
            Require(panel.Children.Count==before+1,"Original Đã chọn did not open actual local queue");panel.Children.RemoveAt(panel.Children.Count-1);
            if(Environment.GetEnvironmentVariable("VIETK_MOBILE_BROWSER_CHECK")=="1")
            {
                var start=new System.Diagnostics.ProcessStartInfo("python") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
                start.ArgumentList.Add("tools/verify_mobile_browser.py");
                start.Environment["VIETK_REMOTE_TEST_URL"]=client.BaseAddress!.ToString();start.Environment["VIETK_REMOTE_TEST_TOKEN"]=server.TestToken;
                start.Environment["VIETK_REMOTE_TEST_OUTPUT"]=Path.GetFullPath(directory);start.Environment["VIETK_REMOTE_TEST_PHASE"]="original";
                using var browser=System.Diagnostics.Process.Start(start)!;var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
                try { await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60)); }
                catch(TimeoutException) { browser.Kill(true);throw new InvalidDataException("Original queue browser verification timed out"); }
                Require(browser.ExitCode==0,"Original phone browser failed: "+((await stdout)+(await stderr)).Replace(server.TestToken,"[redacted]",StringComparison.Ordinal));
            }
            async Task Send(string action,string id="",int target=0)
            { using var response=await client.PostAsJsonAsync("api/action",new { action,id,target,bank="original" });Require(response.IsSuccessStatusCode,"Original phone action failed: "+response.StatusCode); }
            await Send("top",repeated);Require(selected.Snapshot()[1].FlowId==repeated[6..],"Original duplicate-row priority failed");
            await Send("move",repeated,2);Require(selected.Snapshot()[2].FlowId==repeated[6..],"Original duplicate-row move failed");
            await Send("remove",repeated);Require(selected.Count==2&&selected.Snapshot()[0].FlowId==first[6..],"Original repeated-row removal affected playing copy");
            await Send("shuffle");Require(selected.Snapshot()[0].FlowId==first[6..],"Original shuffle replaced the playing head");
            using(var response=await client.PostAsJsonAsync("api/action",new { action="command",id="cut_song_imv" }))Require(response.IsSuccessStatusCode,"Original next command failed");
            Require(selected.Count==1&&selected.Snapshot()[0].FlowId==third[6..],"Next advanced YouTube instead of original karaoke");
            using(var state=JsonDocument.Parse(await client.GetStringAsync("api/state")))Require(state.RootElement.GetProperty("queue").GetArrayLength()==2,"Local next modified saved YouTube orders");
            deadline=DateTime.UtcNow.AddSeconds(10);
            while(playback.Player.State!=OriginalVideoState.Play) { if(DateTime.UtcNow>deadline)throw new TimeoutException("Next original song failed to start");await Task.Delay(50); }
            using(var response=await client.PostAsJsonAsync("api/action",new { action="remove",id="fixture0001",bank="youtube" }))Require(response.IsSuccessStatusCode,"Saved YouTube row removal failed");
            Require(playback.Source==PlaybackSource.LocalKaraoke&&playback.Player.State==OriginalVideoState.Play&&selected.Count==1,"Saved YouTube removal interrupted local playback");
            using(var response=await client.PostAsJsonAsync("api/action",new { action="clear",bank="youtube" }))Require(response.IsSuccessStatusCode,"Saved YouTube clear failed");
            using(var response=await client.PostAsJsonAsync("api/action",new { action="add",id="fixture0003" }))Require(response.IsSuccessStatusCode,"YouTube wait-list add failed");
            Require(playback.Source==PlaybackSource.LocalKaraoke&&playback.Player.State==OriginalVideoState.Play&&selected.Count==1,"Normal YouTube add took over local playback");
            await Send("clear");Require(selected.Count==1&&downloads.Count==0&&cancelled==1,"Original clear stopped playing song or failed to cancel downloads");
            await Send("retry");Require(retried==1,"Original retry did not reach its native callback");
            await Send("remove",third);Require(selected.Count==0&&playback.IsPlayingIdle,"Original last-song removal did not restore idle output");
        }
        finally { playback.NextRequested-=Next;playback.Player.Played-=remote.Refresh;music.OriginalQueue=null; }
    }
    private static void RequireMedia(bool accepted) { if(!accepted)throw new InvalidDataException("Remote vocal fixture rejected"); }
}
