using Microsoft.Data.Sqlite;

namespace VietK.Core;

// The APK ships kmbox.jpg as the initial writable database. SongManager.addColumn
// upgrades tblSong on startup; wholekmbox.jpg is a separate attached catalogue.
public sealed class LocalSongDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    public SongSearch Search { get; }
    public SelectedListStore SelectedList { get; }

    public LocalSongDatabase(string seedPath, string statePath)
    {
        if (Path.GetFullPath(seedPath).Equals(Path.GetFullPath(statePath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Local state must be separate from the original seed", nameof(statePath));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        // Never replace persisted local state with the shipped seed on restart.
        if (!File.Exists(statePath)) File.Copy(seedPath, statePath, overwrite: false);
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = statePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        try
        {
            connection.Open();
            UpgradeSongColumns(connection);
            Search = new SongSearch(connection);
            SelectedList = new SelectedListStore(connection);
        }
        catch { connection.Dispose(); throw; }
    }

    public static void UpgradeSongColumns(SqliteConnection database)
    {
        using var transaction = database.BeginTransaction();
        var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var schema = database.CreateCommand())
        {
            schema.Transaction = transaction;
            schema.CommandText = "PRAGMA table_info(tblSong)";
            using var reader = schema.ExecuteReader();
            while (reader.Read()) columns.Add(reader.GetString(1));
        }
        if (!columns.Contains("IsLocalExist"))
            throw new InvalidDataException("Expected original local-state tblSong, not the whole catalogue");
        // SongManager.addColumn uses these exact types and defaults. Existing
        // rows/flags and already-present columns are left intact.
        foreach (var (name, definition) in new[]
                 { ("is_psl", "int DEFAULT 0"), ("song_name_en", "varchar DEFAULT ''") })
        {
            if (columns.Contains(name)) continue;
            using var alter = database.CreateCommand();
            alter.Transaction = transaction;
            alter.CommandText = $"ALTER TABLE tblSong ADD COLUMN {name} {definition}";
            alter.ExecuteNonQuery();
        }
        transaction.Commit();
    }

    // SongIdDAO.saveOnlineToSong(tblSong). Call from the translated catalogue
    // update/traversal flow, never as evidence of a data-centre connection.
    public int ImportOnlineCatalogue(string cataloguePath) => WithCatalogue(cataloguePath, () =>
    {
        var hasEnglish = false;
        using (var schema = connection.CreateCommand())
        {
            schema.CommandText = "PRAGMA wholedb.table_info(tblSong)";
            using var reader = schema.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1).Equals("song_name_en", StringComparison.OrdinalIgnoreCase)) hasEnglish = true;
        }
        const string columns = "SongID,SongName,SongPy,SongWord,songsterName,SongsterID1,SongsterID2,SongsterID3,SongsterID4," +
            "SongTypeID1,SongTypeID2,SongTypeID3,SongTypeID4,LanguageTypeID,LanguageTypeID2,LanguageTypeID3,LanguageTypeID4," +
            "PlayNum,IsGrand,IsMShow,album,ercVersion,hasRemote,LastUpdateTime";
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = $"INSERT OR IGNORE INTO tblSong({columns},IsLocalExist,is_psl,song_name_en) " +
            $"SELECT {columns},0,is_psl,{(hasEnglish ? "song_name_en" : "''")} FROM wholedb.tblSong WHERE hasRemote BETWEEN 1 AND 2";
        var inserted = insert.ExecuteNonQuery(); transaction.Commit(); return inserted;
    });

    // SongIdDAO.saveOnlineToMedia: a blank volume UUID is metadata only, not
    // a located or downloaded file. Existing MediaID entries are not replaced.
    public int ImportOnlineMedia(string cataloguePath) => WithCatalogue(cataloguePath, () =>
    {
        const string columns = "MediaID,SongID,SongFileName,DefaultVolume,OriginalTrack,AccompanyTrack,MediaType," +
            "SongNameWordType,VolumeBalance,VolumeQuality,ImageQuality,SongVersion,TotalQuality,Price,Md5Value,UpdateDateTime";
        using var transaction = connection.BeginTransaction();
        using var insert = connection.CreateCommand(); insert.Transaction=transaction;
        insert.CommandText=$"INSERT OR IGNORE INTO tblMedia({columns},uuid) SELECT " +
            string.Join(",",columns.Split(',').Select(column=>"wholedb.tblMedia."+column)) +
            ",'' FROM wholedb.tblMedia,tblSong WHERE tblSong.SongID=wholedb.tblMedia.SongID";
        var inserted=insert.ExecuteNonQuery();transaction.Commit();return inserted;
    });

    private int WithCatalogue(string cataloguePath, Func<int> action)
    {
        var fullPath = Path.GetFullPath(cataloguePath);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Original catalogue not found", fullPath);
        using (var attach = connection.CreateCommand())
        {
            attach.CommandText = "ATTACH DATABASE $path AS wholedb";
            attach.Parameters.AddWithValue("$path", fullPath); attach.ExecuteNonQuery();
        }
        try
        {
            return action();
        }
        finally
        {
            using var detach = connection.CreateCommand();
            detach.CommandText = "DETACH DATABASE wholedb"; detach.ExecuteNonQuery();
        }
    }

    public long SongCount
    {
        get { using var query = connection.CreateCommand();query.CommandText="SELECT count(*) FROM tblSong";
            return Convert.ToInt64(query.ExecuteScalar()); }
    }

    public long MediaCount
    {
        get { using var query=connection.CreateCommand();query.CommandText="SELECT count(*) FROM tblMedia";
            return Convert.ToInt64(query.ExecuteScalar()); }
    }

    public IReadOnlyList<SongMedia> GetMedia(int songId)
    {
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT MediaID,SongID,SongFileName,DefaultVolume,OriginalTrack,AccompanyTrack,MediaType,
                SongNameWordType,VolumeBalance,VolumeQuality,ImageQuality,SongVersion,TotalQuality,Price,
                Md5Value,UpdateDateTime,uuid FROM tblMedia WHERE SongID=$song
            """;
        command.Parameters.AddWithValue("$song",songId);
        using var reader=command.ExecuteReader();
        var medias=new List<SongMedia>();
        while(reader.Read()) medias.Add(new(reader.GetInt32(0),reader.GetInt32(1),reader.GetString(2),
            reader.GetInt32(3),reader.GetInt32(4),reader.GetInt32(5),reader.GetString(6),reader.GetString(7),
            reader.GetInt32(8),reader.GetString(9),reader.GetString(10),reader.GetString(11),reader.GetString(12),
            reader.GetInt32(13),reader.IsDBNull(14)?null:reader.GetString(14),
            reader.IsDBNull(15)?null:reader.GetString(15),reader.IsDBNull(16)?null:reader.GetString(16)));
        return medias;
    }

    public LocalSong? GetSongById(int songId,string anonymousSinger="Vô danh")
    {
        using var command=connection.CreateCommand();
        command.CommandText="""
            SELECT SongID,SongName,SongPy,SongWord,songsterName,SongsterID1,SongsterID2,SongsterID3,SongsterID4,
                SongTypeID1,SongTypeID2,SongTypeID3,SongTypeID4,LanguageTypeID,LanguageTypeID2,LanguageTypeID3,LanguageTypeID4,
                PlayNum,IsGrand,IsMShow,album,ercVersion,hasRemote,LastUpdateTime,IsLocalExist,is_psl
            FROM tblSong WHERE SongID=$song
            """;
        command.Parameters.AddWithValue("$song",songId);
        using var row=command.ExecuteReader();if(!row.Read())return null;
        string? String(int index)=>row.IsDBNull(index)?null:row.GetString(index);
        return new(row.GetInt32(0),row.GetString(1),row.GetString(2),row.GetInt32(3),
            LocalSong.DefaultSinger(String(4),anonymousSinger),
            [row.GetInt32(5),row.GetInt32(6),row.GetInt32(7),row.GetInt32(8)],
            [row.GetInt32(9),row.GetInt32(10),row.GetInt32(11),row.GetInt32(12)],
            [row.GetInt32(13),row.GetInt32(14),row.GetInt32(15),row.GetInt32(16)],
            row.GetInt32(17),row.GetInt32(18),row.GetInt32(19),String(20),String(21),row.GetInt32(22),
            String(23),row.GetInt32(24),row.GetInt32(25));
    }

    public void Dispose() => connection.Dispose();
}
