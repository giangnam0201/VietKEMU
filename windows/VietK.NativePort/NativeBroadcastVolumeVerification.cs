using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeBroadcastVolumeVerification
{
    internal static async Task Run(Window host,string root,string fixtures,string output)
    {
        var directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-idle-volume-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(directory);
        var previous=host.Content;
        try
        {
            var contract=JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root,"bottom.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var songDefault=new OriginalDefaultVolumeSettings(directory);songDefault.Save(7);
            var saved=new OriginalBroadcastVolumeSettings(directory);saved.Save(3,false);
            var fixture=Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"));
            using(var playback=new NativePlayback(new BottomBar(root,contract),directory))
            {
                using var tap=new NativePlaybackVerification.PcmTap(playback.Decoder.Native);
                playback.SetIdleVideo(fixture);await Ready(playback,15);
                Require(playback.Source==PlaybackSource.Idle&&playback.Decoder.OutputVolumeStep==3&&playback.Decoder.AppliedVolumePercent==15,"Idle did not use separate configured gain");
                var idlePower=await NativePlaybackVerification.UntilPower(tap,440,value=>value>1e-12,"Configured idle gain produced no PCM");
                playback.BroadcastVolumeSettings.Save(10,true);
                Require(playback.Decoder.OutputVolumeStep==3&&!playback.BroadcastSessionMuted,"Saved idle settings took effect before restart");
                await NativePlaybackVerification.UntilPower(tap,440,value=>Near(value,idlePower),"Saving restart-only settings changed idle PCM");
                playback.Command("volinc");Require(playback.BroadcastSessionVolume==4&&playback.Decoder.OutputVolumeStep==4&&playback.Decoder.AppliedVolumePercent==20,"Idle increment did not affect its session gain");
                var louderIdlePower=await NativePlaybackVerification.UntilPower(tap,440,value=>value>idlePower*1.15,"Idle increment did not increase actual PCM");
                Require(new OriginalBroadcastVolumeSettings(directory).Volume==10,"Idle increment overwrote configured gain");
                Require(playback.PlayMedia(fixture,preserveStereo:true),"Song transition rejected fixture");await Ready(playback,35);
                Require(playback.Decoder.OutputVolumeStep==7&&playback.Decoder.AppliedVolumePercent==35,"Idle gain leaked into next song");
                var songPower=await NativePlaybackVerification.UntilPower(tap,440,value=>value>louderIdlePower*1.15,"Song transition did not restore actual song PCM gain");
                playback.Command("voldec");Require(playback.Decoder.OutputVolumeStep==6,"Song volume decrement failed");
                var quieterSongPower=await NativePlaybackVerification.UntilPower(tap,440,value=>value<songPower*.95&&value>1e-12,"Song decrement did not reduce actual PCM");
                Require(playback.StartIdleDemo(),"Return to idle failed");await Ready(playback,20);
                Require(playback.Decoder.OutputVolumeStep==4,"Idle loop lost session volume");
                await NativePlaybackVerification.UntilPower(tap,440,value=>Near(value,louderIdlePower),"Idle return lost actual session PCM gain");
                playback.BroadcastVolumeSettings.Save(8,false);
                var panel=new Canvas { Width=1280,Height=800,Background=Brushes.Black };host.Content=new Viewbox { Child=panel };
                var dialog=new OriginalBroadcastVolumeDialog(panel,playback.BroadcastVolumeSettings);host.UpdateLayout();
                Click(dialog,"volume_decrease");Require(dialog.Pending==7&&playback.BroadcastVolumeSettings.Volume==8,"Dialog decrement committed before dismissal");
                Click(dialog,"mute");Require(dialog.Muted&&dialog.Pending==0&&!dialog.SeekBar.IsEnabled,"Mute did not zero and disable the slider");
                Click(dialog,"volume_increase");Require(dialog.Pending==0,"Muted dialog allowed gain changes");
                Click(dialog,"mute");Require(!dialog.Muted&&dialog.Pending==0&&dialog.SeekBar.IsEnabled,"Unmute restored an invented old gain");dialog.Change(9);host.UpdateLayout();
                var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);frame.Render(panel);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using(var file=File.Create(Path.Combine(output,"synthetic-broadcast-volume-dialog.png")))encoder.Save(file);
                dialog.Overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
                Require(!panel.Children.Contains(dialog.Overlay)&&playback.BroadcastVolumeSettings.Volume==9&&playback.Decoder.OutputVolumeStep==4,"Dismiss did not save draft or changed live idle audio");
                await NativePlaybackVerification.UntilPower(tap,440,value=>Near(value,louderIdlePower),"Dialog dismissal changed idle PCM before restart");
                Require(playback.PlayMedia(fixture,preserveStereo:true),"Song resume after idle rejected");await Ready(playback,30);Require(playback.Decoder.OutputVolumeStep==6,"Song session gain lost across idle playback");
                await NativePlaybackVerification.UntilPower(tap,440,value=>Near(value,quieterSongPower),"Idle return lost actual song session PCM gain");
            }
            saved.Save(10,true);
            using(var restarted=new NativePlayback(new BottomBar(root,contract),directory))
            {
                using var tap=new NativePlaybackVerification.PcmTap(restarted.Decoder.Native);
                Require(restarted.StartIdleDemo(),"Muted restart idle rejected");await Ready(restarted,0);
                Require(restarted.BroadcastSessionMuted&&restarted.Decoder.OutputVolumeStep==0&&restarted.Decoder.AppliedVolumePercent==0,"Restart did not apply idle mute");
                await NativePlaybackVerification.UntilPeak(tap,value=>value<1e-6,"Configured idle mute did not silence actual PCM");
                restarted.Command("volinc");Require(restarted.Decoder.OutputVolumeStep==0&&restarted.BroadcastSessionVolume==10,"Muted idle allowed volume adjustment");
                Require(restarted.VolumeUnavailableReason.Length>0,"Muted idle did not expose its adjustment restriction");
                Require(restarted.PlayMedia(fixture,preserveStereo:true),"Muted idle song transition rejected");await Ready(restarted,35);
                Require(restarted.Decoder.OutputVolumeStep==7&&restarted.Decoder.AppliedVolumePercent==35,"Idle mute affected next song");
                Require(restarted.VolumeUnavailableReason.Length==0,"Song retained idle adjustment restriction");
                await NativePlaybackVerification.UntilPower(tap,440,value=>value>1e-12,"Idle mute left next song PCM silent");
            }
            File.WriteAllText(Path.Combine(output,"broadcast-volume-verification.json"),JsonSerializer.Serialize(new {
                separateIdleGainApplied=true,savedSettingsWaitForRestart=true,idleSessionChangesDoNotOverwriteConfig=true,
                songGainPreservedAcrossIdle=true,dialogMuteZerosAndDisablesSlider=true,unmuteLeavesZero=true,dismissSavesWithoutChangingLiveGain=true,
                actualPcmGainAndMuteVerified=true,restartAppliesIdleMute=true,mutedIdleBlocksAdjustment=true,nextSongRestoresDefault=true
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally
        {
            host.Content=previous;
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-idle-volume-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected idle fixture cleanup");
            Directory.Delete(directory,true);
        }
    }
    private static async Task Ready(NativePlayback playback,int expectedVolume)
    {
        var deadline=DateTime.UtcNow.AddSeconds(12);
        while(playback.Player.State!=OriginalVideoState.Play||playback.Decoder.Position<=0||playback.Decoder.AppliedVolumePercent!=expectedVolume) { if(DateTime.UtcNow>=deadline)throw new TimeoutException($"Idle-volume output not ready: source={playback.Source}, state={playback.Player.State}, step={playback.Decoder.OutputVolumeStep}, applied={playback.Decoder.AppliedVolumePercent}, appMuted={playback.Decoder.Muted}, nativeMuted={playback.Decoder.Native.Mute}, native={playback.Decoder.Native.Volume}, expected={expectedVolume}, tracks={playback.Decoder.Native.AudioTrackDescription.Count(t=>t.Id>=0)}");await Task.Delay(50); }
    }
    private static void Click(OriginalBroadcastVolumeDialog dialog,string name)=>Descendants(dialog.Overlay).Single(x=>Equals(x.Tag,"broadcast-volume:"+name)).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
    private static IEnumerable<FrameworkElement> Descendants(DependencyObject parent)
    { for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement element)yield return element;foreach(var next in Descendants(child))yield return next; } }
    private static bool Near(double value,double reference)=>value>=reference*.8&&value<=reference*1.2;
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
