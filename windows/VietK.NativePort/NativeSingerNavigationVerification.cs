using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeSingerNavigationVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    internal static async Task Run(Window host,NativePlayback playback,string root,string output)
    {
        var previous=host.Content;var directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-singer-check-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);
        try
        {
            var state=Path.Combine(directory,"local.db");var whole=Path.Combine(directory,"whole.db");
            using var database=new LocalSongDatabase(Path.Combine(root,"local-seed.db"),state);
            using(var catalogue=new LocalSongDatabase(Path.Combine(root,"local-seed.db"),whole)) { }
            using(var fixture=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=whole,Pooling=false }.ToString()))
            {
                fixture.Open();using var command=fixture.CreateCommand();
                command.CommandText="INSERT INTO tblSinger(SongsterID,SongsterName,SongsterPy,SongsterTypeID,singer_name_en,Pic_FileID_L) VALUES(9,'Fixture one','FO',1,'First Artist',42),(10,'Fixture two','FT',1,'Second Artist',43),(11,'Unreferenced fixture','UF',1,'Unreferenced',44)";command.ExecuteNonQuery();
            }
            using(var fixture=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=state,Pooling=false }.ToString()))
            {
                fixture.Open();using var transaction=fixture.BeginTransaction();
                for(var i=0;i<78;i++)
                {
                    using var command=fixture.CreateCommand();command.Transaction=transaction;
                    command.CommandText="INSERT INTO tblSong(SongID,SongName,SongPy,SongWord,songsterName,SongsterID1,SongsterID2,LanguageTypeID,PlayNum,hasRemote,IsLocalExist) VALUES($id,$name,$spell,2,$singer,$first,$second,8,$rank,1,1)";
                    command.Parameters.AddWithValue("$id",70000001+i);command.Parameters.AddWithValue("$name","Fixture song "+(i+1));command.Parameters.AddWithValue("$spell",i==0?"FA":"FX");
                    command.Parameters.AddWithValue("$singer",i==0?"Fixture one,Fixture two":i<72?"Fixture one":"Fixture two");
                    command.Parameters.AddWithValue("$first",i<72?9:10);command.Parameters.AddWithValue("$second",i==0?10:0);command.Parameters.AddWithValue("$rank",1000-i);command.ExecuteNonQuery();
                }
                transaction.Commit();
            }
            Require(database.Singers.Find("Fixture one") is null,"Seed unexpectedly contained fixture singers");
            Require(database.ImportReferencedSingers(whole)==2&&database.ImportReferencedSingers(whole)==0&&database.Singers.Find("Unreferenced fixture") is null,"Referenced-singer import included an unrelated row or replaced existing metadata");
            var contract=Read<SongBrowserContract>("song-browser.json");var more=Read<MoreContract>("more.json");var grid=Read<SongGridContract>("song-grid.json");
            var original=new SongBrowser(root,contract,more,database,grid) { Playback=playback };
            var bottomContract=Read<BottomContract>("bottom.json");var bottom=new BottomBar(root,bottomContract);
            var duet=new CatalogueSong(70000001,"Fixture duet","FA",2,"Fixture one,Fixture two",8,1000,1,"") { LocalState=1 };
            var originalPanel=original.CreateVerificationFixture(new[]{duet});Decorate(originalPanel);var originalView=new Viewbox { Child=originalPanel };host.Content=originalView;
            var selected=new HashSet<int>();var collected=new HashSet<int>();var actions=new List<(int Id,string Action)>();var home=0;
            var navigation=new OriginalSingerNavigation(root,contract,more,database,grid,()=>host,Decorate,()=>new(),()=>playback,()=>selected,()=>collected);
            original.SingerRequested+=name=>navigation.Open(name);navigation.SongActionRequested+=(song,action)=>actions.Add((song.Id,action));navigation.FragmentRequested+=fragment=> { Require(fragment==1,"Singer category requested the wrong original fragment");home++; };
            original.Input!.Letter("F");host.UpdateLayout();
            ClickSinger(originalPanel,"song-singers:70000001","Fixture two");
            Require(navigation.Active?.Singer?.Id==10&&navigation.Active.Results.Count==7,"Second duet span did not open the exact singer ID's songs");
            Require(actions.Count==0&&Application.Current.Windows.Count==2,"Singer click ordered a song or introduced a third window");
            bottom.SetConfirmedPlaybackState(true,true);bottom.SetConfirmedQueueCount(3);
            await ClickBack();Require(ReferenceEquals(host.Content,originalView)&&original.Input.Text=="F","Singer Back did not restore the previous view/input object");
            await Until(()=>originalPanel.IsLoaded,"Restored song panel did not load");
            Require(Descendants<FrameworkElement>(originalPanel).Single(element=>Equals(element.Tag,"play_imv")).Visibility==Visibility.Visible&&
                Descendants<FrameworkElement>(originalPanel).Single(element=>Equals(element.Tag,"pause_imv")).Visibility==Visibility.Hidden&&
                Descendants<TextBlock>(originalPanel).Single(element=>Equals(element.Tag,"original-queue-badge")).Text=="3","Back restored stale playback controls or queue count");
            Require(!navigation.Open("missing fixture")&&ReferenceEquals(host.Content,originalView),"Unknown singer replaced the screen");
            ClickSinger(originalPanel,"song-singers:70000001","Fixture one");
            var active=navigation.Active!;Require(active.Singer?.Id==9&&active.Results.Count==60,"Singer initial fetch did not retain the original 60-record page");
            var panel=((Viewbox)host.Content).Child as Canvas??throw new InvalidDataException("Singer panel missing");host.UpdateLayout();
            var scrolling=Descendants<ScrollViewer>(panel).Single();
            await Until(()=>panel.IsLoaded&&scrolling.IsLoaded&&scrolling.ViewportHeight>0,"Singer screen did not finish loading before the scroll gesture");
            scrolling.ScrollToVerticalOffset(600);
            try { await Until(()=>active.Results.Count==72,"Singer scrolling did not append the next original page"); }
            catch(InvalidDataException error) { throw new InvalidDataException($"{error.Message}; count={active.Results.Count}, offset={scrolling.VerticalOffset}, viewport={scrolling.ViewportHeight}, loaded={panel.IsLoaded}",error); }
            Require(active.Results.Select(song=>song.Id).Distinct().Count()==72&&active.Results.All(song=>song.Id<70000073),"Singer pagination leaked another singer or duplicated songs");
            active.Input!.Letter("F");active.Input.Letter("A");await Until(()=>active.Results.Count==1,"Singer search did not stay restricted to the active ID");
            Require(active.Results.Single().Id==70000001,"Singer spell search returned another title");
            host.UpdateLayout();var resultBody=(Canvas)Descendants<ScrollViewer>(panel).Single().Content;
            Require(Math.Abs(((FrameworkElement)resultBody.Children[0]).TranslatePoint(new Point(0,0),panel).Y-(contract.ContainerY+50))<1,"A short singer result list was centered instead of placed directly below the category");
            collected.Add(70000001);navigation.SetConfirmedCollectedSongs(collected);host.UpdateLayout();
            var favorite=Descendants<Image>(panel).Single(image=>Equals(image.Tag,"collect:70000001"));
            Require(favorite.Source is BitmapImage bitmap&&bitmap.UriSource.LocalPath==Path.GetFullPath(Path.Combine(root,grid.Icons["button_add_song_item_collected_normal"].File)),"Singer screen did not confirm the saved favorite state");
            await ClickElement(favorite);
            Require(actions.Single().Action=="collect","Singer favorite callback did not reach the common handler");
            Capture(panel,"original-singer-song-browser.png");
            ClickSinger(panel,"song-singers:70000001","Fixture two");Require(navigation.Active?.Singer?.Id==10,"Nested singer route failed");
            await ClickBack();Require(ReferenceEquals(navigation.Active,active)&&ReferenceEquals(((Viewbox)host.Content).Child,panel)&&active.Input.Text=="FA","Nested Back lost the previous singer/filter view");
            await ClickBack();Require(ReferenceEquals(host.Content,originalView),"Singer history did not return to its original screen");
            var profiles=new OriginalCollectionProfiles(directory);Require(profiles.Login("fixture","pass")==CollectionLoginResult.Success,"Fixture collection login failed");profiles.Toggle(70000001);
            var controls=new NativeCollectionControls(profiles,()=>((Viewbox)host.Content).Child as Canvas,_=>{});
            using var collection=new OriginalCollectionBrowser(root,profiles,id=>database.GetSongById(id),()=>new(),()=>[],controls,grid);
            collection.SingerRequested+=name=>navigation.Open(name);
            var collectionView=new Viewbox { Child=collection.Create() };host.Content=collectionView;host.UpdateLayout();
            ClickSinger((Canvas)collectionView.Child,"collection-singers:70000001","Fixture two");Require(navigation.Active?.Singer?.Id==10,"Collection duet span was not wired to the original singer route");
            await ClickBack();Require(ReferenceEquals(host.Content,collectionView),"Singer Back lost collection sidebar state");
            navigation.Open("Fixture one");host.UpdateLayout();
            await ClickElement(Descendants<StackPanel>((Viewbox)host.Content).Single(element=>Equals(element.Tag,"singer-category-home")));
            Require(home==1&&navigation.Active?.Singer?.Id==9,"Singer category did not request the original directory fragment");
            navigation.Clear();
            using(var fixture=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=state,Pooling=false }.ToString()))
            {
                fixture.Open();using var transaction=fixture.BeginTransaction();
                for(var index=0;index<91;index++)
                {
                    using var command=fixture.CreateCommand();command.Transaction=transaction;
                    command.CommandText="INSERT INTO tblSinger(SongsterID,SongsterName,SongsterPy,SongsterTypeID,SongsterOrderRank,singer_name_en,Pic_FileID_L) VALUES($id,$name,'ZZ',8,$rank,'Directory',0)";
                    command.Parameters.AddWithValue("$id",100+index);command.Parameters.AddWithValue("$name",index==0?"Fixture one":"Directory fixture "+index);
                    command.Parameters.AddWithValue("$rank",1000-index);command.ExecuteNonQuery();
                }
                using(var female=fixture.CreateCommand())
                { female.Transaction=transaction;female.CommandText="INSERT INTO tblSinger(SongsterID,SongsterName,SongsterPy,SongsterTypeID,SongsterOrderRank,singer_name_en,Pic_FileID_L) VALUES(200,'Directory female','DF',9,100,'Female',0)";female.ExecuteNonQuery(); }
                transaction.Commit();
            }
            var singerDirectory=new OriginalSingerDirectoryBrowser(root,contract,more,database,grid) { Playback=playback };
            singerDirectory.SingerRequested+=singer=>navigation.Open(singer);
            var directoryHome=0;singerDirectory.HomeRequested+=()=>directoryHome++;
            var directoryPanel=singerDirectory.Create();Decorate(directoryPanel);var directoryView=new Viewbox { Child=directoryPanel };host.Content=directoryView;
            await Until(()=>directoryPanel.IsLoaded,"Directory never loaded");host.UpdateLayout();
            Require(singerDirectory.LoadedSingers.Count==80&&Descendants<Border>(directoryPanel).Count(element=>element.Tag is string tag&&tag.StartsWith("singer-card:"))==8,"Directory did not display eight singers from its first SQL batch");
            var firstCard=Descendants<Border>(directoryPanel).Single(element=>Equals(element.Tag,"singer-card:100"));
            var secondCard=Descendants<Border>(directoryPanel).Single(element=>Equals(element.Tag,"singer-card:101"));
            Require(Canvas.GetLeft(firstCard)==Canvas.GetLeft(secondCard)&&Canvas.GetTop(secondCard)>Canvas.GetTop(firstCard),"Horizontal directory grid lost its column-first order");
            await ClickElement(Descendants<Border>(directoryPanel).Single(element=>Equals(element.Tag,"singer-country:1")));
            Require(singerDirectory.Country==1&&singerDirectory.TypePopup?.IsOpen==true,"Country tab did not filter singers and show sex popup");
            await ClickElement(Descendants<Border>(singerDirectory.TypePopup!.Child).Single(element=>Equals(element.Tag,"singer-sex:2")));
            Require(singerDirectory.Sex==2&&singerDirectory.LoadedSingers.Single().Id==200,"Sex popup did not apply the original Vietnam female type");
            await ClickElement(Descendants<Border>(singerDirectory.TypePopup.Child).Single(element=>Equals(element.Tag,"singer-sex:1")));
            singerDirectory.TypePopup.IsOpen=false;
            Require(singerDirectory.Sex==1&&singerDirectory.TotalPages==12,"Vietnam male paging count differs");
            for(var page=2;page<=8;page++)
                await ClickElement(Descendants<Border>(directoryPanel).Single(element=>Equals(element.Tag,"singer-page:icon_next_page.png")));
            Require(singerDirectory.CurrentPage==8&&singerDirectory.LoadedSingers.Count==91,"Directory did not prefetch its second SQL batch two pages before the boundary");
            Capture(directoryPanel,"original-singer-directory.png");
            singerDirectory.Input!.Letter("Z");singerDirectory.Input.Letter("Z");
            await Until(()=>singerDirectory.CurrentPage==1&&singerDirectory.LoadedSingers.Count==80,"Shared directory keyboard did not reset its initial batch");
            await ClickElement(Descendants<Border>(directoryPanel).Single(element=>Equals(element.Tag,"singer-card:100")));
            Require(navigation.Active?.Singer?.Id==100,"Directory resolved a duplicate singer name instead of its exact ID");
            await ClickBack();Require(ReferenceEquals(host.Content,directoryView)&&singerDirectory.Input.Text=="ZZ"&&singerDirectory.Country==1&&singerDirectory.Sex==1,"Directory Back lost its filter/input state");
            singerDirectory.Input.Clear();await Task.Delay(100);singerDirectory.Input.Letter("NOMATCH");
            await Until(()=>singerDirectory.LoadedSingers.Count==0,"Directory zero-result query did not finish");
            Require(singerDirectory.TotalPages==0,"Empty directory retained old page count");
            await ClickElement(Descendants<TextBlock>(directoryPanel).Single(element=>Equals(element.Tag,"singer-directory-home")));
            Require(directoryHome==1,"Directory title did not request Home");
            Require(Application.Current.Windows.Count==2,"Singer routing introduced an extra native window");
            File.WriteAllText(Path.Combine(output,"singer-navigation-verification.json"),JsonSerializer.Serialize(new {
                referencedSingerImport=true,individualDuetSpans=true,exactSingerMembership=true,originalInitialPageSize=true,
                continuousNextPage=true,singerScopedSearch=true,shortResultListTopAligned=true,confirmedFavoriteState=true,sharedActionCallback=true,
                unknownSingerNoOp=true,restoresPreviousViewAndInput=true,restoredFooterUsesCurrentState=true,nestedSingerBack=true,collectionSingerRoute=true,
                categoryDirectoryRequest=true,directoryScreen=true,directoryEightCards=true,directoryColumnFirst=true,
                directoryCountrySexFilters=true,directoryBatchPrefetch=true,directoryExactIdRoute=true,directoryRetainedState=true,
                directoryKeyboard=true,directoryZeroResults=true,twoWindows=true,manufacturerSingerPictures=false,directoryPopupArtwork=false
            },new JsonSerializerOptions { WriteIndented=true }));
            T Read<T>(string file)=>JsonSerializer.Deserialize<T>(File.ReadAllText(Path.Combine(root,file)),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            void Decorate(Canvas canvas) { var bar=bottom.Create();Canvas.SetTop(bar,bottomContract.Y);canvas.Children.Add(bar); }
            async Task ClickBack()
            {
                host.UpdateLayout();var back=Descendants<Border>((Viewbox)host.Content).Single(element=>Equals(element.Tag,"singer-song-back"));
                await ClickElement(back);
            }
            async Task ClickElement(FrameworkElement element)
            {
                host.Activate();host.UpdateLayout();var point=element.PointToScreen(new Point(element.ActualWidth/2,element.ActualHeight/2));
                Require(SystemParameters.WorkArea.Contains(point)&&SetCursorPos((int)point.X,(int)point.Y),"Singer fixture control lay outside the desktop");await Task.Delay(70);
                element.RaiseEvent(new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice,Environment.TickCount,System.Windows.Input.MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            }
            void Capture(Canvas canvas,string file)
            {
                host.UpdateLayout();var bitmap=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);bitmap.Render(canvas);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,file));encoder.Save(stream);
            }
        }
        finally
        {
            host.Content=previous;
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-singer-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected singer fixture cleanup path");
            Directory.Delete(directory,true);
        }
    }
    private static void ClickSinger(Canvas panel,string tag,string name)
    {
        var text=Descendants<TextBlock>(panel).Single(element=>Equals(element.Tag,tag));
        text.Inlines.OfType<Hyperlink>().Single(link=>Equals(link.Tag,name)).RaiseEvent(new RoutedEventArgs(Hyperlink.ClickEvent));
    }
    private static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
    private static async Task Until(Func<bool> condition,string message)
    { var deadline=DateTime.UtcNow.AddSeconds(5);while(!condition()&&DateTime.UtcNow<deadline)await Task.Delay(50);Require(condition(),message); }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        { var child=VisualTreeHelper.GetChild(parent,i);if(child is T element)yield return element;foreach(var nested in Descendants<T>(child))yield return nested; }
    }
}
