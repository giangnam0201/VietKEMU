using Microsoft.Data.Sqlite;

namespace VietK.Core;

public sealed record SelectedSong(int SongId,bool CanScore,string? CustomerId,int TableId,int Stage,
    string? Type,string? Name,string? Url,string? Singer,string? PlayId,string? FlowId,string? CustomerContent)
{
    public string? CustomerId { get; set; }=CustomerId;
}
public sealed record StoredSelectedSong(long Id,int Sequence,int? LegacyPlayType,SelectedSong Song);

// SelectedListDAO storage operations and SelectedLocalListManager.addNewColum.
// Sequence numbers are ONE based. Manager operations must still protect the
// currently playing entry and send the original playback/list notifications.
public sealed class SelectedListStore
{
    private readonly SqliteConnection database;
    private readonly object gate=new();

    public SelectedListStore(SqliteConnection database)
    {
        this.database=database;
        var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using(var schema=database.CreateCommand())
        {
            schema.CommandText="PRAGMA table_info(tblSelectedList)";
            using var row=schema.ExecuteReader();while(row.Read())columns.Add(row.GetString(1));
        }
        if(!new[]{"id","songid","canscore","sequence","customerId","tableId","stage"}.All(columns.Contains))
            throw new InvalidDataException("Missing original selected-list base schema");
        foreach(var (name,type) in new[]{("playType","int"),("name","TEXT"),("url","TEXT"),
            ("customerContent","TEXT"),("type","TEXT"),("singer","TEXT"),("playid","TEXT"),("flowid","TEXT")})
            if(!columns.Contains(name))Execute($"ALTER TABLE tblSelectedList ADD COLUMN {name} {type}");
    }

    public int AddSong(SelectedSong song)
    {
        lock(gate)
        {
            song.CustomerId??="";var id=0;
            try
            {
                using var transaction=database.BeginTransaction();
                var count=Scalar("SELECT count(*) FROM tblSelectedList",transaction);
                try
                {
                    Execute("""
                        INSERT INTO tblSelectedList(songid,canscore,sequence,customerId,tableId,stage,type,name,url,singer,playid,flowid,customerContent)
                        VALUES($song,$score,$sequence,$customer,$table,$stage,$type,$name,$url,$singer,$play,$flow,$content)
                        """,transaction,("$song",song.SongId),("$score",song.CanScore?1:0),("$sequence",count+1),
                        ("$customer",song.CustomerId),("$table",song.TableId),("$stage",song.Stage),("$type",song.Type),
                        ("$name",song.Name),("$url",song.Url),("$singer",song.Singer),("$play",song.PlayId),
                        ("$flow",song.FlowId),("$content",song.CustomerContent));
                    id=unchecked((int)Scalar("SELECT last_insert_rowid()",transaction));
                }
                catch(SqliteException error) { Log(error);id=-1; } // SQLiteDatabase.insert's failed-insert result.
                transaction.Commit();return id;
            }
            catch(SqliteException error) { Log(error);return id; }
        }
    }

    public bool TopSong(int sequence)
    {
        lock(gate)
        {
            try
            {
                var found=Scalar("SELECT sequence FROM tblSelectedList WHERE sequence=$sequence",null,("$sequence",sequence));
                if(found<=0)return false;
                Execute("UPDATE tblSelectedList SET sequence=0 WHERE sequence=$sequence",null,("$sequence",found));
                Execute("UPDATE tblSelectedList SET sequence=sequence+1 WHERE sequence>1 AND sequence<$sequence",null,("$sequence",found));
                Execute("UPDATE tblSelectedList SET sequence=2 WHERE sequence=0");return true;
            }
            catch(SqliteException error) { Log(error);return false; }
        }
    }

    public bool SortLocalSong(int source,int target)
    {
        lock(gate)
        {
            // Original DAO trusts manager validation and reports true even for
            // a missing source. Don't replace its count-based behavior here.
            Execute("UPDATE tblSelectedList SET sequence=0 WHERE sequence=$source",null,("$source",source));
            Execute(source<target
                ?"UPDATE tblSelectedList SET sequence=sequence-1 WHERE sequence>$source AND sequence<=$target"
                :"UPDATE tblSelectedList SET sequence=sequence+1 WHERE sequence>=$target AND sequence<$source",
                null,("$source",source),("$target",target));
            Execute("UPDATE tblSelectedList SET sequence=$target WHERE sequence=0",null,("$target",target));return true;
        }
    }

    public bool DeleteSong(int sequence)
    {
        lock(gate)
        {
            try
            {
                if(sequence<=0)return false;
                // Original returns true for a positive absent sequence too.
                Execute("DELETE FROM tblSelectedList WHERE sequence=$sequence",null,("$sequence",sequence));
                Execute("UPDATE tblSelectedList SET sequence=sequence-1 WHERE sequence>$sequence",null,("$sequence",sequence));return true;
            }
            catch(SqliteException error) { Log(error);return false; }
        }
    }

    public bool DeleteSongBySongId(int songId)
    {
        lock(gate)
        {
            try
            {
                using var transaction=database.BeginTransaction();
                var first=Scalar("SELECT sequence FROM tblSelectedList WHERE songid=$song",transaction,("$song",songId));
                if(first<=0) { transaction.Commit();return false; }
                Execute("DELETE FROM tblSelectedList WHERE songid=$song",transaction,("$song",songId));
                // The original shifts once even if multiple repeats were
                // deleted. Preserve that behavior rather than repairing gaps.
                Execute("UPDATE tblSelectedList SET sequence=sequence-1 WHERE sequence>$sequence",transaction,("$sequence",first));
                transaction.Commit();return true;
            }
            catch(SqliteException error) { Log(error);return false; }
        }
    }

    public int GetSequenceNumber(int songId)
    {
        lock(gate)
        {
            using var command=Command("SELECT sequence FROM tblSelectedList WHERE songid=$song",null,("$song",songId));
            try { var value=command.ExecuteScalar();return value is null?-1:Convert.ToInt32(value is DBNull?0:value); }
            catch(SqliteException error) { Log(error);return -1; }
        }
    }

    public bool IsExist(long id)
    {
        lock(gate)
        {
            try { return Scalar("SELECT count(*) FROM tblSelectedList WHERE id=$id",null,("$id",id))>0; }
            catch(SqliteException error) { Log(error);return false; }
        }
    }
    public long Count
    {
        get { lock(gate) { try { return Scalar("SELECT count(*) FROM tblSelectedList"); }
            catch(SqliteException error) { Log(error);return 0; } } }
    }
    public bool Clear()
    {
        lock(gate) { try { Execute("DELETE FROM tblSelectedList"); }
            catch(SqliteException error) { Log(error); }return true; }
    }

    // Raw storage rows. SelectedListDAO.getlist additionally reconstructs
    // songs, filters absent songs/types and applies runtime playlist metadata.
    public IReadOnlyList<StoredSelectedSong> ReadStoredEntries()
    {
        lock(gate)
        {
            using var command=Command("""
                SELECT id,songid,canscore,sequence,customerId,tableId,stage,playType,name,url,
                    customerContent,type,singer,playid,flowid FROM tblSelectedList WHERE id>0 ORDER BY sequence ASC
                """);
            using var row=command.ExecuteReader();var entries=new List<StoredSelectedSong>();
            int Int(int index)=>row.IsDBNull(index)?0:row.GetInt32(index);
            string? String(int index)=>row.IsDBNull(index)?null:row.GetString(index);
            while(row.Read())entries.Add(new(row.GetInt64(0),Int(3),row.IsDBNull(7)?null:Int(7),
                new(Int(1),Int(2)==1,String(4),Int(5),Int(6),String(11),String(8),String(9),String(12),String(13),String(14),String(10))));
            return entries;
        }
    }

    private SqliteCommand Command(string sql,SqliteTransaction? transaction=null,params (string Name,object? Value)[] values)
    {
        var command=database.CreateCommand();command.CommandText=sql;command.Transaction=transaction;
        foreach(var (name,value) in values)command.Parameters.AddWithValue(name,value??DBNull.Value);return command;
    }
    private void Execute(string sql,SqliteTransaction? transaction=null,params (string Name,object? Value)[] values)
    { using var command=Command(sql,transaction,values);command.ExecuteNonQuery(); }
    private long Scalar(string sql,SqliteTransaction? transaction=null,params (string Name,object? Value)[] values)
    { using var command=Command(sql,transaction,values);var value=command.ExecuteScalar();return value is null or DBNull?0:Convert.ToInt64(value); }
    private static void Log(Exception error)=>System.Diagnostics.Trace.TraceError(error.ToString());
}
