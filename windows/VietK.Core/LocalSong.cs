namespace VietK.Core;

// SongDAO.getSongById's 26 database fields and Song constructor defaults.
// The bytecode returns the constructed object; JADX incorrectly drops that
// return. This is local state, distinct from WholeCatalogue's metadata model.
public sealed record LocalSong(int Id,string Name,string Spell,int Words,string Singer,
    int[] SingerIds,int[] Types,int[] Languages,int PlayRate,int CanScore,int CanMShow,
    string? Album,string? ErcVersion,int HasRemote,string? LastUpdateTime,int LocalFlag,int IsPsl)
{
    // Song.canScore uses zero as enabled, unlike tblSelectedList's bool encoding.
    public bool ScoringEnabled => CanScore==0;
    public int ReportTableNumber { get; set; }=-1;
    public int Stage { get; set; }
    public int SongSpecies { get; set; }=-999;
    public bool Selected { get; set; }
    public bool Favourite { get; set; }
    // getSongById uses the original constructor without the English-name field.
    public string? EnglishName { get; set; }

    public static string DefaultSinger(string? value,string anonymousSinger)=>
        value is null or "unknow"?anonymousSinger:value;
}
