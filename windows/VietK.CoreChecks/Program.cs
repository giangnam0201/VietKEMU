using Microsoft.Data.Sqlite;
using VietK.Core;

// Explicit query fixtures, not licensed song media or a simulated server.
using var connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
using (var command = connection.CreateCommand())
{
    command.CommandText = """
        CREATE TABLE tblSong(SongID INTEGER,SongName TEXT,SongPy TEXT,SongWord INTEGER,
            songsterName TEXT,LanguageTypeID INTEGER,PlayNum INTEGER,hasRemote INTEGER,
            song_name_en TEXT,is_psl INTEGER,IsLocalExist INTEGER);
        INSERT INTO tblSong VALUES
            (1,'Alpha','AA',2,'Singer',8,10,1,'Alpha',0,1),
            (2,'Beta Alpha','BA',2,'Singer',4,100,1,'Beta Alpha',0,2),
            (3,'Remote','RA',2,'Singer',8,1000,1,'Remote',0,0),
            (4,'Unavailable','UA',2,'Singer',8,2000,-1,'Unavailable',0,0),
            (5,'Other language','OA',2,'Singer',1,9999,1,'Other language',0,1),
            (6,'PSL','PA',2,'Singer',8,99999,1,'PSL',1,1),
            (7,'Long','ABCDEFGHI',9,'Singer',8,1,1,'Long',0,1),
            (8,'No spell','',0,'Singer',8,0,1,'No spell',0,1),
            (90000001,'Private','PV',2,'Singer',8,9000,1,'Private',0,0),
            (100000000,'MIDI','MI',2,'Singer',8,999999,1,'MIDI',0,1);
        """;
    command.ExecuteNonQuery();
}
var search = new SongSearch(connection);
void Require(bool value, string description)
{ if (!value) throw new InvalidOperationException(description); }
int[] Ids(IReadOnlyList<CatalogueSong> songs) => songs.Select(song => song.Id).ToArray();

var offline = new SongQueryContext();
var offlineIds = Ids(search.BySpell("",0,0,new(),offline));
Require(offlineIds.SequenceEqual(new[]{2,1,7,8,5}), "Local flags, Vietnamese/Chinese language priority, play counts or exclusions differ");
Require(!offlineIds.Contains(3), "Offline query exposed unavailable remote media");
Require(search.BySpell("Al",0,0,new(),offline).Single().LocalState==1,
    "Search model lost the original local-state flag required by grid controls");
Require(Ids(search.BySpell("",0,0,new(),new(true,false))).SequenceEqual(offlineIds), "Online switch bypassed disconnected data centre");
var connected = Ids(search.BySpell("",0,0,new(),new(true,true)));
Require(connected.Contains(3) && !connected.Contains(4) && !connected.Contains(90000001), "Connected remote/private-ID filtering differs");
Require(Ids(search.ByName("Alpha",0,new(),offline)).SequenceEqual(new[]{1,2}), "Name prefix boost did not precede popularity");
Require(Ids(search.BySpell("Al",0,0,new(),offline)).SequenceEqual(new[]{1}), "English spelling-prefix alternative was lost");
Require(Ids(search.BySpell("",9,0,new(),offline)).SequenceEqual(new[]{7}), "Nine-word option must mean more than eight");
Require(Ids(search.BySpell("",2,4,new(),offline)).SequenceEqual(new[]{2}), "Length/language filtering differs");
Require(Ids(search.ByName("",9,new(),offline)).SequenceEqual(offlineIds), "Empty name incorrectly applied its length filter");
Require(search.BySpell("",0,0,new(0,1),offline).Count == 0, "MIDI was filtered before LIMIT or its page was refilled");
Require(Ids(search.BySpell("",0,0,new(1,1),offline)).SequenceEqual(new[]{2}), "Page offset did not include original filtered MIDI row");
Require(search.ByName("' OR 1=1 --",0,new(),offline).Count == 0, "Search input changed SQL structure");
Console.WriteLine("Original SongDAO search rules verified: local/remote state, spelling/name, ordering, length, language, PSL/MIDI and pagination.");

using var oldSchema = new SqliteConnection("Data Source=:memory:");
oldSchema.Open();
using (var command = oldSchema.CreateCommand())
{
    command.CommandText = "CREATE TABLE tblSong(SongID INTEGER,IsLocalExist INTEGER); INSERT INTO tblSong VALUES(42,2)";
    command.ExecuteNonQuery();
}
LocalSongDatabase.UpgradeSongColumns(oldSchema);
LocalSongDatabase.UpgradeSongColumns(oldSchema);
using (var command = oldSchema.CreateCommand())
{
    command.CommandText = "SELECT SongID,IsLocalExist,is_psl,song_name_en FROM tblSong";
    using var row = command.ExecuteReader();
    Require(row.Read() && row.GetInt32(0)==42 && row.GetInt32(1)==2 && row.GetInt32(2)==0 && row.GetString(3)=="",
        "Original column upgrade changed local state or omitted original defaults");
}
using var wrongSchema = new SqliteConnection("Data Source=:memory:");
wrongSchema.Open();
using (var command = wrongSchema.CreateCommand())
{ command.CommandText = "CREATE TABLE tblSong(SongID INTEGER)"; command.ExecuteNonQuery(); }
var rejected = false;
try { LocalSongDatabase.UpgradeSongColumns(wrongSchema); }
catch (InvalidDataException) { rejected = true; }
Require(rejected, "Whole catalogue was accepted as a local-media database");
Console.WriteLine("Original local schema upgrade verified: defaults, repeat startup, preserved local flags and catalogue rejection.");

var scheduled = new List<(int Delay, Action Callback)>();
var input = new VietnameseSearchInput((delay, callback) => scheduled.Add((delay, callback)));
var searches = new List<string>();
input.SpellRequested += searches.Add;
input.Space(); Require(input.Text == "" && searches.Count == 0, "Leading space changed original input");
input.Letter("A"); input.Letter("B");
Require(input.Text == "AB" && scheduled.All(item => item.Delay == 200) && searches.Count == 0,
    "Letter input did not defer its original query");
scheduled[0].Callback(); Require(searches.Count == 0, "Rapid letters were not coalesced");
scheduled[1].Callback(); Require(searches.SequenceEqual(new[]{"AB"}), "Coalesced query lost current input");
input.Space(); input.Space(); Require(input.Text == "AB " && searches.Count == 2, "Repeated space was accepted");
input.Back(); Require(input.Text == "AB" && searches[^1] == "AB", "Backspace query differs");
scheduled.Clear(); input.Clear();
Require(input.Text == "" && scheduled.Count == 1 && scheduled[0].Delay == 50, "Clear delay differs");
scheduled[0].Callback(); Require(searches[^1] == "", "Clear did not request initial spelling results");
Console.WriteLine("Original Vietnamese input verified: 200ms request coalescing, space, backspace and 50ms clear.");

var orderState=new OrderContext(ScannedVolumes:1,NetworkConnected:true);
var localOrder=new OrderCandidate("normal",1,false);
var remoteOrder=new OrderCandidate("normal",0,false);
OrderDecision Decision(OrderCandidate? item,bool top=false,OrderContext? state=null)=>
    OriginalOrderPolicy.Evaluate(item,top,state??orderState);
Require(Decision(null)==OrderDecision.MissingItem &&
    Decision(null,state:orderState with { OrderingAvoided=true })==OrderDecision.OrderingAvoided,
    "Ordering avoidance/null precedence differs from bytecode");
Require(Decision(localOrder)==OrderDecision.AppendLocal && Decision(localOrder,true)==OrderDecision.TopLocal &&
    Decision(remoteOrder)==OrderDecision.AppendDownload && Decision(remoteOrder,true)==OrderDecision.TopDownload,
    "Original local/download routing differs");
Require(Decision(localOrder,state:orderState with { QueueCount=299 })==OrderDecision.AppendLocal &&
    Decision(localOrder,state:orderState with { QueueCount=300 })==OrderDecision.QueueLimit &&
    Decision(localOrder with { AlreadyQueued=true },true,orderState with { QueueCount=300 })==OrderDecision.TopLocal,
    "Original 300-item boundary or existing Top exception differs");
Require(Decision(localOrder with { AlreadyQueued=true })==OrderDecision.AlreadyQueued &&
    Decision(localOrder with { AlreadyQueued=true },state:orderState with { RepeatOrderingEnabled=true })==OrderDecision.AppendLocal &&
    Decision(localOrder with { PlayType="midi",AlreadyQueued=true },state:orderState with { RepeatOrderingEnabled=true })==OrderDecision.AlreadyQueued,
    "Repeat ordering must apply to normal songs only");
Require(Decision(remoteOrder,state:orderState with { ScannedVolumes=0,NetworkConnected=false })==OrderDecision.NoStorage &&
    Decision(remoteOrder,state:orderState with { NetworkConnected=false })==OrderDecision.NoNetwork,
    "Storage/network gate precedence differs");
Require(Decision(remoteOrder with { PlayType="soundcloud" },state:orderState with { ScannedVolumes=0 })==OrderDecision.AppendDownload &&
    Decision(remoteOrder with { PlayType="soudcloud" },state:orderState with { ScannedVolumes=0 })==OrderDecision.NoStorage,
    "Original service play type spelling or disk exemption differs");
Require(Decision(remoteOrder,state:orderState with { CloudLocked=true,VietnamRegion=false })==OrderDecision.CloudLocked &&
    Decision(remoteOrder,state:orderState with { CloudLocked=true })==OrderDecision.AppendDownload,
    "Original Vietnam-region cloud-lock exemption differs");
Require(Decision(remoteOrder,state:orderState with { MicroServiceLinked=true,MicroServicePaused=true,NetworkConnected=false })==OrderDecision.MicroServicePaused &&
    Decision(localOrder,state:orderState with { MicroServiceLinked=true,TcpConnected=false })==OrderDecision.MicroServiceDisconnected &&
    Decision(remoteOrder,state:orderState with { MicroServiceLinked=true,TcpConnected=true,ScannedVolumes=0,CloudLocked=true,VietnamRegion=false })==OrderDecision.AppendDownload,
    "Original linked-service gates or exemptions differ");
Require(Decision(localOrder with { LocalFlag=-1 })==OrderDecision.AppendLocal,
    "Original routing uses zero versus nonzero, not the grid's local-range test");
Console.WriteLine("Original order gates verified against bytecode: capacity, duplicates, region, storage, network and linked service.");

Require(LocalSong.DefaultSinger(null,"Vô danh")=="Vô danh" && LocalSong.DefaultSinger("unknow","Vô danh")=="Vô danh" &&
    LocalSong.DefaultSinger("unknown","Vô danh")=="unknown" && LocalSong.DefaultSinger("","Vô danh")=="",
    "Original default singer spelling/null/empty rules differ");
using(var command=connection.CreateCommand())
{ command.CommandText="UPDATE tblSong SET songsterName=NULL WHERE SongID=1";command.ExecuteNonQuery(); }
Require(search.BySpell("Al",0,0,new(),offline).Single().Singer=="Vô danh", "Search omitted original default singer handling");

var selectedPath=Path.Combine(Path.GetTempPath(),"vietk-selected-check-"+Guid.NewGuid()+".db");
using(var selectedConnection=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=selectedPath,Pooling=false }.ToString()))
{
    selectedConnection.Open();
    using(var schema=selectedConnection.CreateCommand())
    {
        schema.CommandText="""
            CREATE TABLE tblSelectedList(id INTEGER NOT NULL PRIMARY KEY,songid INT,canscore INT,sequence INT,
                customerId NVARCHAR,tableid INT,stage INTEGER,playType INT,name TEXT,url TEXT,customerContent TEXT);
            INSERT INTO tblSelectedList VALUES(7,100,1,1,NULL,-1,0,17,'Legacy',NULL,NULL);
            """;
        schema.ExecuteNonQuery();
    }
    var selected=new SelectedListStore(selectedConnection);
    var legacy=selected.ReadStoredEntries().Single();
    Require(legacy.LegacyPlayType==17 && legacy.Song.Type is null && legacy.Song.CustomerId is null && legacy.Id==7,
        "Selected-list migration conflated the legacy numeric playType with the text type");
    _=new SelectedListStore(selectedConnection);
    Require(selected.Count==1,"Repeat selected schema upgrade changed existing rows");
    selected.Clear();
    SelectedSong Entry(int id)=>new(id,true,null,5,2,"normal","Mộng 'dưới hoa'","","Ái Vân","id-"+id,"flow-"+id,"guest");
    var first=Entry(10);var firstId=selected.AddSong(first);
    var secondId=selected.AddSong(Entry(20));selected.AddSong(Entry(30));selected.AddSong(Entry(40));
    Require(firstId>0 && selected.IsExist(firstId) && first.CustomerId=="" &&
        selected.ReadStoredEntries().Select(row=>row.Sequence).SequenceEqual(new[]{1,2,3,4}),
        "Selected append/customer normalization/sequence numbering differs");
    var restored=selected.ReadStoredEntries()[0];
    Require(restored.Song==first && restored.LegacyPlayType is null,"Selected metadata did not round-trip exactly");
    Require(selected.TopSong(4) && selected.ReadStoredEntries().Select(row=>row.Song.SongId).SequenceEqual(new[]{10,40,20,30}),
        "Top replaced the playing song instead of the next slot");
    selected.SortLocalSong(2,4);
    Require(selected.ReadStoredEntries().Select(row=>row.Song.SongId).SequenceEqual(new[]{10,20,30,40}),"Forward queue sort differs");
    selected.SortLocalSong(4,2);selected.DeleteSong(3);
    Require(selected.ReadStoredEntries().Select(row=>row.Song.SongId).SequenceEqual(new[]{10,40,30}) &&
        selected.ReadStoredEntries().Select(row=>row.Sequence).SequenceEqual(new[]{1,2,3}) &&
        selected.GetSequenceNumber(20)==-1 && !selected.IsExist(secondId),"Delete/renumber/identity lookup differs");
    Require(!selected.DeleteSong(0) && selected.DeleteSong(99) && selected.Count==3,
        "Original absent-positive-sequence delete result was changed");
    selected.Clear();selected.AddSong(Entry(10));selected.AddSong(Entry(20));selected.AddSong(Entry(10));selected.AddSong(Entry(40));
    Require(selected.DeleteSongBySongId(10) && selected.ReadStoredEntries().Select(row=>row.Sequence).SequenceEqual(new[]{1,3}),
        "Repeated song deletion must preserve the original single-shift behavior");
    selected.AddSong(Entry(50));
    Require(selected.ReadStoredEntries().Select(row=>row.Sequence).SequenceEqual(new[]{1,3,3}),
        "Append used max sequence instead of the original count plus one");
    Require(!selected.DeleteSongBySongId(999),"Absent song deletion reported success");
    using(var trigger=selectedConnection.CreateCommand())
    {
        trigger.CommandText="CREATE TRIGGER reject_fixture BEFORE INSERT ON tblSelectedList WHEN NEW.songid=999 BEGIN SELECT RAISE(ABORT,'fixture'); END";
        trigger.ExecuteNonQuery();
    }
    Require(selected.AddSong(Entry(999))==-1 && selected.Count==3,"Failed selected insert fabricated success or a row");
}
using(var reopened=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=selectedPath,Mode=SqliteOpenMode.ReadWrite,Pooling=false }.ToString()))
{
    reopened.Open();var persisted=new SelectedListStore(reopened);
    Require(persisted.Count==3 && persisted.ReadStoredEntries().All(row=>row.Song.Name=="Mộng 'dưới hoa'"),
        "Selected metadata was lost or rewritten across database reopen");
}
File.Delete(selectedPath);
Console.WriteLine("Original selected store verified: migration, round-trip, Top/sort/delete, repeats, persistence and failed insert.");

using(var queueDb=new SqliteConnection("Data Source=:memory:"))
{
    queueDb.Open();
    using(var schema=queueDb.CreateCommand())
    {
        schema.CommandText="CREATE TABLE tblSelectedList(id INTEGER NOT NULL PRIMARY KEY,songid INT,canscore INT,sequence INT,customerId TEXT,tableid INT,stage INT)";
        schema.ExecuteNonQuery();
    }
    var store=new SelectedListStore(queueDb);
    LocalSong? Lookup(int id)=>id==999?null:new(id,"Database name "+id,"DB",2,"Database singer",
        new[]{1,2,-1,-1},new[]{8,-1,-1,-1},new[]{8,-1,-1,-1},1,0,0,"","",1,"",2,0);
    SongMedia Media(int id)=>new(id,10,"media"+id,100,1,0,"audio","",0,"","","","",0,null,null,"");
    SelectedSong Stored(int id,string? type)=>new(id,false,null,5,3,type,"Saved name "+id,null,null,"id-"+id,null,null);
    store.AddSong(Stored(10,null));store.AddSong(Stored(999,"normal"));
    foreach(var type in new[]{"normal","mdream","kmtrain","photomv","movie","youtube"})store.AddSong(Stored(10,type));
    IReadOnlyList<SelectedPlaylistItem> Restore()=>SelectedPlaylistItem.Restore(store.ReadStoredEntries(),Lookup,
        _=>new[]{Media(1),Media(2)},media=>media.Id==2?"verified-local-file":null);
    var restored=Restore();
    Require(restored.Count==6 && restored[0].Sequence==3 && restored[0].CanScoreInDatabase && restored[0].IsDisco &&
        restored[0].SongMetadata.Name=="Database name 10" && restored[0].PlayName=="Saved name 10" &&
        restored[0].TableId==5 && restored[0].Stage==3 && restored[0].VideoMedia?.Id==2 &&
        restored[0].SingerName=="" && restored[0].PlayUrl=="" && restored[0].FlowId=="" &&
        restored[0].InfoId=="normal||id-10||Saved name 10" && restored[0].DownloadState==200 && !restored[0].DownloadFinished,
        "Selected restore filtering, source-versus-saved metadata, null getters, score polarity or local media preference differs");
    Require(restored.Take(4).All(item=>item.CanScoreInDatabase && item.LocalFlag==2) &&
        restored.Skip(4).All(item=>!item.CanScoreInDatabase && item.LocalFlag==1 && item.SongMetadata.Spell=="" &&
            item.SongMetadata.SingerIds.SequenceEqual(new int[4])),
        "Original catalogue-backed versus synthetic playback type reconstruction differs");
    var noLocal=SelectedPlaylistItem.Restore(store.ReadStoredEntries(),Lookup,_=>new[]{Media(1),Media(2)},_=>null);
    Require(noLocal[0].VideoMedia?.Id==1,"Remote metadata fallback changed or inferred a local file");
    Require(!restored[0].IsSongCanScore(_=>null,_=>true) && !restored[0].IsSongCanScore(_=>"missing.erc",_=>false) &&
        restored[0].IsSongCanScore(_=>"existing.erc",_=>true) && !restored[4].IsSongCanScore(_=>"existing.erc",_=>true),
        "Scoring availability did not require both the original database flag and an existing ERC file");
    var copying=restored[0].Copy();copying.DownloadState=400;copying.DownloadProgress=80;copying.DownloadFinished=true;
    var copied=copying.Copy();Require(copied.DownloadState==200 && copied.DownloadProgress==0 && copied.DownloadFinished,
        "Original copy's download-state defaults or finished-state transfer differs");

    var commands=new List<SelectedQueueCommand>();var effects=new List<string>();
    void Post(SelectedQueueCommand command) { commands.Add(command);effects.Add("post"+command.What); }
    var queue=new OriginalSelectedQueue(Lookup,Post,()=>effects.Add("changed"),()=>effects.Add("start"));
    queue.ClearOnInitialize=false;queue.Initialize(store,Restore);
    Require(queue.IsInitialized && queue.Count==0 && store.Count==8 && commands.Count==0,
        "Initialization invented saved queue restoration or persistence before initialized");
    SelectedPlaylistItem Item(int id,string type="normal")=>new(Lookup(id)!,0,null,null)
        { PlayType=type,PlayId="id-"+id,PlayName="Song "+id,InfoId=type+"||id-"+id+"||Song "+id };
    queue.Add(Item(10));queue.Add(Item(20));queue.Add(Item(30));queue.Add(Item(40));
    Require(effects.Take(3).SequenceEqual(new[]{"start","post4","changed"}) && commands.Count==4 &&
        queue.Snapshot().All(item=>Guid.TryParse(item.FlowId,out _) && item.LocalFlag==2),
        "Append flow IDs or first-song notification/DAO dispatch order differs");
    Require(!queue.TopByIndex(0) && !queue.TopByIndex(1) && !queue.TopByIndex(4) && queue.TopByIndex(3) &&
        queue.Snapshot().Select(item=>item.SongMetadata.Id).SequenceEqual(new[]{10,40,20,30}) && commands.Last()==new SelectedQueueCommand(3,4),
        "Queue Top guards, current-song preservation or one-based DAO argument differs");
    Require(!queue.SortByIndex(0,2) && !queue.SortByIndex(2,0) && queue.SortByIndex(1,3) &&
        queue.Snapshot().Select(item=>item.SongMetadata.Id).SequenceEqual(new[]{10,20,30,40}) &&
        commands.Last()==new SelectedQueueCommand(6,1,3),"Drag sort converted original DAO indices or moved current song");
    Require(queue.Exists(Item(20)) && !queue.Exists(Item(50)) && queue.Top(Item(30),true,false) &&
        queue.Snapshot()[1].SongMetadata.Id==30 && queue.Top(Item(50),false,false) && queue.Snapshot()[1].SongMetadata.Id==50,
        "Existing versus new Top routing differs");
    var oldCount=queue.Count;Require(queue.Top(Item(30),true,true) && queue.Count==oldCount+1 &&
        queue.Snapshot()[1].SongMetadata.Id==30,"Normal repeat Top did not add a separate item");
    var youtube=Item(60,"youtube");youtube.PlayUrl="https://www.youtube.com/watch?v=ABC";queue.Add(youtube);
    var ytCopy=queue.Snapshot().Last();
    var ytDuplicate=Item(70,"youtube");ytDuplicate.PlayUrl=ytCopy.PlayUrl;
    Require(youtube.PlayUrl=="ABC" && ytCopy.FlowId=="ABC" && ytCopy.PlayUrl=="https://www.youtube.com/watch?v=ABC" &&
        queue.Exists(ytDuplicate),"YouTube normalization/clone flow or URL duplicate identity differs");
    var cloud=Item(80,"soundcloud");cloud.CloudKey="cloud-key";queue.Add(cloud);
    var cloudDuplicate=Item(90,"mixcloud");cloudDuplicate.CloudKey="cloud-key";
    Require(queue.Snapshot().Last().FlowId=="cloud-key" && queue.Exists(cloudDuplicate),"Cloud key identity differs");
    cloudDuplicate.PlayId="-1";Require(queue.Top(cloudDuplicate,true,false) && queue.Snapshot()[1].FlowId=="cloud-key",
        "Original -1 play-ID sentinel did not select Top by cloud flow ID");
    var named=Item(100);named.PlayId="-1";queue.Add(named);
    Require(queue.Top(named,true,false) && queue.Snapshot()[1].PlayName==named.PlayName,
        "Original -1 play-ID sentinel did not select Top by play name");
    effects.Clear();Require(queue.DeleteByIndex(0) && effects.SequenceEqual(new[]{"post1","start","changed"}) &&
        commands.Last()==new SelectedQueueCommand(1,1),"Playing-song deletion notifications/one-based DAO dispatch differs");
    effects.Clear();Require(!queue.DeleteByIndex(-1) && effects.SequenceEqual(new[]{"changed"}),
        "Invalid deletion changed original notification semantics");
    store.Clear();foreach(var command in commands.Take(6))command.Apply(store,item=>item.IsSongCanScore(_=>null,_=>false));
    Require(store.ReadStoredEntries().Select(row=>row.Song.SongId).SequenceEqual(new[]{40,20,10,30}),
        "DAO dispatch repaired the original drag-sort index mismatch instead of forwarding it");
    queue.ClearWithoutNext();Require(queue.Count==0 && commands.Last()==new SelectedQueueCommand(5),"Clear dispatch differs");
    effects.Clear();Require(!queue.DeleteByIndex(0) && effects.SequenceEqual(new[]{"start","changed"}),
        "Original empty current-slot deletion did not request playback/list notification");
    var fresh=new OriginalSelectedQueue(Lookup,Post,()=>{},()=>{});
    fresh.Initialize(store,Restore);Require(fresh.ClearOnInitialize && store.Count==0 && fresh.Count==0,
        "Default manager initialization did not clear the persisted selected list");
}
Console.WriteLine("Original selected items/manager verified: reconstruction, score polarity, media preference, initialization, Top, drag indices, flow identity and notifications.");

var dispatchPath=Path.Combine(Path.GetTempPath(),"vietk-dispatch-"+Guid.NewGuid()+".db");
using(var observer=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=dispatchPath,Pooling=false }.ToString()))
{
    observer.Open();
    using(var schema=observer.CreateCommand())
    {
        schema.CommandText="CREATE TABLE tblSelectedList(id INTEGER NOT NULL PRIMARY KEY,songid INT,canscore INT,sequence INT,customerId TEXT,tableid INT,stage INT)";
        schema.ExecuteNonQuery();
    }
    var store=new SelectedListStore(observer);
    SelectedPlaylistItem Item(int id)=>new(new LocalSong(id,"Song "+id,"",0,"",new int[4],new int[4],new int[4],
        0,0,0,"","",0,"",1,0),0,null,null) { PlayId=id.ToString(),PlayName="Song "+id };
    using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
    var workerThread=0;var mainThread=Environment.CurrentManagedThreadId;
    using(var dispatcher=new SelectedQueueDispatcher(dispatchPath,item=>
    {
        workerThread=Environment.CurrentManagedThreadId;entered.Set();
        if(!release.Wait(TimeSpan.FromSeconds(10)))throw new TimeoutException("Dispatcher fixture release timed out");
        return false;
    }))
    {
        await dispatcher.Ready.WaitAsync(TimeSpan.FromSeconds(10));
        Require(dispatcher.Post(new(4,Item:Item(10))),"Worker rejected first append");
        Require(entered.Wait(TimeSpan.FromSeconds(10)),"Worker never processed append");
        var second=Item(20);dispatcher.Post(new(4,Item:second));second.PlayName="Mutated before dispatch";
        dispatcher.Post(new(4,Item:Item(30)));dispatcher.Post(new(3,3));dispatcher.Post(new(1,2));
        dispatcher.Post(new(31));release.Set();
        await dispatcher.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        var rows=store.ReadStoredEntries();
        Require(workerThread!=mainThread && rows.Select(row=>row.Song.SongId).SequenceEqual(new[]{10,20}) &&
            rows[1].Song.Name=="Mutated before dispatch" && rows.All(row=>row.Song.CustomerId=="" && !row.Song.CanScore),
            "Worker thread isolation, FIFO append/Top/delete, reference message or scoring normalization differs");
        dispatcher.Post(new(5));await dispatcher.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Require(store.Count==0,"Worker clear/barrier did not reach the observer connection");
        dispatcher.Dispose();Require(!dispatcher.Post(new(4,Item:Item(40))),"Stopped worker accepted a message");
    }
    using(var failing=new SelectedQueueDispatcher(dispatchPath,_=>throw new InvalidDataException("fixture worker failure")))
    {
        await failing.Ready.WaitAsync(TimeSpan.FromSeconds(10));failing.Post(new(4,Item:Item(50)));
        var failed=false;
        try { await failing.FlushAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch(InvalidDataException) { failed=true; }
        Require(failed && !failing.Post(new(5)) && store.Count==0,"Faulted worker hid its failure or accepted further operations");
    }
}
File.Delete(dispatchPath);
Console.WriteLine("Original playlist worker verified: independent connection/thread, FIFO messages, barriers, live message objects, ignored 31, shutdown rejection and fault propagation.");
