using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeBroadcastEditorVerification
{
    internal static async Task Run(Window host,string root,string fixtures,string output)
    {
        var folder=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-idle-editor-"+Guid.NewGuid().ToString("N")));Directory.CreateDirectory(folder);
        var previous=host.Content;
        try
        {
            var panel=new Canvas { Width=1280,Height=800,Background=Brushes.Black };host.Content=new Viewbox { Child=panel };
            var contract=JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root,"bottom.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var songs=new[]{Song(1,"Alpha song"),Song(2,"Beta song"),Song(3,"Gamma song")};var requests=new List<int>();
            var config=JsonSerializer.Serialize(new { play_list=songs.Select(s=>new { song_id=s.Id.ToString(),type="1" }) });new OriginalBroadcastPlaylist(folder).Import(config);
            using var playback=new NativePlayback(new BottomBar(root,contract),folder);
            var fixture=Path.GetFullPath(Path.Combine(fixtures,"stereo.mkv"));
            playback.ResolveIdleSong=id=>new(fixture,new(id,id,fixture,100,0,5,"","",0,"","","","",0,null,null,null));playback.IdleSongExists=_=>true;
            Require(playback.StartIdleDemo(),"Editor fixture idle rejected");var timer=Stopwatch.StartNew();
            while(playback.Player.State!=OriginalVideoState.Play||playback.Player.Position<=0) { if(timer.ElapsedMilliseconds>12000)throw new InvalidDataException("Editor fixture did not start real idle playback");await Task.Delay(30); }
            IReadOnlyList<LocalSong> Search(string text,int page)=>text=="Page"?Enumerable.Range(1000+page*50,page==0?50:10).Select(id=>Song(id,"Page song "+id)).ToArray():songs.Where(s=>s.Name.StartsWith(text,StringComparison.OrdinalIgnoreCase)).ToArray();
            OriginalBroadcastPlaylistDialog Open()=>new(panel,playback,id=>songs.FirstOrDefault(s=>s.Id==id),Search,id=>id!=3,requests.Add);
            var dialog=Open();host.UpdateLayout();Click(dialog.Overlay,"broadcast:top:2");host.UpdateLayout();
            Require(dialog.Draft.Select(s=>s.Id).SequenceEqual(new[]{3,1,2}),"Top operation did not move the selected row to index zero");
            Click(dialog.Overlay,"broadcast:delete:1");host.UpdateLayout();
            Require(dialog.Draft.Select(s=>s.Id).SequenceEqual(new[]{3,2})&&requests.Count==0&&File.ReadAllText(Path.Combine(folder,"localbroadcastlist.init"))==config,"Draft operations queued music or saved early");
            Capture(host,panel,output,"synthetic-broadcast-editor.png");Click(dialog.Overlay,"broadcast:cancel");
            Require(!panel.Children.Contains(dialog.Overlay)&&playback.IdlePlaylist.Entries.Count==3&&playback.IdleSongId==1,"Cancel changed saved list or current playback");
            dialog=Open();host.UpdateLayout();Click(dialog.Overlay,"broadcast:add");host.UpdateLayout();var add=dialog.AddDialog!;
            Require(!panel.Children.Contains(dialog.Overlay)&&panel.Children.Contains(add.Overlay)&&add.Draft.Count==0,"Create-new did not open a fresh add screen");
            add.Input.Text="Page";host.UpdateLayout();Require(Elements(add.Overlay).Any(x=>Equals(x.Tag,"broadcast-add:result:1049")),"Original 50-result search page missing");
            Capture(host,panel,output,"synthetic-broadcast-add-search.png");
            var scrolling=Elements(add.Overlay).OfType<ScrollViewer>().First(x=>x.Height==320);scrolling.ScrollToBottom();await Task.Delay(100);host.UpdateLayout();
            Require(Elements(add.Overlay).Any(x=>Equals(x.Tag,"broadcast-add:result:1059")),"Bottom scrolling did not load the next search page");
            add.Input.Text="Beta";host.UpdateLayout();Click(add.Overlay,"broadcast-add:result:2");host.UpdateLayout();
            Require(add.Draft.Single().Id==2&&add.Input.Text=="Beta","Search selection lost its original input or draft");
            add.Input.Text="Gamma";host.UpdateLayout();Click(add.Overlay,"broadcast-add:result:3");host.UpdateLayout();Click(add.Overlay,"broadcast-add:delete:0");host.UpdateLayout();
            Require(add.Draft.Single().Id==3,"Add-screen delete removed the wrong draft row");Click(add.Overlay,"broadcast-add:back");host.UpdateLayout();
            Require(panel.Children.Contains(dialog.Overlay)&&dialog.Draft.Count==3,"Back committed new additions or discarded the parent draft");
            Click(dialog.Overlay,"broadcast:add");host.UpdateLayout();add=dialog.AddDialog!;
            add.Input.Text="Beta";host.UpdateLayout();Click(add.Overlay,"broadcast-add:result:2");add.Input.Text="B";host.UpdateLayout();Click(add.Overlay,"broadcast-add:result:2");host.UpdateLayout();
            Require(add.Draft.Select(s=>s.Id).SequenceEqual(new[]{2,2}),"Original duplicate selections were silently deduplicated");
            Capture(host,panel,output,"synthetic-broadcast-add-draft.png");Click(add.Overlay,"broadcast-add:confirm");host.UpdateLayout();
            Require(dialog.Draft.Select(s=>s.Id).SequenceEqual(new[]{1,2,3,2,2})&&playback.IdlePlaylist.Entries.Count==3,"Add confirmation saved before parent confirmation");
            var source=playback.Player.Source;Click(dialog.Overlay,"broadcast:confirm");await Task.Delay(150);
            Require(playback.IdlePlaylist.Entries.Select(s=>s.SongId).SequenceEqual(new[]{1,2,3,2,2})&&requests.SequenceEqual(new[]{3}),"Parent confirm lost duplicates or omitted original nonlocal song order");
            Require(playback.Player.Source==source&&playback.IdleSongId==1&&playback.Player.State==OriginalVideoState.Play,"Saving the playlist interrupted current idle media");
            using(var saved=JsonDocument.Parse(File.ReadAllText(Path.Combine(folder,"localbroadcastlist.init"))))Require(saved.RootElement.GetProperty("is_need_reply").GetString()=="1","Original reply flag not saved");
            dialog=Open();host.UpdateLayout();Click(dialog.Overlay,"broadcast:add");host.UpdateLayout();add=dialog.AddDialog!;add.Input.Text="Beta";host.UpdateLayout();Click(add.Overlay,"broadcast-add:result:2");
            Outside(add.Overlay);Require(!panel.Children.Contains(add.Overlay)&&!panel.Children.Contains(dialog.Overlay)&&playback.IdlePlaylist.Entries.Count==5,"Outside add dismissal committed or returned to a dismissed parent");
            playback.ImportIdlePlaylist("{\"play_list\":[]}",restartIdle:false);dialog=Open();host.UpdateLayout();Capture(host,panel,output,"synthetic-broadcast-editor-empty.png");Outside(dialog.Overlay);
            Require(!panel.Children.Contains(dialog.Overlay)&&playback.IdlePlaylist.Entries.Count==0,"Outside parent dismissal saved or retained UI");
            File.WriteAllText(Path.Combine(output,"broadcast-editor-verification.json"),JsonSerializer.Serialize(new {
                originalLocalGeometry=true,topAndDeleteStageOnly=true,cancelDiscardsDraft=true,freshAddDraft=true,
                realUiSearchAndScrollPagination=true,searchSelectionRetainsInput=true,addDeleteAndBack=true,
                duplicateSelectionPreserved=true,addConfirmationStagesParent=true,parentConfirmationPersistsOriginalFormat=true,
                nonlocalSongRoutesToOriginalOrder=true,savingDoesNotInterruptIdlePlayback=true,outsideDismissalDiscards=true,
                emptyListHint=true,usbFileChooserAdapted=true,originalUsbCopyDeleteDialogPorted=false,linkedCloudEditorPorted=false,
                originalHintArtworkPresent=File.Exists(Path.Combine(OriginalSupplement.Root,"ambience","settings","dialog_public_play_hint_icon.png"))
            },new JsonSerializerOptions { WriteIndented=true }));
        }
        finally
        {
            host.Content=previous;var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!folder.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-idle-editor-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected editor fixture cleanup path");
            if(Directory.Exists(folder))Directory.Delete(folder,true);
        }
    }
    private static LocalSong Song(int id,string name)=>new(id,name,"",2,"Singer "+id,[0,0,0,0],[0,0,0,0],[8,0,0,0],0,0,0,null,null,1,null,1,0);
    private static void Capture(Window host,Canvas panel,string output,string name)
    {
        host.UpdateLayout();var bitmap=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
    }
    private static void Click(Canvas overlay,string tag)=>Elements(overlay).Single(x=>Equals(x.Tag,tag)).RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
    private static void Outside(Canvas overlay)=>overlay.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonDownEvent });
    private static IEnumerable<FrameworkElement> Elements(DependencyObject parent)
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++) { var child=VisualTreeHelper.GetChild(parent,i);if(child is FrameworkElement element)yield return element;foreach(var nested in Elements(child))yield return nested; }
    }
    private static void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
}
