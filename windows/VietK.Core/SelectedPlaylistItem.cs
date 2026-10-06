namespace VietK.Core;

// SelectedListDAO.getlist and the Song-based KmPlayListItem constructor.
// Media paths are supplied by storage discovery, never inferred from metadata.
public sealed class SelectedPlaylistItem
{
    public LocalSong SongMetadata { get; }
    public SongMedia? VideoMedia { get; }
    public int Sequence { get; }
    public string? CustomerId { get; set; }
    public int TableId { get; }
    public int Stage { get; }
    public int LocalFlag { get; set; }
    public bool CanScore { get; }
    public bool IsDisco { get; }
    public int SongSpecies { get; }
    public bool Broadcast => false;
    public int Score { get; set; }
    public int DownloadState { get; set; }=200;
    public int DownloadProgress { get; set; }
    public bool DownloadFinished { get; set; }
    public string PlayType { get; set; }="normal";
    public string PlayName { get; set; }="";
    public string PlayUrl { get; set; }="";
    public string CustomerContent { get; set; }="";
    public string SingerName { get; set; }
    public string PlayId { get; set; }="";
    private string flowId="";
    public string FlowId { get=>flowId;set { if(!string.IsNullOrEmpty(value))flowId=value; } }
    public string InfoId { get; set; }="";
    public string? CloudKey { get; set; }

    public SelectedPlaylistItem(LocalSong song,int sequence,string? customerId,SongMedia? media)
    {
        SongMetadata=song;Sequence=sequence;CustomerId=customerId;VideoMedia=media;
        TableId=song.ReportTableNumber;Stage=song.Stage;LocalFlag=song.LocalFlag;
        CanScore=song.ScoringEnabled;IsDisco=song.Types[0]==8;SongSpecies=song.SongSpecies;
        SingerName=song.Singer;
    }

    public SelectedPlaylistItem Copy()
    {
        var copy=(SelectedPlaylistItem)MemberwiseClone();
        // KmPlayListItem.copy leaves these at its constructor defaults.
        copy.DownloadState=200;copy.DownloadProgress=0;return copy;
    }
    public SelectedSong ToStoredSong()=>new(SongMetadata.Id,CanScore,CustomerId,TableId,Stage,
        PlayType,PlayName,PlayUrl,SingerName,PlayId,FlowId,CustomerContent);

    public static IReadOnlyList<SelectedPlaylistItem> Restore(IEnumerable<StoredSelectedSong> rows,
        Func<int,LocalSong?> lookup,Func<int,IReadOnlyList<SongMedia>> mediaLookup,
        Func<SongMedia,string?> localPath)
    {
        var items=new List<SelectedPlaylistItem>();
        foreach(var row in rows)
        {
            var entry=row.Song;var type=entry.Type;if(type is null)continue;
            var song=type is "normal" or "mdream" or "kmtrain" or "photomv"
                ?lookup(entry.SongId)
                :new LocalSong(entry.SongId,entry.Name??"","",0,"",new int[4],new int[4],new int[4],
                    0,1,0,"","",0,"",1,0);
            if(song is null)continue;
            song.ReportTableNumber=entry.TableId;song.Stage=entry.Stage;
            // Original Media.isVideo returns true for every media object. Prefer
            // the first resolved local file; otherwise keep the first metadata.
            SongMedia? video=null;
            foreach(var media in mediaLookup(song.Id))
            { video??=media;if(!string.IsNullOrEmpty(localPath(media))) { video=media;break; } }
            var item=new SelectedPlaylistItem(song,row.Sequence,entry.CustomerId,video)
            {
                PlayType=type,PlayName=entry.Name??"",PlayUrl=entry.Url??"",
                CustomerContent=entry.CustomerContent??"",SingerName=entry.Singer??"",
                PlayId=entry.PlayId??"",FlowId=entry.FlowId??""
            };
            item.InfoId=item.PlayType+"||"+item.PlayId+"||"+item.PlayName;items.Add(item);
        }
        return items;
    }
}
