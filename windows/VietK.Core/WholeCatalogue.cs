using Microsoft.Data.Sqlite;

namespace VietK.Core;

public sealed record CatalogueSong(int Id, string Name, string Spell, int Words,
    string Singer, int Language, int PlayCount, int HasRemote, string EnglishName);

// Direct translation of WholeSongDAO's catalogue lookup methods. Read-only:
// this does not claim that catalogue entries have local or downloadable media.
public sealed class WholeCatalogue : IDisposable
{
    private readonly SqliteConnection connection;
    public WholeCatalogue(string path)
    {
        connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        connection.Open();
    }

    public long GetCount() => Scalar("SELECT count(*) FROM tblSong");
    public long GetCountHasRemote() => Scalar("SELECT count(*) FROM tblSong WHERE hasRemote = 1 OR hasRemote = 2");
    public bool IsExist(int songId) => GetSongById(songId) is not null;

    public bool IsOnline(int songId)
    {
        // WholeSongDAO.isOnline checks metadata only. It is NOT a live server
        // availability check and is never displayed as successful downloading.
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT hasRemote FROM tblSong WHERE SongID = $id";
        command.Parameters.AddWithValue("$id", songId);
        var value = command.ExecuteScalar();
        return value is long flag && (flag == 1 || flag == 2);
    }

    public CatalogueSong? GetSongById(int songId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT SongID, SongName, SongPy, SongWord, songsterName,
                   LanguageTypeID, PlayNum, hasRemote, song_name_en
              FROM tblSong WHERE SongID = $id
            """;
        command.Parameters.AddWithValue("$id", songId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new CatalogueSong(reader.GetInt32(0), reader.GetString(1),
            reader.GetString(2), reader.GetInt32(3), reader.GetString(4), reader.GetInt32(5),
            reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8)) : null;
    }

    private long Scalar(string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt64(command.ExecuteScalar());
    }

    public void Dispose() => connection.Dispose();
}
