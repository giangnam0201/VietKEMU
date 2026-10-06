using System.Globalization;

namespace VietK.Core;

// RankSongManager.getRankSongFromFile(0) -> PublicPlaySongPageLoadPresenter.
// Raw 50-entry slices precede database/PSL/availability filtering. Bad cache
// records stop the current slice, preserving the preceding results as Java does.
public sealed class OriginalBroadcastRankCache(string directory,Func<int,LocalSong?> lookup)
{
    public IReadOnlyList<LocalSong> Page(int page,SongQueryContext context)
    {
        if(page<0||page>int.MaxValue/50)throw new ArgumentOutOfRangeException(nameof(page));
        var result=new List<LocalSong>();
        try
        {
            var text=File.ReadAllText(Path.Combine(directory,"rankcache","0.txt"));
            if(text.Length<2)return result;
            var records=text[1..^1].Split(", ",StringSplitOptions.None);
            var start=(long)page*50;var end=Math.Min(start+50,records.Length);
            for(var position=start;position<end;position++)
            {
                var parts=records[(int)position].Split('/');
                if(parts[0].Length==0)continue;
                if(!int.TryParse(parts[0],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out var id))break;
                var song=id>0?lookup(id):null;if(song is null)continue;
                if(parts.Length<2||!int.TryParse(parts[1],NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out _))break;
                if(song.Name.Length==0||(!context.PslEnabled&&song.IsPsl!=0))continue;
                if(!(context.OnlineNamesEnabled&&context.DataCenterConnected)&&song.LocalFlag==0)continue;
                result.Add(song);
            }
        }
        catch(IOException) { }
        catch(UnauthorizedAccessException) { }
        return result;
    }
}
