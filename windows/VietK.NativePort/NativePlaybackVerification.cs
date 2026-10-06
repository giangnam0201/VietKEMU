using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using PixelFormats = System.Windows.Media.PixelFormats;
using LibVLCSharp.Shared;
using VietK.Core;

namespace VietK.NativePort;

public static class NativePlaybackVerification
{
    public static int Run(Application app, Canvas panel, BottomBar bottom, string root, string fixtures, string output)
    {
        Directory.CreateDirectory(output);
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var host=NativePanelWindow.Create("VietK playback verification",panel);
        app.MainWindow = host;
        using var playback = new NativePlayback(bottom, output);
        playback.PreviewFrameChanged += _ => { };
        using var tap = new PcmTap(playback.Decoder.Native);
        var result = 1;
        host.Loaded += async (_, _) =>
        {
            try
            {
                playback.ShowTelevision(host);
                await NativeCollectionVerification.Run(host,root,output);
                Require(new WindowInteropHelper(host).Handle != IntPtr.Zero &&
                    new WindowInteropHelper(playback.Television).Handle != IntPtr.Zero &&
                    playback.Television.Owner is null, "Independent panel/TV window handles missing");
                var played = 0; playback.Player.Played += () => played++;
                await VerifyAudioFile(Path.Combine(fixtures,"stereo.mkv"),true);
                await VerifyAudioFile(Path.Combine(fixtures,"multiple.ts"),true);
                await VerifyAudioFile(Path.Combine(fixtures,"audio-ends-early.ts"),false);
                await NativeMusicPipelineVerification.Run(playback,root,fixtures,output);
                played=0;
                var idlePreviewBefore=playback.DecodedPreviewFrames;
                Require(playback.StartIdleDemo(),"Bundled original idle background unavailable");
                await Until(()=>played>0 && playback.DecodedPreviewFrames>idlePreviewBefore,
                    "Bundled original idle background did not decode into the panel preview");
                await NativeSongPreviewVerification.Run(host,playback,fixtures,output);
                await NativeSingerNavigationVerification.Run(host,playback,root,output);
                await NativeBroadcastVolumeVerification.Run(host,root,fixtures,output);
                await NativeBroadcastPlaylistVerification.Run(host,root,fixtures,output);
                await NativeBroadcastEditorVerification.Run(host,root,fixtures,output);
                await NativeSongGridAnimationVerification.Run(host,root,output);
                Require(!string.IsNullOrWhiteSpace(playback.Television.Overlay.MarqueeText),"Idle marquee missing");
                var marquee=playback.Television.Overlay;var scrollBefore=marquee.MarqueeOffset;
                await Task.Delay(300);
                Require(marquee.MarqueeOffset<scrollBefore-8 && marquee.MarqueeCopyCount>=2,"TV marquee did not move as a repeating text train");
                scrollBefore=marquee.MarqueeOffset;marquee.SetSong("");
                Require(Math.Abs(marquee.MarqueeOffset-scrollBefore)<2,"Unchanged idle message restarted the marquee");
                var qr=marquee.Qr;var savedBinding=qr.Binding;var savedMode=qr.State.Mode;
                var binding=new MobileQrBinding("fixture","https://x.invalid/?c=","1234","5678");
                qr.Configure(binding);qr.SetMode(0,false);
                await Task.Delay(120);
                var qrFrame=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);
                playback.Television.VideoLayers.UpdateLayout();qrFrame.Render(playback.Television.VideoLayers);
                var qrPixels=new byte[qrFrame.PixelWidth*qrFrame.PixelHeight*4];
                qrFrame.CopyPixels(qrPixels,qrFrame.PixelWidth*4,0);
                var qrReader=new ZXing.BarcodeReaderGeneric { Options=new ZXing.Common.DecodingOptions { TryHarder=true } };
                var qrResult=qrReader.Decode(qrPixels,qrFrame.PixelWidth,qrFrame.PixelHeight,ZXing.RGBLuminanceSource.BitmapFormat.BGRA32);
                Require(qrResult?.Text==OriginalMobileQr.TelevisionPayload(binding),"TV QR did not decode from the actual shared TV surface");
                var sharedQr=playback.Television.CompositePreview(playback.Decoder.VideoSurface);
                var qrCorner=new byte[4];sharedQr.CopyPixels(new Int32Rect(34,38,1,1),qrCorner,4,0);
                Require(qrCorner.Take(3).All(v=>v>240),"QR white margin missing in composed panel preview");
                var qrEncoder=new PngBitmapEncoder();qrEncoder.Frames.Add(BitmapFrame.Create(qrFrame));
                using(var qrFile=File.Create(Path.Combine(output,"synthetic-mobile-qr-preview.png")))qrEncoder.Save(qrFile);
                var modeDialog=new TvQrModeDialog(panel,qr);modeDialog.Select(2);modeDialog.Close();
                Require(qr.State.Mode==0,"Cancel changed TV QR mode");
                modeDialog=new TvQrModeDialog(panel,qr);modeDialog.Select(1);modeDialog.Confirm(false);
                Require(qr.State.Mode==1&&!qr.State.ImageVisible,"Original mode-one visibility branch was replaced");
                qr.SetMode(savedMode,false);qr.Configure(savedBinding);
                await MobileRemoteVerification.Run(playback,bottom,panel,root,fixtures,output,(expected,unwanted,message)=>Tone(tap,expected,unwanted,message));
                var idleEncoder=new PngBitmapEncoder();idleEncoder.Frames.Add(BitmapFrame.Create(playback.PreviewFrame!));
                using(var idleFile=File.Create(Path.Combine(output,"bundled-idle-preview.png")))idleEncoder.Save(idleFile);
                played=0;
                var clip = Path.GetFullPath(Path.Combine(root, "player", "grade_video.mp4"));
                var previewFramesBefore=playback.DecodedPreviewFrames;
                Require(playback.PlayMedia(clip), "Original APK grading video rejected");
                await Until(() => played > 0 && playback.Decoder.Position > 0, "Original video did not decode/render");
                Require(!playback.Television.BlackVisible, "Original black cover stayed above playing media");
                var previewFixture=new byte[640*360*4];
                for(var pixel=0;pixel<previewFixture.Length;pixel+=4)
                { previewFixture[pixel]=31;previewFixture[pixel+1]=63;previewFixture[pixel+2]=127;previewFixture[pixel+3]=255; }
                var composed=playback.Television.CompositePreview(BitmapSource.Create(640,360,96,96,
                    PixelFormats.Bgra32,null,previewFixture,640*4));
                var corner=new byte[4];composed.CopyPixels(new Int32Rect(500,300,1,1),corner,4,0);
                Require(corner.SequenceEqual(new byte[] {31,63,127,255}),"TV logo overlay stretched across the panel video preview");
                var snapshot = Path.GetFullPath(Path.Combine(output, "original-tv-video.png"));
                playback.SaveVideoFrame(snapshot);
                await Until(() => File.Exists(snapshot) && new FileInfo(snapshot).Length > 1024, "Decoded video snapshot missing");
                await Until(() => playback.DecodedPreviewFrames>previewFramesBefore && playback.PreviewFrame is not null,
                    "Panel preview did not receive real decoder pixels");
                var previewEncoder=new PngBitmapEncoder();previewEncoder.Frames.Add(BitmapFrame.Create(playback.PreviewFrame!));
                using(var previewFile=File.Create(Path.Combine(output,"panel-tv-preview.png")))previewEncoder.Save(previewFile);

                var sourceStereo = Path.GetFullPath(Path.Combine(fixtures, "stereo.mkv"));
                foreach(var tracks in new[]{(0,5),(5,0)})
                {
                    Require(playback.PlayMedia(sourceStereo,Metadata(sourceStereo,tracks.Item1,tracks.Item2)),"Stereo-only karaoke fixture rejected");
                    await Until(()=>playback.Player.State==OriginalVideoState.Play,"Stereo-only karaoke fixture did not start");
                    await Stereo(tap);
                    Require(!playback.CanSwitchVocal,"APK stereo-only mode allowed vocal switching");
                    var beforeMode=playback.Player.SingMode;
                    playback.Command("ori_imv");
                    Require(playback.Player.SingMode==beforeMode,"Unavailable vocal mode changed the requested mode");
                    await Stereo(tap);
                }
                var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();
                string stereo;
                try
                {
                    var url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/stereo.mkv";
                    var serving=Task.Run(async()=>
                    {
                        for(var request=0;request<2;request++)
                        {
                        using var socket=await listener.AcceptTcpClientAsync();
                        await using var stream=socket.GetStream();
                        using var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
                        while(!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                        await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+new FileInfo(sourceStereo).Length+"\r\nConnection: close\r\n\r\n"));
                        if(request==1) { await using var file=File.OpenRead(sourceStereo);await file.CopyToAsync(stream); }
                        }
                    });
                    using var transfer=new OriginalMusicTransfer();
                    stereo=await transfer.Download(101000,url,Path.Combine(output,"downloaded"),(_,_)=>{},CancellationToken.None);
                    await serving.WaitAsync(TimeSpan.FromSeconds(15));
                    Require(SHA256.HashData(File.ReadAllBytes(stereo)).SequenceEqual(SHA256.HashData(File.ReadAllBytes(sourceStereo))),
                        "HTTP download changed media bytes");
                }
                finally { listener.Stop(); }
                Require(playback.PlayMedia(stereo, Metadata(stereo, 0, 1)), "Stereo test media rejected");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Native.AudioTrackDescription.Count(t => t.Id >= 0) == 1,
                    "Single audio track did not start");
                await Tone(tap, 880, 440, "Original channel should duplicate the right channel");
                playback.Command("ori_imv");
                await Tone(tap, 440, 880, "Accompaniment channel should duplicate the left channel");
                await Until(() => !bottom.OriginalVocal, "Panel track icon did not observe the decoder");
                Require(playback.Television.Overlay.LastControl=="accompany" && playback.Television.Overlay.ControlVisible,
                    "Confirmed accompaniment switch did not show original TV image");
                playback.Command("pause_imv");
                await Until(() => bottom.Paused && playback.Decoder.Native.State == VLCState.Paused, "Pause button did not pause decoder");
                Require(playback.Television.Overlay.Paused,"TV pause indicator missing");
                Require(playback.Television.Overlay.PauseVisible,"Pause cycle did not begin visibly");
                await Until(()=>!playback.Television.Overlay.PauseVisible,"Original pause cycle never hid its image");
                Require(playback.Television.Overlay.Paused,"Pause cycle changed decoder pause state");
                await Until(()=>playback.Television.Overlay.PauseVisible,"Original pause cycle never restored its image");
                playback.Television.Overlay.ShowControl("play_ctrl_audio_bg",playback.Decoder.OutputVolumeStep);
                Require(!playback.Television.Overlay.PauseVisible && playback.Television.Overlay.Paused,
                    "Volume feedback failed to hide pause image while preserving paused state");
                var pausedAt = playback.Decoder.Position;
                await Task.Delay(350);
                Require(Math.Abs(playback.Decoder.Position - pausedAt) < 100, "Paused decoder clock kept running");
                playback.Command("play_imv");
                await Until(() => !bottom.Paused && playback.Decoder.Position > pausedAt, "Play button did not resume decoder");
                Require(playback.Player.Seek(6000) == 0, "Valid original seek rejected");
                await Until(() => playback.Decoder.Position >= 5800, "Native seek did not reach target");
                Require(playback.Player.Seek(-1) == -1 && playback.Player.Seek(playback.Decoder.Duration + 1) == -1,
                    "Original seek bounds were bypassed");
                playback.Command("replay_imv");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Position is > 0 and < 2000,
                    "Replay did not restart actual media");
                var volume = playback.Decoder.OutputVolumeStep;
                var quietPower=await MeasurePower(tap,440);
                playback.Command("volinc");
                Require(playback.Decoder.OutputVolumeStep == volume + 1,"Original 0..20 volume step differs");
                Require(playback.Television.Overlay.LastControl=="play_ctrl_audio_bg" && playback.Television.Overlay.ControlVisible,
                    "TV volume feedback missing");
                // libVLC's amem output does not publish a volume report for its
                // getter. Verify actual decoded sample amplitude instead.
                var loudPower=await UntilPower(tap,440,value=>value>quietPower*1.15,
                    "Volume increment did not increase actual output PCM amplitude");
                playback.Command("voldec");
                Require(playback.Decoder.OutputVolumeStep == volume, "Volume decrement did not restore level");
                var restoredPower=await UntilPower(tap,440,value=>value>=quietPower*.8 && value<=quietPower*1.2,
                    "Volume decrement did not restore actual output PCM amplitude");
                // Synthetic TV graphics exercise presentation without uploading
                // the newly recovered proprietary mute/unmute PNGs.
                var muteDirectory=Path.Combine(output,"mute-fixture");Directory.CreateDirectory(muteDirectory);
                var mutePixels=new byte[32*32*4];
                for(var pixel=0;pixel<mutePixels.Length;pixel+=4) { mutePixels[pixel]=255;mutePixels[pixel+3]=255; }
                foreach(var name in new[]{"mute","unmute"})
                {
                    var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(BitmapSource.Create(32,32,96,96,PixelFormats.Bgra32,null,mutePixels,32*4)));
                    using var file=File.Create(Path.Combine(muteDirectory,name+".png"));png.Save(file);
                }
                marquee.MuteResourceDirectory=muteDirectory;
                playback.Player.Seek(0);playback.Command("mute");
                Require(playback.Decoder.Muted&&playback.Decoder.OutputVolumeStep==volume&&marquee.MuteVisible,"Mute lost stored volume or original TV indicator");
                await UntilPower(tap,440,value=>value<quietPower*.001,"Mute did not silence decoded PCM");
                await Until(()=>!marquee.MuteVisible,"Mute indicator did not enter the original two-hidden-tick interval");
                await Until(()=>marquee.MuteVisible,"Mute indicator did not return after its hidden interval");
                playback.Command("pause_imv");await Task.Delay(1100);
                Require(marquee.Paused&&!marquee.MuteVisible,"Pause did not take priority over mute indicator");
                playback.Command("play_imv");playback.Command("mute");
                Require(!playback.Decoder.Muted&&marquee.LastControl=="unmute","Unmute did not show its original feedback");
                await UntilPower(tap,440,value=>value>=quietPower*.8&&value<=quietPower*1.2,"Unmute fade did not restore the actual PCM level");
                playback.Command("mute");playback.Command("volinc");
                Require(!playback.Decoder.Muted&&!marquee.Muted&&playback.Decoder.OutputVolumeStep==volume+1,"Volume increment failed to clear mute");
                playback.Command("voldec");

                Require(playback.PlayMedia(stereo,preserveStereo:true), "YouTube-style stereo media rejected");
                await Stereo(tap);
                playback.Command("replay_imv");
                await Stereo(tap);
                Require(playback.Decoder.PreserveStereo,"Replay lost stereo playback mode");

                var multi = Path.GetFullPath(Path.Combine(fixtures, "multiple.ts"));
                using(var growing=new ProgressiveVideo(Path.Combine(output,"growing-video.ts"),async (target,cancel)=>
                {
                    await using var source=File.OpenRead(multi);var chunk=new byte[8192];
                    while(true)
                    {
                        var count=await source.ReadAsync(chunk,cancel);if(count==0)break;
                        await target.WriteAsync(chunk.AsMemory(0,count),cancel);await target.FlushAsync(cancel);
                        await Task.Delay(100,cancel);
                    }
                    await Task.Delay(3000,cancel);
                },CancellationToken.None))
                {
                    await growing.WaitUntilReady(CancellationToken.None);
                    var before=playback.DecodedPreviewFrames;
                    Require(playback.PlayMedia(growing.Url,preserveStereo:true),"Growing video stream rejected");
                    await Until(()=>playback.DecodedPreviewFrames>before+10,"Growing stream did not produce live decoded frames");
                    Require(!growing.Completion.IsCompleted,"Video only started after download completed");
                    var frameStart=playback.DecodedPreviewFrames;await Task.Delay(1000);
                    Require(playback.DecodedPreviewFrames-frameStart>=10,"Shared TV/preview source still updates like snapshot polling");
                    playback.Player.Stop();
                }
                // Vocal mode persists between songs in the original player.
                // Select original explicitly before testing stream index 1.
                playback.Player.SetSingMode(OriginalSingMode.Original);
                Require(playback.PlayMedia(multi, Metadata(multi, 1, 0)), "Multiple-track MPEG media rejected");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Native.AudioTrackDescription.Count(t => t.Id >= 0) == 2,
                    "Two MPEG audio streams not available");
                await Tone(tap, 1200, 480, "Original stream must select audio ordinal 1");
                playback.Command("accp_imv");
                await Tone(tap, 480, 1200, "Accompaniment stream must select audio ordinal 0");
                var next = 0; playback.NextRequested += () => next++;
                playback.Command("cut_song_imv");
                Require(next == 1 && playback.Player.State == OriginalVideoState.Idle && playback.Television.BlackVisible,
                    "Next button did not stop decoder and request queue advance");
                // Verify actual decoder end-of-stream, not a timer-generated
                // completion. Seek close to the end of the same MPEG file.
                Require(playback.PlayMedia(multi, Metadata(multi, 1, 0)), "Completion media failed to restart");
                await Until(() => playback.Player.State == OriginalVideoState.Play && playback.Decoder.Duration > 0, "Completion media did not start");
                playback.Player.Seek(playback.Decoder.Duration - 800);
                await Until(() => next == 2, "Actual decoder completion did not request queue advance");
                Require(playback.Player.State == OriginalVideoState.Idle, "Original completion state not idle");
                // This is explicitly a test fixture, not the original idle asset.
                File.Copy(multi,Path.Combine(output,"Demo.mp4"),true);
                var idleStarts=played;
                Require(playback.StartIdleDemo(),"Configured Demo.mp4 did not start");
                await Until(()=>played>idleStarts && playback.Decoder.Duration>0,"Idle video did not decode");
                playback.Player.Seek(playback.Decoder.Duration-800);
                await Until(()=>played>idleStarts+1,"Idle video did not loop after real decoder completion");
                Require(next==2,"Idle broadcast advanced the song queue");
                idleStarts=played;playback.Command("replay_imv");
                Require(playback.IsPlayingIdle,"Replay changed idle broadcast into a queued song");
                await Until(()=>played>idleStarts && playback.Decoder.Duration>0,"Idle replay did not decode");
                playback.Player.Seek(playback.Decoder.Duration-800);
                await Until(()=>played>idleStarts+1,"Replayed idle broadcast stopped looping");
                Require(next==2,"Replayed idle broadcast advanced the queue");
                playback.SetIdleVideo(multi);playback.Command("volinc");
                using(var preferences=JsonDocument.Parse(File.ReadAllText(Path.Combine(output,"playback-state.json"))))
                    Require(preferences.RootElement.GetProperty("IdleVideoPath").GetString()==Path.GetFullPath(multi),
                        "Volume save lost custom idle-video selection");
                playback.UseFactoryIdleVideo();playback.Player.Stop();File.Delete(Path.Combine(output,"Demo.mp4"));
                // This package uses a known decoder fixture. The owner's real
                // factory clip stays local and is never uploaded by CI.
                var supplement=Path.Combine(output,"supplement-fixture.zip");
                using(var bundle=ZipFile.Open(supplement,ZipArchiveMode.Create))
                    bundle.CreateEntryFromFile(clip,"player/60003950.mp4");
                Environment.SetEnvironmentVariable("VIETK_ORIGINAL_RESOURCES",supplement);OriginalSupplement.Initialize();
                Require(File.Exists(Path.Combine(OriginalSupplement.Root,"player","60003950.mp4")),"Local original resource import lost the idle clip");
                idleStarts=played;Require(playback.StartIdleDemo(),"Imported idle clip did not start");
                Require(playback.IdleVideoSource==Path.Combine(OriginalSupplement.Root,"player","60003950.mp4"),
                    "Imported factory idle lost precedence over the random background");
                await Until(()=>played>idleStarts,"Imported idle clip did not decode");playback.Player.Stop();
                // Synthetic PNG/WAV exercise the original six-second expression
                // path without publishing the owner's recovered media.
                var expressionRoot=Path.Combine(output,"expression-fixture");
                var expressionDirectory=Path.Combine(expressionRoot,"ambience");Directory.CreateDirectory(expressionDirectory);
                var picture=new PngBitmapEncoder();
                var red=new byte[184*184*4];for(var pixel=0;pixel<red.Length;pixel+=4) { red[pixel+2]=255;red[pixel+3]=255; }
                picture.Frames.Add(BitmapFrame.Create(BitmapSource.Create(184,184,96,96,PixelFormats.Bgra32,null,red,184*4)));
                using(var file=File.Create(Path.Combine(expressionDirectory,"memeda.png")))picture.Save(file);
                File.Copy(Path.Combine(root,"player","pause.png"),Path.Combine(expressionDirectory,"dc_overseas_popup_close.png"));
                File.Copy(Path.Combine(root,"player","pause.png"),Path.Combine(expressionDirectory,"dc_overseas_set_off.png"));
                File.Copy(Path.Combine(root,"player","play.png"),Path.Combine(expressionDirectory,"dc_overseas_set_on.png"));
                File.Copy(Path.Combine(expressionDirectory,"memeda.png"),Path.Combine(expressionDirectory,"barrage_ellipse.png"));
                File.Copy(Path.Combine(expressionDirectory,"memeda.png"),Path.Combine(expressionDirectory,"barrage_rocket.png"));
                File.Copy(Path.Combine(fixtures,"expression.wav"),Path.Combine(expressionDirectory,"memeda.wav"));
                using(var expressions=new AmbienceExpressions(playback.Television.Overlay,playback.Television,playback))
                using(var expressionTap=new PcmTap(expressions.Sound))
                {
                    var beforeChildren=panel.Children.Count;expressions.ShowDialog(host,expressionRoot);
                    Require(panel.Children.Count==beforeChildren+1,"Vui nhon expression dialog did not attach to the panel");
                    Require(playback.PlayMedia(sourceStereo,null,true),"Karaoke fixture rejected during expression verification");
                    await Until(()=>playback.Decoder.Position>0,"Karaoke fixture did not start");
                    var before=playback.Decoder.Position;
                    Require(expressions.Show("memeda",expressionRoot),"Original expression file was not shown");
                    Require(!expressions.Show("../untrusted",expressionRoot),"Expression selection escaped the original filename list");
                    Require(playback.Television.Overlay.ExpressionVisible,"TV expression overlay is hidden");
                    var combined=playback.Television.CompositePreview(playback.Decoder.VideoSurface);var center=new byte[4];
                    combined.CopyPixels(new Int32Rect(320,110,1,1),center,4,0);
                    Require(center[2]>240 && center[0]<10 && center[1]<10,"Panel preview omitted the TV expression pixels");
                    await Task.Delay(1600);await Tone(expressionTap,1600,880,"Expression sound did not loop beyond its first second");
                    Require(expressions.Show("memeda",expressionRoot),"Expression could not restart for mute verification");
                    await Tone(expressionTap,1600,880,"Restarted expression sound missing before mute");
                    var expressionPeak=await MeasurePeak(expressionTap);
                    playback.SetMuted(true);
                    await UntilPeak(expressionTap,peak=>peak<expressionPeak*.001,"Mute left expression WAV audible");
                    playback.SetMuted(false);
                    await UntilPeak(expressionTap,peak=>peak>=expressionPeak*.8&&peak<=expressionPeak*1.2,"Expression audio did not recover its original level after unmute; baseline peak="+expressionPeak);
                    Require(playback.Decoder.Position>before+1000 && playback.Decoder.PreserveStereo,"Expression replaced or interrupted karaoke playback");
                    var dialog=(Canvas)panel.Children[panel.Children.Count-1];var content=(Canvas)((Border)dialog.Children[0]).Child;
                    var tvHeading=content.Children.OfType<Border>().Single(child=>child.Child is TextBlock { Text:"TV" });
                    Click(tvHeading);Require(expressions.CurrentTab==12,"Original TV tab did not select");
                    var tvPage=content.Children.OfType<Canvas>().Single(child=>Equals(child.Tag,"original-tv-page"));
                    var toggle=tvPage.Children.OfType<Image>().Single();Click(toggle);
                    Require(playback.Television.IsScreenMasked,"Original TV mask toggle did not blank output");
                    var hidden=playback.Television.CompositePreview(playback.Decoder.VideoSurface);
                    VerifyBlack(hidden,"TV mask left video or OSD visible in the panel preview");
                    playback.Television.UpdateLayout();
                    var actualTv=new RenderTargetBitmap(1280,720,96,96,PixelFormats.Pbgra32);actualTv.Render(playback.Television.VideoLayers);
                    VerifyBlack(actualTv,"TV mask was not above the complete live TV visual");
                    var maskedPosition=playback.Decoder.Position;await Stereo(tap);await Task.Delay(400);
                    Require(playback.Decoder.Position>maskedPosition+200,"TV mask stopped the song clock");
                    Click(toggle);Require(!playback.Television.IsScreenMasked,"TV mask toggle did not restore output");
                    Require(playback.Television.Overlay.ExpressionVisible,"TV mask destroyed the active expression");
                    await Until(()=>!playback.Television.Overlay.ExpressionVisible,"Expression did not disappear at the original timeout");
                    Require(expressions.ActiveExpression=="" && !expressions.Sound.IsPlaying,"Expression sound continued after its TV image expired");
                    var close=content.Children.OfType<Image>().Single();Click(close);
                    Require(panel.Children.Count==beforeChildren,"Original ambience close button did not dismiss the dialog");
                    expressions.ShowDialog(host,expressionRoot);
                    Require(expressions.CurrentTab==12 && panel.Children.Count==beforeChildren+1,"Ambience dialog lost the selected TV tab on reopen");
                    var reopened=(Canvas)((Border)((Canvas)panel.Children[panel.Children.Count-1]).Children[0]).Child;
                    Require(reopened.Children.OfType<Canvas>().Single(child=>Equals(child.Tag,"original-tv-page")).Visibility==Visibility.Visible,"Reopened ambience dialog did not restore TV page visibility");
                    var barrageHeading=reopened.Children.OfType<Border>().Single(child=>child.Child is TextBlock { Text:"Lời chúc" });Click(barrageHeading);
                    Require(expressions.CurrentTab==11,"Wishes tab did not select");
                    var barragePage=reopened.Children.OfType<Canvas>().Single(child=>Equals(child.Tag,"original-barrage-page"));
                    var input=(TextBox)barragePage.Children.OfType<Border>().Single(child=>child.Child is TextBox).Child;
                    Require(input.MaxLength==30 && !input.AcceptsReturn,"Wishes input differs from the original single-line limit");
                    var send=barragePage.Children.OfType<Border>().Single(child=>Equals(child.Tag,"original-barrage-send"));
                    Click(send);Require(playback.Television.Overlay.Barrage.PendingCount==0,"Empty wishes input submitted a message");
                    input.Text="Chúc mừng sinh nhật";Click(send);Require(input.Text=="","Submitted wishes input was not cleared");
                    var barrage=playback.Television.Overlay.Barrage;Require(barrage.PendingCount==1 && barrage.VisibleCount==0,"Barrage appeared before its original delay");
                    await Until(()=>barrage.VisibleCount==1,"Submitted message did not appear on the TV");
                    var beforeLeft=barrage.FirstVisibleLeft!.Value;await Task.Delay(300);
                    Require(barrage.FirstVisibleLeft<beforeLeft-50,"Wishes message did not move right to left");
                    var preview=playback.Television.CompositePreview(null); // Exclude red pixels in the decoder fixture.
                    var pixels=new byte[preview.PixelWidth*preview.PixelHeight*4];preview.CopyPixels(pixels,preview.PixelWidth*4,0);
                    Require(Enumerable.Range(0,pixels.Length/4).Any(pixel=>pixels[pixel*4+2]>245 && pixels[pixel*4]<10 && pixels[pixel*4+1]<10),
                        "Shared panel preview omitted the moving barrage bitmap");
                    barrage.Clear();Require(barrage.VisibleCount==0 && barrage.PendingCount==0,"Barrage clear left delayed messages running");
                    for(var index=0;index<12;index++)Require(barrage.Send("Chúc mừng",expressionRoot),"Barrage saturation fixture was not submitted");
                    await Until(()=>barrage.PendingCount==0 && barrage.Canvas.Children.Count==barrage.VisibleCount,
                        "Delayed barrage burst did not reach layout");
                    Require(barrage.VisibleCount==9,"Original measured-height overflow filtering admitted overlapping rows or merged duplicate messages");
                    var rows=barrage.Canvas.Children.OfType<Image>().Where(image=>image.Visibility==Visibility.Visible)
                        .Select(image=>(System.Windows.Media.TranslateTransform)image.RenderTransform).OrderBy(transform=>transform.Y).ToArray();
                    Require(rows.Length==9 && rows[0].Y==5 && rows[^1].Y==597 &&
                        rows.Zip(rows.Skip(1)).All(pair=>pair.Second.Y-pair.First.Y==74),"TV barrage burst ignored retained row positions");
                    barrage.Clear();Require(barrage.VisibleCount==0,"Barrage clear did not remove saturated rows");
                    panel.Children.RemoveAt(panel.Children.Count-1);playback.Player.Stop();
                }
                var unsafeSupplement=Path.Combine(output,"unsafe-supplement-fixture.zip");
                using(var bundle=ZipFile.Open(unsafeSupplement,ZipArchiveMode.Create))
                { using var writer=new StreamWriter(bundle.CreateEntry("ambience/../../escape.png").Open());writer.Write("fixture"); }
                Environment.SetEnvironmentVariable("VIETK_ORIGINAL_RESOURCES",unsafeSupplement);var rejected=false;
                try { OriginalSupplement.Initialize(); } catch(InvalidDataException) { rejected=true; }
                Require(rejected,"Supplement importer admitted a path outside its local directory");
                Environment.SetEnvironmentVariable("VIETK_ORIGINAL_RESOURCES",null);
                File.WriteAllText(Path.Combine(output, "playback-verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsDecoder = "bundled libVLC", androidRuntimeUsed = false,
                    independentPanelAndTvWindows = true, originalApkVideoDecoded = true,
                    decodedPanelPreviewVerified=true, tvPauseAndVolumeFeedbackVerified=true,actualMutePcmSilenceVerified=true,muteBlinkAndPausePriorityVerified=true,unmutePcmRestoredVerified=true,
                    originalPauseRepeatAndConfirmedTrackFeedbackVerified=true,
                    audioEndingAt45PercentRejected=true,fullMkvAndTsAudioCoverageVerified=true,
                    continuousMarqueeMovementAndRefreshVerified=true,
                    configuredIdleDemoDecoderAndLoopVerified=true,
                    localSupplementImportIdlePrecedenceAndTraversalRejectionVerified=true,
                    expressionDialogSharedPreviewLoopingSoundAndTimeoutVerified=true,
                    tvMaskButtonFullOutputSharedPreviewAndContinuedStereoPlaybackVerified=true,
                    wishesInputDelayRightToLeftMovementAndSharedPreviewVerified=true,
                    barrageBurstDuplicateRetentionAndVerticalOverflowVerified=true,
                    idleReplayLoopAndSavedVideoSelectionVerified=true,
                    bundledOriginalBackgroundDecodedIntoPreview=true,
                    playbackBeforeDownloadCompletionVerified=true,sharedFrameRateAbove10FpsVerified=true,
                    stereoChannelPcmVerified = true, multipleAudioStreamPcmVerified = true,
                    youtubeStereoPcmAndReplayVerified = true,
                    pauseResumeClockVerified = true, nativeSeekReplayVerified = true,
                    panelPlaybackObserverVerified = true, volumeStepVerified = true,
                    volumePcmPowerBefore=quietPower,volumePcmPowerAfterIncrement=loudPower,volumePcmPowerRestored=restoredPower,
                    nextAndDecoderCompletionVerified = true,
                    httpDownloadedVideoHashAndDecoderVerified = true,
                    signedHttpLoginMediaRequestCacheQueueAndDecoderVerified = true,
                    scope = "Real video pixels and decoded PCM, with original player/control rules. Audio-device playback, full TV OSD, encrypted karaoke, storage, scoring and live downloads remain unverified."
                }, new JsonSerializerOptions { WriteIndented = true }));
                result = 0;
            }
            catch (Exception error) { File.WriteAllText(Path.Combine(output, "playback-error.txt"), error+
                $"\nAudio tracks={playback.Decoder.LastAudioTrackCount}; switch succeeded={playback.Decoder.LastTrackSwitchSucceeded}; channel={playback.Decoder.Native.Channel}"); }
            finally { app.Shutdown(); }
        };
        app.Run(host);
        return result;
    }
    private static SongMedia Metadata(string path, int original, int accompaniment) =>
        new(1, 101000, path, 100, original, accompaniment, "0", "0", 1, "", "", "", "", 0, null, null, "decoder-fixture");
    private static void Click(UIElement element)=>element.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(
        System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left)
        { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
    private static void VerifyBlack(BitmapSource image,string message)
    {
        var pixels=new byte[image.PixelWidth*image.PixelHeight*4];image.CopyPixels(pixels,image.PixelWidth*4,0);
        for(var offset=0;offset<pixels.Length;offset+=4)
            if(pixels[offset]!=0 || pixels[offset+1]!=0 || pixels[offset+2]!=0 || pixels[offset+3]!=255)throw new InvalidDataException(message);
    }
    private static async Task VerifyAudioFile(string path,bool expected)
    {
        var start=new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"YouTubeTools","ffprobe.exe")) {
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true };
        foreach(var argument in new[]{"-v","error","-show_entries","stream=codec_type,start_time,duration:stream_tags=DURATION","-of","json",path})start.ArgumentList.Add(argument);
        using var process=Process.Start(start)!;var output=process.StandardOutput.ReadToEndAsync();var errors=process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();Require(process.ExitCode==0,"Audio fixture probe failed: "+await errors);
        var accepted=true;
        try { YouTubeMusicClient.ValidateAudioCoverage(await output); }
        catch(YouTubeIncompleteAudioException) { accepted=false; }
        Require(accepted==expected,"Audio coverage check disagreed with independently generated fixture: "+Path.GetFileName(path));
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    private static async Task Until(Func<bool> condition, string message)
    {
        var stop = DateTime.UtcNow.AddSeconds(15);
        while (!condition()) { if (DateTime.UtcNow >= stop) throw new TimeoutException(message); await Task.Delay(50); }
    }
    private static async Task Tone(PcmTap tap, int expected, int unwanted, string message)
    {
        var stop = DateTime.UtcNow.AddSeconds(5);
        double signal = 0, other = 0;
        do
        {
            tap.Reset(); await Task.Delay(300);
            var samples = tap.Read();
            signal = Math.Min(PcmTap.Power(samples, expected,0),PcmTap.Power(samples, expected,1));
            other = Math.Max(PcmTap.Power(samples, unwanted,0),PcmTap.Power(samples, unwanted,1));
            if (samples.Length >= 4800 && signal > 0.000001 && signal > other * 25) return;
        } while (DateTime.UtcNow < stop);
        throw new InvalidDataException($"{message}; decoded power expected={signal}, unwanted={other}");
    }
    private static async Task Stereo(PcmTap tap)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);
        do
        {
            tap.Reset();await Task.Delay(300);var samples=tap.Read();
            if(samples.Length>=4800 && PcmTap.Power(samples,440,0)>0.000001 &&
                PcmTap.Power(samples,880,1)>0.000001 &&
                PcmTap.Power(samples,440,0)>PcmTap.Power(samples,880,0)*25 &&
                PcmTap.Power(samples,880,1)>PcmTap.Power(samples,440,1)*25)return;
        }while(DateTime.UtcNow<deadline);
        throw new InvalidDataException("Native stereo playback collapsed or swapped the decoded left/right audio channels");
    }
    private static async Task<double> MeasurePower(PcmTap tap,int frequency)
    {
        var samples=await CaptureSamples(tap);
        return Math.Min(PcmTap.Power(samples,frequency,0),PcmTap.Power(samples,frequency,1));
    }
    private static async Task<double> MeasurePeak(PcmTap tap)
    {
        var samples=await CaptureSamples(tap);
        // Loop boundaries can insert silence or reset phase. Peak amplitude
        // measures the known sine fixture's gain without averaging those gaps.
        // It also rejects even a brief non-silent output while muted.
        return samples.Max(sample=>Math.Abs(sample/32768d));
    }
    internal static async Task<short[]> CaptureSamples(PcmTap tap)
    {
        tap.Reset();await Task.Delay(400);
        var samples=tap.Read();
        // Repeating one-second expression media can be between decoder inputs.
        // Require real PCM, but give the next loop time to supply enough frames.
        var deadline=DateTime.UtcNow.AddSeconds(2);
        while(samples.Length<4800&&DateTime.UtcNow<deadline)
        { await Task.Delay(100);samples=tap.Read(); }
        if(samples.Length<4800)throw new InvalidDataException("No decoded PCM available for gain verification; "+tap.OutputState);
        return samples;
    }
    internal static async Task UntilPeak(PcmTap tap,Func<double,bool> accepted,string message)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);double peak;
        try
        {
            do { peak=await MeasurePeak(tap);if(accepted(peak))return; }while(DateTime.UtcNow<deadline);
        }
        catch(InvalidDataException error) { throw new InvalidDataException(message+"; "+error.Message,error); }
        throw new InvalidDataException(message+"; measured PCM peak="+peak+"; "+tap.OutputState);
    }
    private static async Task<double> UntilPower(PcmTap tap,int frequency,Func<double,bool> accepted,string message)
    {
        var deadline=DateTime.UtcNow.AddSeconds(5);double power;
        do { power=await MeasurePower(tap,frequency);if(accepted(power))return power; } while(DateTime.UtcNow<deadline);
        throw new InvalidDataException(message+"; measured power="+power);
    }
    // Native audio output tap enables CI without an audio device, while checking
    // the actual downmix/stream-selection result in the decoder's PCM output.
    internal sealed class PcmTap : IDisposable
    {
        private readonly object gate = new();
        private readonly List<short> samples = new();
        private readonly MediaPlayer.LibVLCAudioPlayCb callback;
        private readonly MediaPlayer outputPlayer;
        public string OutputState=>"audio state="+outputPlayer.State+", time="+outputPlayer.Time;
        public PcmTap(MediaPlayer player)
        {
            outputPlayer=player;
            callback = (_, data, count, _) =>
            {
                var values = new short[checked((int)count * 2)];
                Marshal.Copy(data, values, 0, values.Length);
                lock (gate) { if (samples.Count < 96000) samples.AddRange(values); }
            };
            player.SetAudioFormat("S16N", 48000, 2);
            player.SetAudioCallbacks(callback, null!, null!, null!, null!);
        }
        public void Reset() { lock (gate) samples.Clear(); }
        public short[] Read() { lock (gate) return samples.ToArray(); }
        public static double Power(short[] data, int frequency,int channel)
        {
            var count = data.Length / 2;
            if (count == 0) return 0;
            double sin = 0, cos = 0;
            for (var i = 0; i < count; i++)
            {
                var sample = data[i * 2+channel] / 32768.0;
                var phase = 2 * Math.PI * frequency * i / 48000;
                sin += sample * Math.Sin(phase); cos += sample * Math.Cos(phase);
            }
            return (sin * sin + cos * cos) / (count * (double)count);
        }
        public void Dispose() => GC.KeepAlive(callback);
    }
}
