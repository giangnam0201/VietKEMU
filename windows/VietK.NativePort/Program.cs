using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
            OriginalSupplement.Initialize();
            OriginalFont.Initialize(root);
            var contract = JsonSerializer.Deserialize<HomeContract>(File.ReadAllText(Path.Combine(root, "home.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original home contract");
            var app = new Application();
            var renderer = new HomeScreen(root, contract);
            var bottomContract = JsonSerializer.Deserialize<BottomContract>(File.ReadAllText(Path.Combine(root, "bottom.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original bottom bar contract");
            var bottom = new BottomBar(root, bottomContract);
            var topContract = JsonSerializer.Deserialize<TopContract>(File.ReadAllText(Path.Combine(root, "top.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original top bar contract");
            var top = new TopBar(root, topContract, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VietKNativePort", "download", "logo"));
            var moreContract = JsonSerializer.Deserialize<MoreContract>(File.ReadAllText(Path.Combine(root, "more.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original More screen contract");
            var more = new MoreScreen(root, moreContract, contract);
            var songContract = JsonSerializer.Deserialize<SongBrowserContract>(File.ReadAllText(Path.Combine(root, "song-browser.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidDataException("Missing original song browser contract");
            var capturing = args.Length == 2 && args[0] == "--capture";
            var verifyingPlayback = args.Length == 3 && args[0] == "--verify-playback";
            var verifyingYouTube = args.Length == 3 && args[0] is "--verify-youtube" or "--verify-youtube-firefox";
            var stateDirectory = capturing || verifyingPlayback || verifyingYouTube ? args[^1] : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "VietKNativePort");
            using var songState = new LocalSongDatabase(Path.Combine(root,"local-seed.db"),
                Path.Combine(stateDirectory,"song-browser-state.db"));
            if(!capturing && !verifyingPlayback)
            {
                songState.ImportOnlineCatalogue(Path.Combine(root,"wholekmbox.db"));
                songState.ImportOnlineMedia(Path.Combine(root,"wholekmbox.db"));
            }
            using var queueDispatcher=new SelectedQueueDispatcher(Path.Combine(stateDirectory,"song-browser-state.db"),
                // No discovered storage/ERC resolver yet: metadata cannot prove
                // a usable local subtitle. Replace with original storage port.
                item=>item.IsSongCanScore(_=>null,File.Exists));
            queueDispatcher.Ready.GetAwaiter().GetResult();
            using var downloadDispatcher=new DownloadQueueDispatcher(Path.Combine(stateDirectory,"song-browser-state.db"));
            downloadDispatcher.Ready.GetAwaiter().GetResult();
            SongBrowser? queueBrowser=null;
            OriginalSelectedQueue? selectedQueue=null;
            OriginalDownloadQueue? downloadQueue=null;
            NativePlayback? playback=null;
            OriginalQueueRemote? originalRemote=null;
            OriginalCollectionBrowser? collectionBrowser=null;
            using var musicServer=new NativeMusicServer(app.Dispatcher,stateDirectory,id=>songState.GetSongById(id));
            IReadOnlyList<SongMedia> AvailableMedia(int id)=>musicServer.Get(id) is { } cached?
                new[]{cached.Metadata}.Concat(songState.GetMedia(id)).ToArray():songState.GetMedia(id);
            void StartQueuedMedia()
            {
                app.Dispatcher.BeginInvoke(() =>
                {
                    var item = selectedQueue?.Snapshot().FirstOrDefault();
                    if (item is null) { playback?.StartIdleDemo(); return; }
                    var cached=musicServer.Get(item.SongMetadata.Id);
                    var path=string.IsNullOrEmpty(item.PlayUrl)?cached?.Path:item.PlayUrl;
                    if(item.PlayType!="normal" || string.IsNullOrEmpty(path))return;
                    playback?.PlayMedia(path,cached?.Metadata??item.VideoMedia,flowId:item.FlowId);
                });
            }
            void QueueChanged()
            {
                var combined=(selectedQueue?.Snapshot()??Array.Empty<SelectedPlaylistItem>())
                    .Concat(downloadQueue?.Snapshot()??Array.Empty<SelectedPlaylistItem>()).ToArray();
                if(playback?.Source!=PlaybackSource.YouTube)bottom.SetConfirmedQueueCount(combined.Length);
                originalRemote?.Refresh();
                queueBrowser?.SetConfirmedQueuedSongs(combined.Select(item=>item.SongMetadata.Id).ToHashSet());
                collectionBrowser?.RefreshMedia();
            }
            selectedQueue=new OriginalSelectedQueue(id=>songState.GetSongById(id),
                command=> { if(!queueDispatcher.Post(command))throw new InvalidOperationException("Playlist database worker stopped"); },
                QueueChanged,
                StartQueuedMedia);
            selectedQueue.Initialize(songState.SelectedList,()=>SelectedPlaylistItem.Restore(
                songState.SelectedList.ReadStoredEntries(),id=>songState.GetSongById(id),AvailableMedia,musicServer.LocalPath));
            OriginalDownloadSelection? downloadSelection=null;
            downloadQueue=new OriginalDownloadQueue(
                command=> { if(!downloadDispatcher.Post(command))throw new InvalidOperationException("Download database worker stopped"); },
                QueueChanged,()=>downloadSelection!.DownloadFirst(),
                ()=> { musicServer.Cancel();downloadSelection!.Reset(); },
                id=>System.Diagnostics.Trace.WriteLine($"Original progress-registry removal {id}; registry port pending"),
                id=>System.Diagnostics.Trace.WriteLine($"Original song-update removal {id}; updater port pending"));
            downloadSelection=new(downloadQueue,QueueChanged,
                musicServer.Request,
                (item,action)=>System.Diagnostics.Trace.WriteLine($"Original non-Evideo download request {item.PlayType}, action {action}; handler port pending"));
            downloadQueue.Initialize(()=>songState.DownloadList.Clear(),
                ()=>songState.DownloadList.Restore(id=>songState.GetSongById(id),AvailableMedia,musicServer.LocalPath),onlineNeeded:true);
            musicServer.Progress+=(id,received,total)=> { downloadQueue.SetProgressBySong(id,total,received);QueueChanged(); };
            musicServer.Completed+=(id,cached)=>
            {
                songState.ConfirmDownloadedSong(id);
                // LocalOnlineSongManager.handleDownloadSuccess/moveItem: reverse
                // matching entries, transfer to local list, then remove download.
                for(var index=downloadQueue.Count-1;index>=0;index--)
                {
                    var item=downloadQueue.At(index)!;if(item.SongMetadata.Id!=id)continue;
                    item.DownloadState=203;item.LocalFlag=item.SongMetadata.HasRemote!=0?item.SongMetadata.HasRemote:1;
                    item.PlayUrl=cached.Path;
                    if(item.Stage>0)selectedQueue.Top(item,false,false);else selectedQueue.Add(item);
                    downloadQueue.DeleteByIndex(index);
                }
                downloadSelection.Reset();downloadSelection.DownloadFirst();QueueChanged();
                queueBrowser?.Refresh();
            };
            musicServer.Failed+=async (id,code,detail)=>
            {
                if(code==1015)downloadSelection.Stop();
                if(id>0)
                {
                    downloadSelection.RecordError(code,code==1016);
                    System.Diagnostics.Trace.WriteLine($"Song {id}: download error {code}: {detail}");
                    // Original errors update the queue rather than block its
                    // worker behind a Windows modal dialog.
                    if(code!=1016) { await Task.Delay(1000);downloadSelection.AdvanceAfterError(); }
                }
                else MessageBox.Show(detail,"VietK music server",MessageBoxButton.OK,MessageBoxImage.Error);
            };
            QueueChanged();
            var gridContract=JsonSerializer.Deserialize<SongGridContract>(File.ReadAllText(Path.Combine(root,"song-grid.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original song grid contract");
            var browser = new SongBrowser(root, songContract, moreContract, songState,gridContract,
                ()=>new SongQueryContext(OnlineNamesEnabled:true,DataCenterConnected:musicServer.IsConnected));
            musicServer.ConnectionChanged+=browser.Refresh;
            queueBrowser=browser;
            var orderDependencies=JsonSerializer.Deserialize<OrderDependencies>(File.ReadAllText(Path.Combine(root,"order-dependencies.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive=true })??throw new InvalidDataException("Missing original order dependencies");
            void Feedback(OrderDecision decision)
            {
                var resource=OriginalOrderExecutor.FeedbackResource(decision);
                // Original Android system-toast styling still needs translation.
                // Retain its exact resource text without substituting a dialog.
                if(resource is not null)System.Diagnostics.Trace.WriteLine(orderDependencies.Feedback[resource]);
            }
            var orderExecutor=new OriginalOrderExecutor(
                item=>OriginalPlaylistIdentity.Exists(selectedQueue.Snapshot().Concat(downloadQueue.Snapshot()),item),selectedQueue.Add,
                selectedQueue.Top,
                downloadQueue.Add,
                (item,exists)=>downloadQueue.Top(item,exists,false),
                _=>System.Diagnostics.Trace.WriteLine("Original rate-sync request; rate updater pending"),
                _=>System.Diagnostics.Trace.WriteLine("Original countAllOrderSong call; stat observer pending"),Feedback,
                id=>songState.GetSongById(id));
            var songOrder=new NativeSongOrder(songState,orderDependencies,orderExecutor,
                ()=>new OrderContext(NetworkConnected:musicServer.NetworkConnected,
                    ScannedVolumes:musicServer.StorageAvailable?1:0,QueueCount:selectedQueue.Count+downloadQueue.Count),musicServer.LocalPath,()=>false,
                text=>System.Diagnostics.Trace.WriteLine(text),
                (action,mode)=>System.Diagnostics.Trace.WriteLine($"Original report plugin request {action}, mode {mode}; plugin execution pending"),AvailableMedia);
            YouTubeMusicScreen? youtube=null;
            var collectionProfiles=new OriginalCollectionProfiles(stateDirectory);
            var collectionControls=new NativeCollectionControls(collectionProfiles,
                ()=>app.MainWindow?.Content is Viewbox { Child:Canvas panel }?panel:null,browser.SetConfirmedCollectedSongs);
            using var collectionScreen=new OriginalCollectionBrowser(root,collectionProfiles,id=>songState.GetSongById(id),
                ()=>new SongQueryContext(OnlineNamesEnabled:true,DataCenterConnected:musicServer.IsConnected),
                ()=>selectedQueue.Snapshot().Concat(downloadQueue.Snapshot()).ToArray(),collectionControls,gridContract);
            collectionBrowser=collectionScreen;
            collectionScreen.ActionRequested+=(song,action)=>
            { if(action is "order" or "top")songOrder.Request(song.Id,action=="top"); };
            musicServer.ConnectionChanged+=collectionScreen.RefreshMedia;
            browser.SongActionRequested+=(song,action)=>
            {
                if(action is "order" or "top")songOrder.Request(song.Id,action=="top");
                else if(action=="collect")collectionControls.Collect(song.Id);
            };
            Canvas Panel(int screen = 0)
            {
                var panel = screen switch { 34 when youtube is not null => youtube.Create(),
                    38 => more.Create(), 14 => collectionScreen.Create(), 2 => browser.Create(), _ => renderer.Create() };
                TextElement.SetFontFamily(panel, OriginalFont.Family);
                var bar = bottom.Create();
                Canvas.SetTop(bar, bottomContract.Y); panel.Children.Add(bar);
                panel.Children.Add(top.Create());
                return panel;
            }
            if (verifyingPlayback)
                return NativePlaybackVerification.Run(app, Panel(), bottom, root, args[1], args[2]);
            if(verifyingYouTube)
                return NativeYouTubeVerification.Run(app,Panel(),bottom,args[1],args[2],args[0]=="--verify-youtube-firefox");
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
                    var storedInput=new SelectedSong(localSong.Id,localSong.ScoringEnabled,null,localSong.ReportTableNumber,
                        localSong.Stage,"normal",localSong.Name,"",localSong.Singer,localSong.Id.ToString(),"fixture-flow","");
                    var selectedId=selected.AddSong(storedInput);
                    var storedOutput=selected.ReadStoredEntries().Single();
                    if(selectedId<=0 || storedOutput.Sequence!=1 || storedOutput.LegacyPlayType is not null ||
                        storedOutput.Song!=storedInput || !selected.IsExist(selectedId))
                        throw new InvalidDataException("Original seed selected-list schema/storage round-trip differs");
                    var restoredItem=SelectedPlaylistItem.Restore(selected.ReadStoredEntries(),
                        id=>imported.GetSongById(id),imported.GetMedia,_=>null).Single();
                    if(restoredItem.SongMetadata.Id!=101000 || restoredItem.CanScoreInDatabase ||
                        restoredItem.VideoMedia?.FileName!="101000.MPG" || restoredItem.LocalFlag!=0 ||
                        restoredItem.InfoId!="normal||101000||Mộng dưới hoa (sc)" ||
                        restoredItem.FlowId!="fixture-flow" || restoredItem.DownloadState!=200 || restoredItem.DownloadFinished)
                        throw new InvalidDataException("Original selected item reconstruction differs");
                    selected.Clear();
                    var gridFixture=browser.CreateVerificationFixture(fixtureSongs);
                    var downloads=imported.DownloadList;downloads.Clear();
                    var downloadId=downloads.Add(restoredItem);var downloadRow=downloads.ReadStoredEntries().Single();
                    var restoredDownload=downloads.Restore(id=>imported.GetSongById(id),imported.GetMedia,_=>null).Single();
                    if(downloadId<=0 || downloadRow.Sequence!=1 || downloadRow.LegacyPlayType is not null ||
                        downloadRow.Song.SongId!=101000 || downloadRow.Song.FlowId!="fixture-flow" ||
                        restoredDownload.LocalFlag!=0 || restoredDownload.CustomerId!="" || restoredDownload.CanScoreInDatabase ||
                        restoredDownload.InfoId!="normal||101000||Mộng dưới hoa (sc)")
                        throw new InvalidDataException("Original download table creation/metadata/reconstruction differs");
                    downloads.Clear();
                    using(var fixtureDownloadDispatcher=new DownloadQueueDispatcher(importPath))
                    {
                        fixtureDownloadDispatcher.Ready.GetAwaiter().GetResult();
                        OriginalDownloadSelection? selectionFixture=null;OriginalDownloadQueue? downloadFixture=null;
                        var requestedId=0;var notifiedCount=0;
                        downloadFixture=new OriginalDownloadQueue(command=>
                            { if(!fixtureDownloadDispatcher.Post(command))throw new InvalidOperationException("Download fixture dispatcher stopped"); },
                            ()=>notifiedCount=downloadFixture!.Count,()=>selectionFixture!.DownloadFirst(),
                            ()=>selectionFixture!.Reset(),_=>{},_=>{});
                        selectionFixture=new(downloadFixture,()=>notifiedCount=downloadFixture.Count,
                            id=>requestedId=id,(_,_)=>throw new InvalidDataException("Normal song reached non-Evideo downloader"));
                        downloadFixture.Initialize(()=>downloads.Clear(),()=>downloads.Restore(id=>imported.GetSongById(id),imported.GetMedia,_=>null),true);
                        // A backend fixture, not a claim that this machine has
                        // registered storage, server access or playable music.
                        downloadFixture.Add(restoredItem);fixtureDownloadDispatcher.FlushAsync().GetAwaiter().GetResult();
                        if(requestedId!=101000 || notifiedCount!=1 || downloadFixture.At(0)!.DownloadState!=202 ||
                            downloadFixture.At(0)!.LocalFlag!=0 || downloads.ReadStoredEntries().Single().Song.SongId!=101000 ||
                            !OriginalPlaylistIdentity.Exists(downloadFixture.Snapshot(),restoredItem))
                            throw new InvalidDataException("Native download worker/selection/combined identity differs");
                        downloadFixture.Clear();fixtureDownloadDispatcher.FlushAsync().GetAwaiter().GetResult();
                        if(downloads.ReadStoredEntries().Count!=0 || selectionFixture.IsDownloading)
                            throw new InvalidDataException("Native download clear/reset differs");
                    }
                    var gridBottom=bottom.Create();Canvas.SetTop(gridBottom,bottomContract.Y);gridFixture.Children.Add(gridBottom);
                    gridFixture.Measure(new Size(1280,800));gridFixture.Arrange(new Rect(0,0,1280,800));gridFixture.UpdateLayout();
                    var gridImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);gridImage.Render(gridFixture);
                    var gridEncoder=new PngBitmapEncoder();gridEncoder.Frames.Add(BitmapFrame.Create(gridImage));
                    using(var file=File.Create(Path.Combine(args[1],"native-song-grid-fixture.png")))gridEncoder.Save(file);
                    // Explicit backend integration fixture, bypassing order
                    // admission. It verifies notifications/storage/rendering,
                    // not network admission, downloaded media or playback.
                    using(var fixtureDispatcher=new SelectedQueueDispatcher(importPath,item=>item.IsSongCanScore(_=>null,File.Exists)))
                    {
                        fixtureDispatcher.Ready.GetAwaiter().GetResult();
                        var playRequests=0;OriginalSelectedQueue? fixtureQueue=null;
                        fixtureQueue=new OriginalSelectedQueue(id=>imported.GetSongById(id),
                            command=> { if(!fixtureDispatcher.Post(command))throw new InvalidOperationException("Fixture dispatcher stopped"); },
                            ()=> { bottom.SetConfirmedQueueCount(fixtureQueue!.Count);
                                browser.SetConfirmedQueuedSongs(fixtureQueue.Snapshot().Select(item=>item.SongMetadata.Id).ToHashSet()); },
                            ()=>playRequests++);
                        fixtureQueue.Initialize(selected,()=>SelectedPlaylistItem.Restore(selected.ReadStoredEntries(),
                            id=>imported.GetSongById(id),imported.GetMedia,_=>null));
                        var feedbackText="";var reportRequests=0;
                        var fixtureExecutor=new OriginalOrderExecutor(fixtureQueue.Exists,fixtureQueue.Add,fixtureQueue.Top,
                            _=>throw new InvalidDataException("Rejected native click reached download backend"),
                            (_,_)=>throw new InvalidDataException("Rejected native click reached download backend"),
                            _=>{},_=>{},decision=>
                            { var key=OriginalOrderExecutor.FeedbackResource(decision);if(key is not null)feedbackText=orderDependencies.Feedback[key]; },
                            id=>imported.GetSongById(id));
                        var fixtureOrder=new NativeSongOrder(imported,orderDependencies,fixtureExecutor,()=>new(),_=>null,()=>false,
                            text=>feedbackText=text,(action,mode)=>
                            { if(action!=OriginalReportTableRoute.PluginAction || mode!=2)throw new InvalidDataException("Original plugin launch changed");reportRequests++; });
                        var clickHandled=fixtureOrder.Request(101000,true);
                        if(orderDependencies.Manifests.Length!=23 || selected.Count!=0 || fixtureQueue.Count!=0 ||
                            (orderDependencies.ReportTableActivityCount==0
                                ?clickHandled || fixtureOrder.LastExecution?.Decision!=OrderDecision.NoStorage ||
                                    feedbackText!=orderDependencies.Feedback["order_song_no_disk_tip"]
                                :!clickHandled || reportRequests!=1 || fixtureOrder.LastExecution is not null))
                            throw new InvalidDataException("Original native click/plugin/admission flow differs or fabricated queue success");
                        fixtureQueue.Add(restoredItem);fixtureDispatcher.FlushAsync().GetAwaiter().GetResult();
                        if(bottom.QueueCount!=1 || fixtureQueue.Count!=1 || playRequests!=1 ||
                            selected.ReadStoredEntries().Single().Song.SongId!=101000 ||
                            selected.ReadStoredEntries().Single().Song.CanScore || fixtureQueue.Snapshot()[0].LocalFlag!=0)
                            throw new InvalidDataException("Native queue/worker/observer integration differs or fabricated media/scoring");
                        var queuedSongs=imported.Search.BySpell("MDH",0,0,new(),new(true,true));
                        if(!queuedSongs.Any(song=>song.Id==101000))throw new InvalidDataException("Queued visual fixture does not include its selected song");
                        var queuedFixture=browser.CreateVerificationFixture(queuedSongs);
                        var queuedBottom=bottom.Create();Canvas.SetTop(queuedBottom,bottomContract.Y);queuedFixture.Children.Add(queuedBottom);
                        queuedFixture.Measure(new Size(1280,800));queuedFixture.Arrange(new Rect(0,0,1280,800));queuedFixture.UpdateLayout();
                        var queueImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);queueImage.Render(queuedFixture);
                        var queueEncoder=new PngBitmapEncoder();queueEncoder.Frames.Add(BitmapFrame.Create(queueImage));
                        using(var file=File.Create(Path.Combine(args[1],"native-queue-observer-fixture.png")))queueEncoder.Save(file);
                        fixtureQueue.DeleteByIndex(0);fixtureDispatcher.FlushAsync().GetAwaiter().GetResult();
                        if(bottom.QueueCount!=0 || selected.Count!=0 || playRequests!=2)
                            throw new InvalidDataException("Native queue deletion did not propagate to observer/store/play request");
                    }
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
                var youtubeBottom=new BottomBar(root,bottomContract);
                using(var youtubePlayback=new NativePlayback(youtubeBottom,args[1]))
                using(var youtubeCapture=new YouTubeMusicScreen(root,args[1],youtubePlayback,youtubeBottom))
                {
                    var youtubeCanvas=youtubeCapture.Create(loadDefault:false);TextElement.SetFontFamily(youtubeCanvas,OriginalFont.Family);
                    youtubeCanvas.Children.Add(top.Create());var youtubeBar=youtubeBottom.Create();
                    Canvas.SetTop(youtubeBar,bottomContract.Y);youtubeCanvas.Children.Add(youtubeBar);
                    youtubeCanvas.Measure(new Size(1280,800));youtubeCanvas.Arrange(new Rect(0,0,1280,800));youtubeCanvas.UpdateLayout();
                    var youtubeImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);youtubeImage.Render(youtubeCanvas);
                    var youtubeEncoder=new PngBitmapEncoder();youtubeEncoder.Frames.Add(BitmapFrame.Create(youtubeImage));
                    using var captureFile=File.Create(Path.Combine(args[1],"native-youtube.png"));youtubeEncoder.Save(captureFile);
                    youtubeCapture.SetVerificationResults(Enumerable.Range(0,8).Select(index=>
                        new YouTubeVideo("fixture000"+index,"Verification fixture "+(index+1)+" — kiểm tra bố cục hai hàng", "", "")).ToArray());
                    youtubeCapture.VerifyPagination();
                    youtubeCapture.VerifyQueueControls(youtubeCanvas,args[1]);
                    youtubeCanvas.UpdateLayout();
                    var pageImage=new RenderTargetBitmap(1280,800,96,96,PixelFormats.Pbgra32);pageImage.Render(youtubeCanvas);
                    var pageEncoder=new PngBitmapEncoder();pageEncoder.Frames.Add(BitmapFrame.Create(pageImage));
                    using var pageFile=File.Create(Path.Combine(args[1],"native-youtube-page-fixture.png"));pageEncoder.Save(pageFile);
                }
                File.WriteAllText(Path.Combine(args[1], "verification.json"), JsonSerializer.Serialize(new
                {
                    nativeWindowsRendering = true,
                    youtubeMainPanelNativeRendering = true,
                    youtubeSixCardPaginationAndBoundsVerified = true,
                    selectedQueueTopDeleteShuffleAndPreservePlayingVerified = true,
                    selectedQueueTabsAndRealByteProgressStatesVerified = true,
                    selectedQueueDragMoveCancelAndEdgeScrollVerified = true,
                    androidRuntimeUsed = false,
                    originalDefaultTileOrderVerified = true,
                    originalAssetsVerifiedDuringPackaging = true,
                    nativeCatalogueLookupVerified = true,
                    originalLocalSeedUpgradeVerified = true,
                    originalRemoteCatalogueImportVerified = true,
                    originalMediaMetadataLookupVerified = true,
                    originalLocalSongLookupVerified = true,
                    originalSelectedListStorageVerified = true,
                    originalSelectedItemReconstructionVerified = true,
                    nativeQueueWorkerObserverIntegrationVerified = true,
                    nativeOrderPluginAdmissionVerified = true,
                    originalDownloadListStorageVerified = true,
                    nativeDownloadWorkerSelectionIntegrationVerified = true,
                    firmwareReportTableActivityCount = orderDependencies.ReportTableActivityCount,
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
                    header = "original VietK logo and header template restored; control services pending",
                    originalFirmwareRobotoLoaded = true,
                    navigation = "pending", television = "native video window; complete OSD pending", playback = "native decoder; separate real-media verification required", servers = "pending",
                    fullFidelity = "unverified"
                }, new JsonSerializerOptions { WriteIndented = true }));
                return 0;
            }
            // Component host while the remaining screens and handlers are ported.
            // Pending handlers are deliberately not represented as implemented.
            var window = new Window
            {
                Title = "VietK — control panel",
                Width = 1280, Height = 800, Background = Brushes.Black,
                FontFamily = OriginalFont.Family,
                Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() }
            };
            app.MainWindow = window;
            app.ShutdownMode = ShutdownMode.OnMainWindowClose;
            using var nativePlayback = new NativePlayback(bottom, stateDirectory);
            using var ambienceExpressions=new AmbienceExpressions(nativePlayback.Television.Overlay,nativePlayback.Television,nativePlayback);
            renderer.Playback=nativePlayback;browser.Playback=nativePlayback;
            playback = nativePlayback;
            using var youtubeMusic=new YouTubeMusicScreen(root,stateDirectory,nativePlayback,bottom);
            youtubeMusic.OpenCollectionLogin=()=>collectionControls.Login();
            youtubeMusic.LogoutCollection=collectionControls.Logout;
            originalRemote=new OriginalQueueRemote(selectedQueue,downloadQueue,nativePlayback,bottom,()=>
            {
                if(selectedQueue.Count>0)StartQueuedMedia();
                downloadSelection.Start();
            });
            youtubeMusic.OriginalQueue=originalRemote;
            nativePlayback.Player.Played+=originalRemote.Refresh;
            using var mobileRemote=new MobileRemoteServer(app.Dispatcher,youtubeMusic,nativePlayback,ambience:ambienceExpressions);
            youtubeMusic.MobileConnectionInfo=()=>mobileRemote.ConnectionInfo;
            youtubeMusic.RePairMobile=mobileRemote.RePair;
            youtubeMusic.OpenMobilePairing=()=>mobileRemote.ShowPairingPanel(window);
            youtube=youtubeMusic;
            window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=Panel(34) };
            youtube.HomeRequested+=()=>window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=Panel() };
            browser.YoutubeRequested+=query=>
            {
                var panel=youtube.Create(query);panel.Children.Add(top.Create());
                var bar=bottom.Create();Canvas.SetTop(bar,bottomContract.Y);panel.Children.Add(bar);
                window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=panel };
            };
            nativePlayback.NextRequested += () =>
            {
                if (selectedQueue.Count > 0) selectedQueue.DeleteByIndex(0);
            };
            window.Loaded += async (_, _) =>
            {
                nativePlayback.ShowTelevision(window);
                nativePlayback.StartIdleDemo();
                try { await mobileRemote.StartAsync(); }
                catch(Exception) { System.Diagnostics.Trace.WriteLine("Local mobile remote failed to start; see connection status menu."); }
                // Developer probe, separate from the original song-library UI.
                if (args.Length == 2 && args[0] == "--play-media" && !nativePlayback.PlayMedia(Path.GetFullPath(args[1])))
                    throw new InvalidDataException("Playback probe source is unavailable");
                // YouTube is the user-selected primary source. Original server
                // credentials remain available only through the legacy route.
            };
            bottom.CommandRequested += command =>
            {
                if (command == "home_imv")
                    window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
                else if(command=="ambience_imv")ambienceExpressions.ShowDialog(window);
                else nativePlayback.Command(command);
                // Queue/ambience dialogs and complete service admission checks
                // still need their original ports.
            };
            top.CommandRequested += command =>
            {
                if (command == "logo")
                    window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
                else System.Diagnostics.Trace.WriteLine($"Original top command {command}; service/dialog port pending");
            };
            renderer.NavigationRequested += fragment =>
            {
                if (fragment is 38 or 2 or 34) window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel(fragment==2?34:fragment) };
            };
            more.HomeRequested += () => window.Content = new Viewbox { Stretch = Stretch.Uniform, Child = Panel() };
            more.NavigationRequested+=fragment=> { if(fragment==14)window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=Panel(14) }; };
            collectionScreen.HomeRequested+=()=>window.Content=new Viewbox { Stretch=Stretch.Uniform,Child=Panel() };
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
