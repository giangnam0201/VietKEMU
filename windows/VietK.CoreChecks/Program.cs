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
