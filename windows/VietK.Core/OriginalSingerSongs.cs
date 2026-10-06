using Microsoft.Data.Sqlite;

namespace VietK.Core;

public sealed record OriginalSinger(int Id,string Name,string Spell,string PictureResourceId,string EnglishName);

// SingerDAO.getSingerByName and SongDAO.getSingerSongsOrderBySpell/Name.
public sealed class OriginalSingerSongs(SqliteConnection database)
{
    public OriginalSinger? Find(string name)
    {
        if(string.IsNullOrEmpty(name))return null;
        using var command=database.CreateCommand();
        command.CommandText="SELECT SongsterID,SongsterName,SongsterPy,Pic_FileID_L,singer_name_en FROM tblSinger WHERE SongsterName LIKE $name OR singer_name_en LIKE $english LIMIT 1";
        command.Parameters.AddWithValue("$name",name);command.Parameters.AddWithValue("$english","%"+name+"%");
        using var reader=command.ExecuteReader();
        return reader.Read()?new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?"0":reader.GetValue(3).ToString()!,reader.IsDBNull(4)?"":reader.GetString(4)):null;
    }
    public IReadOnlyList<CatalogueSong> BySpell(int id,string spell,int length,int language,SongPage page,SongQueryContext context)
        =>Query(id,spell,length,language,page,context,false,false);
    // The APK's isFuzzyAfter=true uses a prefix; false uses a substring.
    public IReadOnlyList<CatalogueSong> ByName(int id,string name,int length,bool isFuzzyAfter,SongPage page,SongQueryContext context)
        =>Query(id,name,length,0,page,context,true,isFuzzyAfter);
    private IReadOnlyList<CatalogueSong> Query(int id,string value,int length,int language,SongPage page,SongQueryContext context,bool byName,bool fuzzyAfter)
    {
        if(page.Index<0||page.Size<=0)throw new ArgumentOutOfRangeException(nameof(page));
        if(id<=0)return [];
        using var command=database.CreateCommand();
        var clauses=new List<string> { "(SongsterID1=$singer OR SongsterID2=$singer OR SongsterID3=$singer OR SongsterID4=$singer)","length(SongPy)>0" };
        command.Parameters.AddWithValue("$singer",id);
        var ordering=new List<string>();
        if(value.Length>0)
        {
            clauses.Add(byName?"(SongName LIKE $match OR song_name_en LIKE $match)":"(SongPy LIKE $match OR song_name_en LIKE $match)");
            command.Parameters.AddWithValue("$match",byName&&!fuzzyAfter?"%"+value+"%":value+"%");
            if(!byName||!fuzzyAfter)
            { ordering.Add(byName?"SongName LIKE $prefix DESC":"SongPy LIKE $prefix DESC");command.Parameters.AddWithValue("$prefix",value+"%"); }
            ordering.Add("length(SongPy)");
        }
        else ordering.Add("IsLocalExist=0");
        if(length>0)
        {
            clauses.Add(length==9?"length(SongPy)>8":"length(SongPy)=$length");
            if(length!=9)command.Parameters.AddWithValue("$length",length);
        }
        if(language>0) { clauses.Add("LanguageTypeID=$language");command.Parameters.AddWithValue("$language",language); }
        if(!context.PslEnabled)clauses.Add("is_psl=0");
        clauses.Add(context.OnlineNamesEnabled&&context.DataCenterConnected?
            "(IsLocalExist>0 AND IsLocalExist<=2 OR SongID % 100000000 < 90000000 AND hasRemote>-1)":"IsLocalExist BETWEEN 1 AND 2");
        ordering.Add("(LanguageTypeID=8 OR LanguageTypeID=4) DESC");ordering.Add("PlayNum DESC");
        command.CommandText=$"SELECT SongID,SongName,SongPy,SongWord,songsterName,LanguageTypeID,PlayNum,hasRemote,song_name_en,IsLocalExist FROM tblSong WHERE {string.Join(" AND ",clauses)} ORDER BY {string.Join(",",ordering)} LIMIT $count OFFSET $offset";
        command.Parameters.AddWithValue("$count",page.Size);command.Parameters.AddWithValue("$offset",checked(page.Index*page.Size));
        var result=new List<CatalogueSong>();using var reader=command.ExecuteReader();
        while(reader.Read())
        {
            var songId=reader.GetInt32(0);if(songId is >=100000000 and <=100999999)continue;
            result.Add(new(songId,reader.GetString(1),reader.GetString(2),reader.GetInt32(3),LocalSong.DefaultSinger(reader.IsDBNull(4)?null:reader.GetString(4),"Vô danh"),reader.GetInt32(5),reader.GetInt32(6),reader.GetInt32(7),reader.GetString(8)) { LocalState=reader.GetInt32(9) });
        }
        return result;
    }
}
