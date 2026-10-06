using Microsoft.Data.Sqlite;

namespace VietK.Core;

// SingerNameForRecyclerFragment / SingerTypeManager / SingerDAO.
// SQL batches contain 80 singers; the original pager displays eight at a time.
public sealed class OriginalSingerDirectory(SqliteConnection database)
{
    public const int BatchSize=80;
    public const int PageSize=8;
    public static IReadOnlyList<string> Countries { get; }=Array.AsReadOnly(new[]{"Tất cả","Việt nam","Châu Âu và Mỹ","Trung Quốc"});
    public static IReadOnlyList<string> Sexes { get; }=Array.AsReadOnly(new[]{"Tất cả","Nam","Nữ","Ban nhạc và nhóm"});
    private static readonly int[] Male=[1,3,8,11,14,17,20,23,26,29,32,35,38,41,44,47,50];
    private static readonly int[] Female=[2,4,9,12,15,18,21,24,27,30,33,36,39,42,45,48,51];
    private static readonly int[] Groups=[5,7,10,13,16,19,22,25,28,31,34,37,40,43,46,49,52];
    public static int[] Types(int country,int sex)
    {
        if(country is <0 or >3)throw new ArgumentOutOfRangeException(nameof(country));
        if(sex is <0 or >3)throw new ArgumentOutOfRangeException(nameof(sex));
        if(country==0)return sex switch { 0=>new[]{0,6}.Concat(Male).Concat(Female).Concat(Groups).ToArray(),1=>Male.ToArray(),2=>Female.ToArray(),_=>Groups.ToArray() };
        if(country==3)return sex switch { 0=>[1,2,3,4,5],1=>[1,3],2=>[2,4],_=>[5] };
        var first=country==1?8:35;
        return sex==0?[first,first+1,first+2]:[first+sex-1];
    }
    public IReadOnlyList<OriginalSinger> BySpell(string spell,int country,int sex,int batch=0,int length=0)
    {
        if(batch<0)throw new ArgumentOutOfRangeException(nameof(batch));
        using var command=Prepare(spell,country,sex,length,false,false,false);
        command.CommandText="SELECT SongsterID,SongsterName,SongsterPy,Pic_FileID_L,singer_name_en FROM tblSinger WHERE "+command.CommandText+
            " ORDER BY "+(spell.Length==0?"":"SongsterPy=$value DESC,SongsterPy LIKE $prefix DESC,length(SongsterPy) ASC,")+"SongsterOrderRank DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size",BatchSize);command.Parameters.AddWithValue("$offset",checked(batch*BatchSize));
        return Read(command);
    }
    public int CountBySpell(string spell,int country,int sex,int length=0)
    {
        // The source count excludes empty spellings even though its list query
        // includes them. Preserve this distinction rather than inventing rows.
        using var command=Prepare(spell,country,sex,length,false,false,true);
        command.CommandText="SELECT count(*) FROM tblSinger WHERE "+command.CommandText;
        return Convert.ToInt32(command.ExecuteScalar());
    }
    public IReadOnlyList<OriginalSinger> ByName(string name,int country,int sex,bool prefix,int batch=0,int length=0)
    {
        if(batch<0)throw new ArgumentOutOfRangeException(nameof(batch));
        using var command=Prepare(name,country,sex,length,true,prefix,false);
        command.CommandText="SELECT SongsterID,SongsterName,SongsterPy,Pic_FileID_L,singer_name_en FROM tblSinger WHERE "+command.CommandText+
            " ORDER BY "+(name.Length==0?"":"SongsterName=$value DESC,SongsterName LIKE $prefix DESC,length(SongsterName) ASC,")+"SongsterOrderRank DESC LIMIT $size OFFSET $offset";
        command.Parameters.AddWithValue("$size",BatchSize);command.Parameters.AddWithValue("$offset",checked(batch*BatchSize));
        return Read(command);
    }
    private SqliteCommand Prepare(string value,int country,int sex,int length,bool byName,bool prefix,bool count)
    {
        if(length<0)throw new ArgumentOutOfRangeException(nameof(length));
        var command=database.CreateCommand();var types=Types(country,sex);
        var clauses=new List<string> { "SongsterTypeID IN ("+string.Join(",",types)+")" };
        if(!byName||value.Length>0)
        {
            clauses.Add(byName?"(SongsterName LIKE $match OR singer_name_en LIKE $english)":"(SongsterPy LIKE $match OR singer_name_en LIKE $english)");
            command.Parameters.AddWithValue("$match",byName&&!prefix?"%"+value+"%":value+"%");
            command.Parameters.AddWithValue("$english",byName&&prefix?value+"%":"%"+value+"%");
        }
        if(length>0) { clauses.Add(length>8?"length(SongsterPy)>8":"length(SongsterPy)=$length");if(length<=8)command.Parameters.AddWithValue("$length",length); }
        else if(count)clauses.Add("length(SongsterPy)>0");
        if(value.Length>0) { command.Parameters.AddWithValue("$value",value);command.Parameters.AddWithValue("$prefix",value+"%"); }
        command.CommandText=string.Join(" AND ",clauses);return command;
    }
    private static IReadOnlyList<OriginalSinger> Read(SqliteCommand command)
    {
        var result=new List<OriginalSinger>();using var reader=command.ExecuteReader();
        while(reader.Read())result.Add(new(reader.GetInt32(0),reader.GetString(1),reader.GetString(2),reader.IsDBNull(3)?"0":reader.GetValue(3).ToString()!,reader.IsDBNull(4)?"":reader.GetString(4)));
        return result;
    }
}
