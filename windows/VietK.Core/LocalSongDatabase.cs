using Microsoft.Data.Sqlite;

namespace VietK.Core;

// The APK ships kmbox.jpg as the initial writable database. SongManager.addColumn
// upgrades tblSong on startup; wholekmbox.jpg is a separate attached catalogue.
public sealed class LocalSongDatabase : IDisposable
{
    private readonly SqliteConnection connection;
    public SongSearch Search { get; }

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

    public void Dispose() => connection.Dispose();
}
