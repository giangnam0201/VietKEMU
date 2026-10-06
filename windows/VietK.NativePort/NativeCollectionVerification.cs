using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

internal static class NativeCollectionVerification
{
    [DllImport("user32.dll")]private static extern bool SetCursorPos(int x,int y);
    public static async Task Run(Window host,string root,string output)
    {
        static void Require(bool condition,string message) { if(!condition)throw new InvalidDataException(message); }
        var directory=Path.GetFullPath(Path.Combine(Path.GetTempPath(),"vietk-native-collection-"+Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(directory);var previous=host.Content;
        var panel=new Canvas { Width=1280,Height=800,Background=new ImageBrush(new BitmapImage(new Uri(Path.Combine(root,"main_bg.jpg")))) };
        host.Content=new Viewbox { Child=panel };
        try
        {
            var model=new OriginalCollectionProfiles(directory);
            var contract=JsonSerializer.Deserialize<SongGridContract>(File.ReadAllText(Path.Combine(root,"song-grid.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
            var grid=new SongGrid(root,contract);ScrollViewer? view=null;
            var fixture=new CatalogueSong(1,"Collection fixture","CF",2,"Fixture Singer",8,1,1,"") { LocalState=1 };
            void Refresh(IReadOnlySet<int> ids)
            {
                if(view is not null)panel.Children.Remove(view);
                view=grid.Create(new[]{fixture},collected:ids);panel.Children.Add(view);Canvas.SetLeft(view,35);Canvas.SetTop(view,150);
            }
            var controls=new NativeCollectionControls(model,()=>panel,Refresh);
            grid.ActionRequested+=(song,action)=> { if(action=="collect")controls.Collect(song.Id); };
            Image Favorite()=>Descendants<Image>(panel).Single(image=>Equals(image.Tag,"collect:1"));
            async Task ClickFavorite()
            {
                host.Activate();host.UpdateLayout();var icon=Favorite();var point=icon.PointToScreen(new Point(icon.ActualWidth/2,icon.ActualHeight/2));
                Require(SetCursorPos((int)point.X,(int)point.Y),"Could not position fixture pointer");await Task.Delay(70);
                icon.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
            }
            void Confirm(string user,string password)
            {
                Require(controls.Username is not null&&controls.Password is not null&&controls.Confirm is not null,"Original collection dialog is missing");
                controls.Username!.Text=user;controls.Password!.Password=password;
                controls.Confirm!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }
            await ClickFavorite();Require(controls.Username is not null&&!model.Contains(1),"Favorite icon did not require login");
            var windows=Application.Current.Windows.Count;
            Require(windows==2,"Collection login opened a third window");Capture("original-collection-login.png");
            Confirm("tiny","a");Require(controls.Username is not null&&!model.Contains(1),"Invalid login confirmed pending favorite");
            Confirm("fixture","fixture");Require(model.Contains(1)&&controls.Username is null,"Original login did not retry pending favorite");
            host.UpdateLayout();
            Require(((BitmapImage)Favorite().Source).UriSource.LocalPath==Path.GetFullPath(Path.Combine(root,contract.Icons["button_add_song_item_collected_normal"].File)),"Favorite icon did not reflect confirmed profile");
            Require(new OriginalCollectionProfiles(directory).Contains(1),"Native favorite was not persisted");Capture("original-collection-confirmed.png");
            Require(Descendants<Border>(panel).Count(border=>Equals(border.Tag,"collection-feedback"))==1&&controls.LastFeedback=="Sưu tập thành công","Collection feedback overlapped an earlier validation message");
            controls.Logout();await ClickFavorite();Confirm("fixture","fixture");
            Require(model.Contains(1)&&new OriginalCollectionProfiles(directory).Contains(1),"Pending add after login removed an existing profile favorite");
            await ClickFavorite();Require(!model.Contains(1)&&new OriginalCollectionProfiles(directory).Snapshot().Count==0,"Favorite icon did not remove persisted song");
            controls.Logout();await ClickFavorite();controls.Close();Require(!model.Contains(1),"Cancel collected a pending song");
            controls.Login();Confirm("fixture","wrong");Require(model.CurrentUser.Length==0&&controls.Username is not null&&controls.Username.Text.Length==0&&controls.Password!.Password.Length==0,"Failed login changed session or did not clear fields");
            Confirm("fixture","fixture");Require(model.CurrentUser=="fixture"&&model.Snapshot().Count==0,"Existing native profile login failed");
            await VerifyBrowser();
            File.WriteAllText(Path.Combine(output,"collection-verification.json"),JsonSerializer.Serialize(new { actualGridIcon=true,pendingFavoriteAfterLogin=true,pendingAddPreservesExistingFavorite=true,confirmedIcon=true,persistedAddRemove=true,cancelPreservesCollection=true,wrongPasswordClearsFields=true,singleFeedbackMessage=true,twoWindows=true,fullCollectionFragment=false },new JsonSerializerOptions { WriteIndented=true }));
            void Capture(string name)
            {
                panel.UpdateLayout();panel.Measure(new Size(1280,800));panel.Arrange(new Rect(0,0,1280,800));
                var bitmap=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);bitmap.Render(panel);
                var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
            }
            async Task VerifyBrowser()
            {
                var songs=Enumerable.Range(1,12).ToDictionary(id=>id,id=>new LocalSong(id,$"Fixture song {id}","F",2,"Fixture Singer",[],[0],[8],0,0,0,null,null,1,null,id==10?0:1,id==11?1:0));
                for(var id=1;id<=12;id++)Require(model.Toggle(id)==CollectionToggleResult.Added,"Browser fixture favorite failed");
                model.Logout();var connected=false;
                var orders=new List<SelectedPlaylistItem> { new(songs[1],1,null,null),new(songs[2],2,null,null) };
                Canvas? browserPanel=null;var browserControls=new NativeCollectionControls(model,()=>browserPanel,_=>{});
                using var browser=new OriginalCollectionBrowser(root,model,id=>songs.GetValueOrDefault(id),()=>new(true,connected),()=>orders,browserControls,contract);
                var actions=new List<(int Id,string Action)>();browser.ActionRequested+=(song,action)=>actions.Add((song.Id,action));
                var home=0;browser.HomeRequested+=()=>home++;
                void Show()
                { browserPanel=browser.Create();host.Content=new Viewbox { Child=browserPanel };host.UpdateLayout(); }
                async Task Click(FrameworkElement target)
                {
                    host.Activate();host.UpdateLayout();var point=target.PointToScreen(new Point(target.ActualWidth/2,target.ActualHeight/2));
                    Require(SetCursorPos((int)point.X,(int)point.Y),"Could not position browser fixture pointer");await Task.Delay(70);
                    Require(new Rect(0,0,target.ActualWidth,target.ActualHeight).Contains(Mouse.GetPosition(target)),"Browser fixture pointer is outside "+target.Tag);
                    target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,Environment.TickCount,MouseButton.Left) { RoutedEvent=UIElement.MouseLeftButtonUpEvent });
                }
                async Task Login(string user,string password)
                {
                    browser.Username!.Text=user;browser.Password!.Password=password;
                    browser.LoginButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Require(browser.LoginProgress!.Visibility==Visibility.Visible&&browser.LoginButton.Visibility==Visibility.Hidden,"Inline collection login did not display its progress state");
                    var limit=Environment.TickCount64+3000;
                    while(browser.LoginProgress.Visibility==Visibility.Visible&&Environment.TickCount64<limit)await Task.Delay(20);
                    Require(browser.LoginProgress.Visibility!=Visibility.Visible,"Inline login did not finish");host.UpdateLayout();
                }
                void BrowserCapture(string name)
                {
                    host.UpdateLayout();var frame=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);frame.Render(browserPanel!);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(frame));using var file=File.Create(Path.Combine(output,name));encoder.Save(file);
                }
                FrameworkElement Tagged(string tag)=>Descendants<FrameworkElement>(browserPanel!).Single(element=>Equals(element.Tag,tag));
                Show();await Task.Delay(300);
                Require(browser.Rows.Count==0&&Descendants<TextBlock>(browserPanel!).Any(text=>text.Text=="Danh sách rỗng"),"Logged-out collection exposed songs");
                BrowserCapture("original-collection-browser-logged-out.png");
                await Login("fixture","wrong");Require(model.CurrentUser.Length==0&&browser.Username!.Text.Length==0&&browser.Password!.Password.Length==0,"Inline wrong password did not reset the account panel");
                await Login("fixture","fixture");
                Require(browser.Rows.Select(song=>song.Id).SequenceEqual(new[]{1,2,3,4,5,6,7,8,9,12}),"Collection browser did not use local/PSL visibility in stored order");
                Require(browser.Username!.Visibility==Visibility.Visible&&browser.Username.Parent is Border { Visibility:Visibility.Hidden }&&Equals(browser.LoginButton!.Content,"Thoát ra"),"Logged-in collection did not retain hidden field layout/logout");
                var first=(Canvas)Tagged("collection-row:1");var second=(Canvas)Tagged("collection-row:2");
                Require(first.Width==744&&first.Height==74&&first.TranslatePoint(new Point(),browserPanel!).X==second.TranslatePoint(new Point(),browserPanel!).X,"Collection used a grid instead of the active full-width row layout");
                Require(Descendants<TextBlock>(first).Any(text=>text.Text=="[Đang phát]")&&Descendants<TextBlock>(second).Any(text=>text.Text=="[Đặt trước 1]"),"Original collection queue labels differ");
                await Click(Tagged("collection-top:2"));await Click(first);
                Require(actions.SequenceEqual(new[]{(2,"top"),(1,"order")}),"Collection action icon ordered twice or dispatched the wrong song");
                Require(Descendants<Canvas>(browserPanel!).Count(canvas=>Equals(canvas.Tag,"collection-order-animation"))==2,"Original collection order animations did not appear");
                await Task.Delay(600);Require(!Descendants<Canvas>(browserPanel!).Any(canvas=>Equals(canvas.Tag,"collection-order-animation")),"Completed collection order animation was not removed");
                Require(model.Contains(2),"Collection fixture lost its favorite before the removal click");
                await Click(Tagged("collection-favorite:2"));
                Require(!model.Contains(2)&&browser.Rows.Any(song=>song.Id==2),$"Collection removal: saved={model.Contains(2)}, row={browser.Rows.Any(song=>song.Id==2)}, feedback={browserControls.LastFeedback}");
                host.UpdateLayout();Require(((BitmapImage)((Image)Tagged("collection-favorite:2")).Source).UriSource.LocalPath==Path.GetFullPath(Path.Combine(root,contract.Icons["button_add_song_item_collect"].File)),"Removed collection row retained the filled star");
                await Click(Tagged("collection-favorite:2"));Require(model.Contains(2),"Retained collection row could not be collected again");
                var scroller=(ScrollViewer)Tagged("collection-scroll");scroller.ScrollToEnd();host.UpdateLayout();await Task.Delay(50);
                Require(scroller.VerticalOffset>200,"Continuous collection scrolling did not reach remaining songs");
                BrowserCapture("original-collection-browser-scrolled.png");
                scroller.ScrollToTop();host.UpdateLayout();BrowserCapture("original-collection-browser-logged-in.png");
                connected=true;orders.Clear();orders.Add(new(songs[10],1,null,null));Show();await Task.Delay(300);
                Require(browser.Rows.Any(song=>song.Id==10)&&Descendants<TextBlock>((Canvas)Tagged("collection-row:10")).Any(text=>text.Text=="[Đặt trước 1]"),"Connected remote visibility/online-head queue offset differs");
                await Click((FrameworkElement)Tagged("collection-back"));Require(home==1,"Original collection back route is missing");
                var homeContract=JsonSerializer.Deserialize<HomeContract>(File.ReadAllText(Path.Combine(root,"home.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
                var moreContract=JsonSerializer.Deserialize<MoreContract>(File.ReadAllText(Path.Combine(root,"more.json")),new JsonSerializerOptions { PropertyNameCaseInsensitive=true })!;
                var more=new MoreScreen(root,moreContract,homeContract);var route=-1;more.NavigationRequested+=value=>route=value;
                browserPanel=more.Create();host.Content=new Viewbox { Child=browserPanel };await Click(Tagged("fl_favorite"));Require(route==14,"More favorite tile did not request original fragment 14");
                Show();browser.LoginButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Require(model.CurrentUser.Length==0&&browser.Rows.Count==0&&Equals(browser.LoginButton.Content,"đăng nhập"),"Collection sidebar logout did not reset rows/fields");
                Require(Application.Current.Windows.Count==2,"Collection browsing introduced a third window");
                File.WriteAllText(Path.Combine(output,"collection-browser-verification.json"),JsonSerializer.Serialize(new { originalMoreRoute=true,inlineLoginProgress=true,wrongPasswordResetsFields=true,activeSingleColumnRows=true,originalQueueLabels=true,originalOnlineHeadOffset=true,orderAndTopCallbacks=true,orderAnimationsAppearAndFinish=true,removeRetainsRowUntilReload=true,recollectRetainedRow=true,continuousScroll=true,originalVisibilityAndPslFilter=true,backAndLogout=true,twoWindows=true,loginArtworkAvailable=browser.LoginArtworkAvailable,manufacturerDownloadsVerified=false,previewAndSingerHandlersPorted=false },new JsonSerializerOptions { WriteIndented=true }));
                host.Content=new Viewbox { Child=panel };
            }
        }
        finally
        {
            host.Content=previous;
            var temp=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!directory.StartsWith(temp,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-native-collection-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected fixture cleanup path");
            Directory.Delete(directory,true);
        }
    }
    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T:DependencyObject
    {
        for(var i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
        {
            var child=VisualTreeHelper.GetChild(parent,i);if(child is T match)yield return match;
            foreach(var descendant in Descendants<T>(child))yield return descendant;
        }
    }
}
