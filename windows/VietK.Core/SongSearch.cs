using Microsoft.Data.Sqlite;

namespace VietK.Core;

public sealed record SongQueryContext(bool OnlineNamesEnabled = false,
    bool DataCenterConnected = false, bool PslEnabled = false);
public sealed record SongPage(int Index = 0, int Size = 60);

// SongDAO.getSongsOrderBySpellLocal/getSongsOrderByNameLocal. This requires
// the real local-state schema (IsLocalExist); whole-catalogue metadata alone
// must not be passed off as local-media availability or a connected server.
public sealed class SongSearch(SqliteConnection database)
{
    public IReadOnlyList<CatalogueSong> BySpell(string spell, int limitLength,
        int languageType, SongPage page, SongQueryContext context) =>
        Query(spell, false, limitLength, languageType, page, context);

    public IReadOnlyList<CatalogueSong> ByName(string name, int limitLength,
        SongPage page, SongQueryContext context) => Query(name, true, limitLength, 0, page, context);

    private IReadOnlyList<CatalogueSong> Query(string value, bool byName, int length,
        int language, SongPage page, SongQueryContext context)
    {
        if (page.Index < 0 || page.Size <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        var clauses = new List<string>();
        var orders = new List<string>();
        using var command = database.CreateCommand();
        if (value.Length > 0)
        {
            clauses.Add(byName ? "(SongName LIKE $match OR song_name_en LIKE $match)"
                               : "(SongPy LIKE $match OR song_name_en LIKE $match)");
            command.Parameters.AddWithValue("$match", byName ? "%" + value + "%" : value + "%");
            command.Parameters.AddWithValue("$prefix", value + "%");
            if (byName) clauses.Add("length(SongPy)>0");
        }
        if (!context.PslEnabled) clauses.Add("is_psl=0");
        // Name searches apply word length only when the name isn't empty;
        // spell searches apply it even for the initial empty input.
        if (length > 0 && (!byName || value.Length > 0))
        {
            clauses.Add(length == 9 ? "length(SongPy)>8" : "length(SongPy)=$length");
            if (length != 9) command.Parameters.AddWithValue("$length", length);
        }
        if (!byName && language > 0)
        { clauses.Add("LanguageTypeID=$language"); command.Parameters.AddWithValue("$language", language); }

        string visibility;
        if (!context.OnlineNamesEnabled || !context.DataCenterConnected)
            visibility = "IsLocalExist BETWEEN 1 AND 2";
        else if (clauses.Count == 0)
            visibility = "(IsLocalExist BETWEEN 1 AND 2) OR SongID % 100000000 < 90000000";
        else
            visibility = "(IsLocalExist>0 AND IsLocalExist<=2 OR (SongID % 100000000 < 90000000) AND hasRemote>-1)";
        clauses.Add(visibility);

        if (value.Length > 0)
        {
            orders.Add(byName ? "SongName LIKE $prefix DESC" : "SongPy LIKE $prefix DESC");
            orders.Add("length(SongPy)");
        }
        else orders.Add("IsLocalExist=0");
        orders.Add("(LanguageTypeID=8 OR LanguageTypeID=4) DESC");
        orders.Add("PlayNum DESC");
        command.CommandText = $"""
            SELECT SongID,SongName,SongPy,SongWord,songsterName,LanguageTypeID,PlayNum,hasRemote,song_name_en,IsLocalExist
            FROM tblSong WHERE {string.Join(" AND ", clauses)}
            ORDER BY {string.Join(",", orders)} LIMIT $count OFFSET $offset
            """;
        command.Parameters.AddWithValue("$count", page.Size);
        command.Parameters.AddWithValue("$offset", checked(page.Index * page.Size));
        var songs = new List<CatalogueSong>();
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            var id = reader.GetInt32(0);
            // Original code filters MIDI after the SQL LIMIT, so a page may
            // contain fewer than 60 results. Don't refill/reorder that page.
            if (id >= 100000000 && id <= 100999999) continue;
            songs.Add(new CatalogueSong(id, reader.GetString(1), reader.GetString(2), reader.GetInt32(3),
                reader.GetString(4), reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetString(8))
                { LocalState = reader.GetInt32(9) });
        }
        return songs;
    }
}
