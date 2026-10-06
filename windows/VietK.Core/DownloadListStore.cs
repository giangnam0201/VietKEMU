using Microsoft.Data.Sqlite;

namespace VietK.Core;

public sealed record DownloadSong(int SongId,int TableId,int Stage,string? Type,string? Name,string? Url,
    string? Singer,string? PlayId,string? FlowId,string? CustomerContent);
public sealed record StoredDownloadSong(long Id,int Sequence,int? LegacyPlayType,DownloadSong Song);

// SongDownListDAO. Download persistence has no canscore/customerId columns.
public sealed class DownloadListStore(SqliteConnection database)
{
    private readonly object gate=new();
    public void UpgradeSchema()
    {
        // DAOHelper creates this table before the manager adds its columns.
        Execute("CREATE TABLE IF NOT EXISTS tblSongDownList(id INTEGER PRIMARY KEY,songid int,sequence int,tableId int,stage int)");
        using var schema=database.CreateCommand();schema.CommandText="PRAGMA table_info(tblSongDownList)";
        var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var row=schema.ExecuteReader())while(row.Read())columns.Add(row.GetString(1));
        if(!new[]{"id","songid","sequence","tableId","stage"}.All(columns.Contains))
            throw new InvalidDataException("Missing original download-list base schema");
        foreach(var (name,type) in new[]{("playType","int"),("name","TEXT"),("url","TEXT"),
            ("customerContent","TEXT"),("type","TEXT"),("singer","TEXT"),("playid","TEXT"),("flowid","TEXT")})
            if(!columns.Contains(name))Execute($"ALTER TABLE tblSongDownList ADD COLUMN {name} {type}");
    }
    public int Add(SelectedPlaylistItem? item)
    {
        if(item is null)return 0;
        lock(gate)
        {
            var id=0;
            try
            {
                using var transaction=database.BeginTransaction();var sequence=Scalar("SELECT count(*) FROM tblSongDownList",transaction)+1;
                try
                {
                    Execute("""
                        INSERT INTO tblSongDownList(songid,sequence,tableId,stage,type,name,url,singer,playid,flowid,customerContent)
                        VALUES($song,$sequence,$table,$stage,$type,$name,$url,$singer,$play,$flow,$content)
                        """,transaction,("$song",item.SongMetadata.Id),("$sequence",sequence),("$table",item.TableId),
                        ("$stage",item.Stage),("$type",item.PlayType),("$name",item.PlayName),("$url",item.PlayUrl),
                        ("$singer",item.SingerName),("$play",item.PlayId),("$flow",item.FlowId),("$content",item.CustomerContent));
                    id=unchecked((int)Scalar("SELECT last_insert_rowid()",transaction));
                }
                catch(SqliteException error) { Log(error);id=-1; }
                transaction.Commit();return id;
            }
            catch(SqliteException error) { Log(error);return id; }
        }
    }
    public bool Top(int requested)
    {
        lock(gate)
        {
            var result=false;
            try
            {
                using var transaction=database.BeginTransaction();
                var sequence=Scalar("SELECT sequence FROM tblSongDownList WHERE sequence=$sequence",transaction,("$sequence",requested));
                if(sequence<=0)return false;
                Execute("UPDATE tblSongDownList SET sequence=0 WHERE sequence=$sequence",transaction,("$sequence",sequence));
                Execute("UPDATE tblSongDownList SET sequence=sequence+1 WHERE sequence>1 AND sequence<$sequence",transaction,("$sequence",sequence));
                Execute("UPDATE tblSongDownList SET sequence=2 WHERE sequence=0",transaction);
                result=true;transaction.Commit();return result;
            }
            catch(SqliteException error) { Log(error);return result; }
        }
    }
    public bool Delete(int requested)=>DeleteCore(requested,false);
    public bool DeleteBySong(int songId)=>DeleteCore(songId,true);
    private bool DeleteCore(int key,bool bySong)
    {
        lock(gate)
        {
            var result=false;
            try
            {
                using var transaction=database.BeginTransaction();
                var found=Scalar("SELECT sequence FROM tblSongDownList WHERE "+(bySong?"songid":"sequence")+"=$key",transaction,("$key",key));
                var sequence=bySong?found:found>0?found:key;
                if(sequence>0)
                {
                    Execute("DELETE FROM tblSongDownList WHERE "+(bySong?"songid":"sequence")+"=$key",transaction,("$key",bySong?key:sequence));
                    Execute("UPDATE tblSongDownList SET sequence=sequence-1 WHERE sequence>$sequence",transaction,("$sequence",sequence));result=true;
                }
                transaction.Commit();return result;
            }
            catch(SqliteException error) { Log(error);return result; }
        }
    }
    public int GetTableNumber(int sequence)
    {
        lock(gate)
        {
            try
            {
                using var query=Command("SELECT tableId FROM tblSongDownList WHERE sequence=$sequence",null,("$sequence",sequence));
                var value=query.ExecuteScalar();return value is null?-1:value is DBNull?0:Convert.ToInt32(value);
            }
            catch(SqliteException error) { Log(error);return -1; }
        }
    }
    public bool Clear()
    { lock(gate) { try { Execute("DELETE FROM tblSongDownList"); }catch(SqliteException error) { Log(error); }return true; } }
    public IReadOnlyList<StoredDownloadSong> ReadStoredEntries()
    {
        lock(gate)
        {
            using var query=Command("SELECT id,songid,sequence,tableId,stage,playType,name,url,customerContent,type,singer,playid,flowid FROM tblSongDownList WHERE id>0 ORDER BY sequence ASC");
            using var row=query.ExecuteReader();var rows=new List<StoredDownloadSong>();
            int Int(int index)=>row.IsDBNull(index)?0:row.GetInt32(index);
            string? Text(int index)=>row.IsDBNull(index)?null:row.GetString(index);
            while(row.Read())rows.Add(new(row.GetInt64(0),Int(2),row.IsDBNull(5)?null:Int(5),
                new(Int(1),Int(3),Int(4),Text(9),Text(6),Text(7),Text(10),Text(11),Text(12),Text(8))));
            return rows;
        }
    }
    public IReadOnlyList<SelectedPlaylistItem> Restore(Func<int,LocalSong?> lookup,
        Func<int,IReadOnlyList<SongMedia>> mediaLookup,Func<SongMedia,string?> localPath)=>
        SelectedPlaylistItem.Restore(ReadStoredEntries().Select(row=>new StoredSelectedSong(row.Id,row.Sequence,row.LegacyPlayType,
            new SelectedSong(row.Song.SongId,false,"",row.Song.TableId,row.Song.Stage,row.Song.Type,row.Song.Name,row.Song.Url,
                row.Song.Singer,row.Song.PlayId,row.Song.FlowId,row.Song.CustomerContent))),lookup,mediaLookup,localPath,syntheticLocalFlag:0);
    public void Apply(DownloadQueueCommand command)
    {
        switch(command.What)
        {
            case 1:Delete(command.Arg1);break;
            case 2:DeleteBySong(command.Arg1);break;
            case 3:Top(command.Arg1);break;
            case 4:Add(command.Item);break;
            case 5:Clear();break;
        }
    }
    private SqliteCommand Command(string sql,SqliteTransaction? transaction=null,params (string Key,object? Value)[] values)
    {
        var query=database.CreateCommand();query.CommandText=sql;query.Transaction=transaction;
        foreach(var (key,value) in values)query.Parameters.AddWithValue(key,value??DBNull.Value);return query;
    }
    private void Execute(string sql,SqliteTransaction? transaction=null,params (string Key,object? Value)[] values)
    { using var query=Command(sql,transaction,values);query.ExecuteNonQuery(); }
    private long Scalar(string sql,SqliteTransaction? transaction=null,params (string Key,object? Value)[] values)
    { using var query=Command(sql,transaction,values);var value=query.ExecuteScalar();return value is null or DBNull?0:Convert.ToInt64(value); }
    private static void Log(Exception error)=>System.Diagnostics.Trace.TraceError(error.ToString());
}
