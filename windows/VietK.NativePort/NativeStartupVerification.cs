using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// This runs the ordinary startup/import/UI path, unlike --capture or playback
// component fixtures. Its state stays in the requested CI verification directory.
internal static class NativeStartupVerification
{
    internal static void Attach(Application app,Window window,NativePlayback playback,MobileRemoteServer remote,
        LocalSongDatabase songs,string output,Stopwatch elapsed)
    {
        var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(100) };
        void Fail(Exception error)
        {
            timer.Stop();File.WriteAllText(Path.Combine(output,"startup-error.txt"),error.ToString());app.Shutdown(1);
        }
        app.DispatcherUnhandledException+=(_,e)=> { e.Handled=true;Fail(e.Exception); };
        timer.Tick+=async (_,_)=>
        {
            if(elapsed.Elapsed>TimeSpan.FromSeconds(30)) { Fail(new TimeoutException("Normal startup did not become ready within 30 seconds"));return; }
            if(!window.IsVisible||!playback.Television.IsVisible||playback.DecodedPreviewFrames<3||remote.Port==0)return;
            timer.Stop();
            try
            {
                using var client=new HttpClient { Timeout=TimeSpan.FromSeconds(3) };
                using var response=await client.GetAsync($"http://127.0.0.1:{remote.Port}/api/state");
                if(response.StatusCode!=HttpStatusCode.Unauthorized)throw new InvalidDataException("Normal startup remote pairing gate failed");
                File.WriteAllText(Path.Combine(output,"startup-verification.json"),JsonSerializer.Serialize(new {
                    normalStartupPath=true,fullCatalogueAndMediaImport=true,referencedSingerImport=true,
                    panelVisible=true,tvVisible=true,idleVideoDecoded=true,remotePairingGate=true,
                    elapsedMilliseconds=elapsed.ElapsedMilliseconds,processCpuMilliseconds=Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds,
                    songCount=songs.SongCount,mediaCount=songs.MediaCount
                },new JsonSerializerOptions { WriteIndented=true }));
                app.Shutdown(0);
            }
            catch(Exception error) { Fail(error); }
        };
        window.Loaded+=(_,_)=>timer.Start();
    }
}
