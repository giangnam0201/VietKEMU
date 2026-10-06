using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using VietK.Core;

namespace VietK.NativePort;

static class NativeYouTubeVerification
{
    public static int Run(Application app,Canvas panel,BottomBar bottom,string url,string output,bool useFirefoxCookies=false)
    {
        Directory.CreateDirectory(output);var window=new Window { Content=panel,Width=1280,Height=800 };
        using var playback=new NativePlayback(bottom,output);
        var downloaded=false;var decoded=false;var failure="";var exit=1;
        window.Loaded+=async (_,_)=>
        {
            try
            {
                playback.ShowTelevision(window);
                var id=YouTubeMusicClient.VideoId(url)??throw new ArgumentException("Invalid verification video URL");
                var client=new YouTubeMusicClient(Path.Combine(AppContext.BaseDirectory,"YouTubeTools"),Path.Combine(output,"cache"),useFirefoxCookies:()=>useFirefoxCookies);
                using var timeout=new CancellationTokenSource(TimeSpan.FromMinutes(4));
                var path=await client.Download(new(id,"Public live verification video","",""),_=>{},timeout.Token);
                downloaded=true;
                if(!playback.PlayMedia(path,preserveStereo:true))throw new IOException("Downloaded YouTube media was rejected by decoder");
                var deadline=DateTime.UtcNow.AddSeconds(30);
                while(playback.Decoder.Position<500 && DateTime.UtcNow<deadline)await Task.Delay(100);
                uint width=0,height=0;
                decoded=playback.Decoder.Position>=500 && playback.Decoder.Native.Size(0,ref width,ref height) && width>0 && height>0;
                if(!decoded)throw new IOException("YouTube video did not produce native decoded video frames");
                var snapshot=Path.GetFullPath(Path.Combine(output,"youtube-native-tv.png"));
                if(!playback.Decoder.Native.TakeSnapshot(0,snapshot,0,0))throw new IOException("YouTube TV snapshot failed");
                deadline=DateTime.UtcNow.AddSeconds(10);
                while(!File.Exists(snapshot) && DateTime.UtcNow<deadline)await Task.Delay(100);
                if(!File.Exists(snapshot))throw new IOException("YouTube decoded-frame snapshot missing");
                exit=0;
            }
            catch(Exception ex) { failure=ex.Message; }
            finally
            {
                File.WriteAllText(Path.Combine(output,"youtube-verification.json"),JsonSerializer.Serialize(new {
                    realYouTubeDownload=downloaded,nativeVideoDecoded=decoded,embeddedPlayerUsed=false,
                    firefoxLoginSelected=useFirefoxCookies,
                    error=failure,scope="One public YouTube video from this runner; no claim of universal availability or original VietK media parity."
                },new JsonSerializerOptions { WriteIndented=true }));
                app.Shutdown(exit);
            }
        };
        app.Run(window);
        return exit;
    }
}
