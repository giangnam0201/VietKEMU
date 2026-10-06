using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using VietK.Core;

namespace VietK.NativePort;

public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        try
        {
            var root = Path.Combine(AppContext.BaseDirectory, "Original");
            var contract = JsonSerializer.Deserialize<HomeContract>(File.ReadAllText(Path.Combine(root, "home.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original home contract");
            var app = new Application();
            var renderer = new HomeScreen(root, contract);
            var bottomContract = JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root, "bottom.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original bottom bar contract");
            var bottom = new BottomBar(root, bottomContract);
            var moreContract = JsonSerializer.Deserialize<MoreContract>(File.ReadAllText(Path.Combine(root, "more.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original More screen contract");
            var more = new MoreScreen(root, moreContract, contract);
            var songContract = JsonSerializer.Deserialize<SongBrowserContract>(File.ReadAllText(Path.Combine(root, "song-browser.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original song browser contract");
            var capturing = args.Length == 2 && args[0] == "--capture";
            var stateDirectory = capturing ? args[1] : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VietKNativePort");
            using var songState = new LocalSongDatabase(Path.Combine(root,"local-seed.db"),
                Path.Combine(stateDirectory,"song-browser-state.db"));
            var gridContract=JsonSerializer.Deserialize<SongGridContract>(File.ReadAllText(Path.Combine(root,"song-grid.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original song grid contract");
            var browser = new SongBrowser(root, songContract, moreContract, songState,gridContract);
            Canvas Panel(int screen = 0)
            {
                var panel = screen switch { 38 => more.Create(), 2 => browser.Create(), _ => renderer.Create() };
                var bar = bottom.Create();
                Canvas.SetTop(bar, bottomContract.Y); panel.Children.Add(bar);
                return panel;
            }
            if (args.Length == 2 && args[0] == "--capture")
            {
                Directory.CreateDirectory(args[1]);
                var canvas = Panel();
                canvas.Measure(new Size(1280, 800));
                canvas.Arrange(new Rect(0, 0, 1280, 800));
                canvas.UpdateLayout();
                var image = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                image.Render(canvas);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                using (var file = File.Create(Path.Combine(args[1], "native-home.png"))) encoder.Save(file);
                var moreCanvas = Panel(38);
                moreCanvas.Measure(new Size(1280, 800)); moreCanvas.Arrange(new Rect(0, 0, 1280, 800)); moreCanvas.UpdateLayout();
                var moreImage = new RenderTargetBitmap(1280, 800, 96, 96, PixelFormats.Pbgra32);
                moreImage.Render(moreCanvas);
                var moreEncoder = new PngBitmapEncoder(); moreEncoder.Frames.Add(BitmapFrame.Create(moreImage));
                using (var file = File.Create(Path.Combine(args[1], "native-more.png"))) moreEncoder.Save(file);
                var songCanvas=Panel(2);
                songCanvas.Measure(new Size(1280,800));songCanvas.Arrange(new Rect(0,0,1280,800));songCanvas.UpdateLayout();
                var songImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);songImage.Render(songCanvas);
                var songEncoder=new PngBitmapEncoder();songEncoder.Frames.Add(BitmapFrame.Create(songImage));
                using(var file=File.Create(Path.Combine(args[1],"native-song-browser.png")))songEncoder.Save(file);
                if(browser.Results.Count!=0 || browser.Input?.Text!="" || !browser.Alphabetic)
                    throw new InvalidDataException("Original initial song browser state differs");
                var spellCallbacks=0;
                browser.Input!.SpellRequested+=_=>spellCallbacks++;
                browser.Input.Letter("M");browser.Input.Letter("D");browser.Input.Letter("H");
                var inputFrame=new System.Windows.Threading.DispatcherFrame();
                var inputWait=new System.Windows.Threading.DispatcherTimer { Interval=TimeSpan.FromMilliseconds(250) };
                inputWait.Tick+=(_,_)=> { inputWait.Stop();inputFrame.Continue=false; };
                inputWait.Start();System.Windows.Threading.Dispatcher.PushFrame(inputFrame);
                if(spellCallbacks!=1 || browser.Input.Text!="MDH" || browser.Results.Count!=0)
                    throw new InvalidDataException("Native keyboard/query integration differs or fabricated local songs");
                songCanvas.UpdateLayout();
                var typedImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);typedImage.Render(songCanvas);
                var typedEncoder=new PngBitmapEncoder();typedEncoder.Frames.Add(BitmapFrame.Create(typedImage));
                using(var file=File.Create(Path.Combine(args[1],"native-song-search.png")))typedEncoder.Save(file);
                // The contract was extracted from HomeNewFragment, not guessed.
                var expected = new[] { "singer", "app", "mixcloud", "youtube", "soudcloud", "more" };
                if (!contract.Tiles.Select(t => t.Tag).SequenceEqual(expected))
                    throw new InvalidDataException("Original default home tile order changed");
                using var catalogue = new WholeCatalogue(Path.Combine(root, "wholekmbox.db"));
                var song = catalogue.GetSongById(101000);
                if (song?.Name != "Mộng dưới hoa (sc)" || song.Spell != "MDH" || song.Singer != "Ái Vân,Thái Châu")
                    throw new InvalidDataException("Native catalogue lookup differs from supplied firmware");
                if (catalogue.GetSongById(-1) is not null || catalogue.IsOnline(-1))
                    throw new InvalidDataException("Native catalogue fabricated a missing song");
                var seedPath = Path.Combine(root, "local-seed.db");
                var originalSeedHash = SHA256.HashData(File.ReadAllBytes(seedPath));
                var localPath = Path.Combine(args[1], "local-state-check.db");
                using (var local = new LocalSongDatabase(seedPath, localPath))
                {
                    // Supplied firmware has an empty local seed: catalogue
                    // metadata must not become locally playable song results.
                    if (local.Search.BySpell("", 0, 0, new(), new()).Count != 0)
                        throw new InvalidDataException("Original empty local seed exposed available songs");
                }
                var upgradedHash = SHA256.HashData(File.ReadAllBytes(localPath));
                using (var reopened = new LocalSongDatabase(seedPath, localPath))
                    reopened.Search.ByName("", 0, new(), new());
                if (!SHA256.HashData(File.ReadAllBytes(localPath)).SequenceEqual(upgradedHash) ||
                    !SHA256.HashData(File.ReadAllBytes(seedPath)).SequenceEqual(originalSeedHash))
                    throw new InvalidDataException("Repeat startup changed local state or modified original seed");
                var importPath = Path.Combine(args[1],"catalogue-import-check.db");
                var wholePath = Path.Combine(root,"wholekmbox.db");
                var catalogueHash = SHA256.HashData(File.ReadAllBytes(wholePath));
                using (var imported = new LocalSongDatabase(seedPath,importPath))
                {
                    var before = imported.SongCount;
                    var count = imported.ImportOnlineCatalogue(wholePath);
                    if(imported.SongCount!=72325 || count!=72325-before || imported.ImportOnlineCatalogue(wholePath)!=0)
                        throw new InvalidDataException("Original catalogue import count/idempotence differs");
                    var mediaBefore=imported.MediaCount;
                    var mediaInserted=imported.ImportOnlineMedia(wholePath);
                    if(imported.MediaCount!=72325 || mediaInserted!=72325-mediaBefore || imported.ImportOnlineMedia(wholePath)!=0)
                        throw new InvalidDataException("Original media metadata import count/idempotence differs");
                    var importedMedia=imported.GetMedia(101000);
                    if(importedMedia.Count!=1 || importedMedia[0] is not { Id:1,FileName:"101000.MPG",DefaultVolume:184,
                        OriginalTrack:1,AccompanyTrack:0,MediaType:"MUSIC",VolumeBalance:291308162,
                        UpdateDateTime:"2019-11-05 16:49:04",VolumeUuid:"" } || imported.GetMedia(-1).Count!=0)
                        throw new InvalidDataException("Original MediaDAO fields or missing media handling differ");
                    var localSong=imported.GetSongById(101000);
                    if(localSong is not { Name:"Mộng dưới hoa (sc)",Spell:"MDH",Words:3,Singer:"Ái Vân,Thái Châu",
                        PlayRate:503,CanScore:1,CanMShow:0,Album:"",ErcVersion:"1",HasRemote:1,
                        LastUpdateTime:"2019-07-04 16:46:03",LocalFlag:0,IsPsl:0,ReportTableNumber:-1,Stage:0,
                        SongSpecies:-999,Selected:false,Favourite:false,EnglishName:null } ||
                        !localSong.SingerIds.SequenceEqual(new[]{4,218,-1,-1}) ||
                        !localSong.Types.SequenceEqual(new[]{3,-1,-1,-1}) ||
                        !localSong.Languages.SequenceEqual(new[]{8,-1,-1,-1}) || imported.GetSongById(-1) is not null)
                        throw new InvalidDataException("Original local song lookup/model defaults differ from bytecode and database");
                    if(imported.Search.BySpell("",0,0,new(),new()).Count!=0 ||
                        imported.Search.BySpell("MDH",0,0,new(),new(true,false)).Count!=0)
                        throw new InvalidDataException("Catalogue import claimed local availability or a connected server");
                    if(imported.Search.BySpell("MDH",0,0,new(),new(true,true)).Count==0)
                        throw new InvalidDataException("Explicit connected query fixture lost original remote metadata");
                    var fixtureSongs=imported.Search.BySpell("",0,0,new(),new(true,true));
                    if(fixtureSongs.Any(song=>song.LocalState!=0))
                        throw new InvalidDataException("Remote import fabricated a local-media flag");
                    var selected=imported.SelectedList;
                    // Separate capture database: these are persistence fixtures,
                    // not interactive selections or claims of playback success.
                    selected.Clear();
                    var storedInput=new SelectedSong(localSong.Id,localSong.CanScore==1,null,localSong.ReportTableNumber,
                        localSong.Stage,"normal",localSong.Name,"",localSong.Singer,localSong.Id.ToString(),"fixture-flow","");
                    var selectedId=selected.AddSong(storedInput);
                    var storedOutput=selected.ReadStoredEntries().Single();
                    if(selectedId<=0 || storedOutput.Sequence!=1 || storedOutput.LegacyPlayType is not null ||
                        storedOutput.Song!=storedInput || !selected.IsExist(selectedId))
                        throw new InvalidDataException("Original seed selected-list schema/storage round-trip differs");
                    selected.Clear();
                    var gridFixture=browser.CreateVerificationFixture(fixtureSongs);
                    var gridBottom=bottom.Create();Canvas.SetTop(gridBottom,bottomContract.Y);gridFixture.Children.Add(gridBottom);
                    gridFixture.Measure(new Size(1280,800));gridFixture.Arrange(new Rect(0,0,1280,800));gridFixture.UpdateLayout();
                    var gridImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);gridImage.Render(gridFixture);
                    var gridEncoder=new PngBitmapEncoder();gridEncoder.Frames.Add(BitmapFrame.Create(gridImage));
                    using(var file=File.Create(Path.Combine(args[1],"native-song-grid-fixture.png")))gridEncoder.Save(file);
                }
                if(!SHA256.HashData(File.ReadAllBytes(wholePath)).SequenceEqual(catalogueHash))
                    throw new InvalidDataException("Local import modified the original whole catalogue");
                if (!bottom.IsVisible("pause_imv") || bottom.IsVisible("play_imv") ||
                    !bottom.IsVisible("ori_imv") || bottom.IsVisible("accp_imv"))
                    throw new InvalidDataException("Original default paired control state differs");
                bottom.SetConfirmedPlaybackState(true, true);
                if (!bottom.IsVisible("play_imv") || bottom.IsVisible("pause_imv") ||
                    !bottom.IsVisible("accp_imv") || bottom.IsVisible("ori_imv"))
                    throw new InvalidDataException("Paired controls do not follow confirmed playback state");
                var history = new FragmentHistory();
                history.Reload(new(1)); history.Reload(new(28)); history.Back();
                if (history.Current.Tag != 1) throw new InvalidDataException("Singer back navigation differs");
                history.Back();
                if (history.Current.Tag != 0) throw new InvalidDataException("Repeated Back revisits the page just left");
                history.Reload(new(35)); history.Reload(new(2)); history.Back(mixcloudEnabled: false);
                if (history.Current.Tag != 0) throw new InvalidDataException("Disabled Mixcloud back fallback differs");
                var arguments = new Dictionary<string, string> { ["youtube_search"] = "sample" };
                history.Reload(new(34, arguments)); history.Reload(new(2)); history.Back();
                if (history.Current.Tag != 34 || history.Current.Arguments is not null)
                    throw new InvalidDataException("YouTube search arguments were not cleared on Back");
                history.Reload(new(0));
                if (history.Entries.Count != 0) throw new InvalidDataException("Home failed to clear navigation history");
                var guard = new OriginalClickGuard();
                if (!guard.TryClick(1000) || guard.TryClick(1500) || !guard.TryClick(1501) || !guard.TryClick(1000))
                    throw new InvalidDataException("Original click guard boundary/clock-reset rules differ");
                File.WriteAllText(Path.Combine(args[1], "verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsRendering = true,
                    androidRuntimeUsed = false,
                    originalDefaultTileOrderVerified = true,
                    originalAssetsVerifiedDuringPackaging = true,
                    nativeCatalogueLookupVerified = true,
                    originalLocalSeedUpgradeVerified = true,
                    originalRemoteCatalogueImportVerified = true,
                    originalMediaMetadataLookupVerified = true,
                    originalLocalSongLookupVerified = true,
                    originalSelectedListStorageVerified = true,
                    originalSongCount = catalogue.GetCount(),
                    bottomControlStateRulesVerified = true,
                    originalNavigationHistoryVerified = true,
                    originalClickGuardVerified = true,
                    moreScreenNativeRendering = true,
                    songBrowserEmptyStateNativeRendering = true,
                    nativeVietnameseInputQueryIntegrationVerified = true,
                    vietnameseKeyboard = "default layout/input translated; Thai and handwriting pending",
                    songGrid = "original default tiles rendered; actions, thumbnails, seekbar and pagination pending",
                    homeResourcePort = "implemented; visual fidelity requires comparison",
                    navigation = "pending", television = "pending", playback = "pending", servers = "pending",
                    fullFidelity = "unverified"
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            // Component host while the remaining screens and handlers are ported.
            // Pending handlers are deliberately not represented as implemented.
            var window = new Window
            {
                Title = "VietK — native home component (port in development)",
                Width = 1280, Height = 800, Background = Brushes.Black,
                Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() }
            };
            bottom.CommandRequested += command =>
            {
                if (command == "home_imv")
                    window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
                // Playback, queue and ambience requests need their real backends.
                // They are not translated into invented playback success/state.
            };
            renderer.NavigationRequested += fragment =>
            {
                if (fragment is 38 or 2) window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel(fragment) };
            };
            more.HomeRequested += () => window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
            browser.HomeRequested += () => window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
            return app.Run(window);
        }
        catch (Exception error)
        {
            var log = Path.Combine(Path.GetTempPath(), "vietk-native-startup-error.txt");
            File.WriteAllText(log, error.ToString());
            if (!args.Contains("--capture")) MessageBox.Show(error.Message, "VietK native component");
            return 1;
        }
    }
}
