using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Controls;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

internal static class MobileRemoteVerification
{
    public static async Task Run(NativePlayback playback,BottomBar bottom,Canvas panel,string root,string output)
    {
        var directory=Path.Combine(output,"mobile-remote");Directory.CreateDirectory(directory);
        using var music=new YouTubeMusicScreen(root,directory,playback,bottom);
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
        using var server=new MobileRemoteServer(Dispatcher.CurrentDispatcher,music,playback,0,true);
        server.SearchFixture=(query,ct)=>Task.FromResult<IReadOnlyList<YouTubeVideo>>([new("fixture0003","Remote search result","","")]);
        await server.StartAsync(false);
        using var client=new HttpClient { BaseAddress=new Uri($"http://127.0.0.1:{server.Port}/"),Timeout=TimeSpan.FromSeconds(10) };
        void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
        Require((await client.GetAsync("api/state")).StatusCode==HttpStatusCode.Unauthorized,"Unpaired phone read queue");
        Require((await client.GetStringAsync("/")).Contains("sessionStorage.setItem"),"Mobile browser page missing");
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
        if(Environment.GetEnvironmentVariable("VIETK_MOBILE_BROWSER_CHECK")=="1")
        {
            var start=new System.Diagnostics.ProcessStartInfo("python") { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
            start.ArgumentList.Add("tools/verify_mobile_browser.py");
            start.Environment["VIETK_REMOTE_TEST_URL"]=client.BaseAddress!.ToString();
            start.Environment["VIETK_REMOTE_TEST_TOKEN"]=server.TestToken;
            start.Environment["VIETK_REMOTE_TEST_OUTPUT"]=Path.GetFullPath(directory);
            using var browser=System.Diagnostics.Process.Start(start)!;
            var stdout=browser.StandardOutput.ReadToEndAsync();var stderr=browser.StandardError.ReadToEndAsync();
            await browser.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            // Failed navigation diagnostics may include the secret fragment: redact before reporting.
            var browserOutput=await stdout;var browserError=await stderr;
            Require(browser.ExitCode==0,"Phone browser verification failed: "+(browserOutput+browserError).Replace(server.TestToken,"[redacted]",StringComparison.Ordinal));
        }
        await Send(new { action="add",id="fixture0003" });Require((await Queue()).Length==3,"Phone add did not reach actual panel queue");
        await Send(new { action="top",id="fixture0003" });Require((await Queue())[1]=="fixture0003","Phone priority failed");
        await Send(new { action="move",id="fixture0003",target=2 });Require((await Queue())[2]=="fixture0003","Phone reorder failed");
        await Send(new { action="remove",id="fixture0003" });Require((await Queue()).Length==2,"Phone removal failed");
        await Send(new { action="shuffle" });Require((await Queue())[0]=="fixture0001","Phone shuffle changed the playing head");
        await Send(new { action="clear" });Require((await Queue()).SequenceEqual(new[]{"fixture0001"}),"Phone clear interrupted current song");
        var volume=playback.Decoder.OutputVolumeStep;
        await Send(new { action="command",id="voldec" });Require(playback.Decoder.OutputVolumeStep==Math.Max(0,volume-1)&&playback.Television.Overlay.LastControl=="play_ctrl_audio_bg","Phone volume/TV feedback failed");
        await Send(new { action="command",id="volinc" });
        await Send(new { action="screen" });Require(playback.Television.IsScreenMasked,"Phone blackout did not reach TV");
        await Send(new { action="screen" });Require(!playback.Television.IsScreenMasked,"Phone did not restore TV picture");
        await Send(new { action="command",id="pause_imv" });
        Require(playback.Player.State==OriginalVideoState.Pause,"Phone did not pause the native player");
        await Send(new { action="command",id="play_imv" });
        Require(playback.Player.State==OriginalVideoState.Play,"Phone did not resume native playback");
        await Send(new { action="command",id="replay_imv" });Require(playback.Television.Overlay.LastControl=="replay","Phone replay TV feedback missing");
        await Send(new { action="command",id="cut_song_imv" });Require((await Queue()).Length==0&&playback.IsPlayingIdle,"Phone next did not clear last song and restore idle video");
        using(var invalid=await client.PostAsJsonAsync("api/action",new { action="command",id="shutdown" }))Require(invalid.StatusCode==HttpStatusCode.BadRequest,"Unknown phone command accepted");
        using(var unknown=await client.PostAsJsonAsync("api/action",new { action="add",id="unsearched1" }))Require(unknown.StatusCode==HttpStatusCode.BadRequest,"Unsearched arbitrary media accepted");
        server.RePair();Require((await client.GetAsync("api/state")).StatusCode==HttpStatusCode.Unauthorized,"Old pairing secret remained active");
        // RePair uses live adapters only to make a local QR; remove it before other tests/captures.
        playback.Television.Overlay.Qr.Configure(new());
        File.WriteAllText(Path.Combine(directory,"verification.json"),JsonSerializer.Serialize(new { realHttp=true,pairedAuthorization=true,originRejection=true,searchFixture=true,
            nativeQueueAdd=true,priority=true,reorder=true,remove=true,clearProtectsPlaying=true,volumeAndTvFeedback=true,revocation=true,
            pauseResume=true,replay=true,nextRestoresIdle=true,blackout=true,phoneSizedBrowserTested=Environment.GetEnvironmentVariable("VIETK_MOBILE_BROWSER_CHECK")=="1",
            physicalPhoneWifiTested=false,manufacturerCloudCompatibility=false }));
    }
}
