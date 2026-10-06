namespace VietK.Core;

// Messages retain original PlayListDAOManager argument semantics. The host
// must enqueue them in order; observer/playback requests are separate effects.
public sealed record SelectedQueueCommand(int What,int Arg1=0,int Arg2=0,SelectedPlaylistItem? Item=null)
{
    public void Apply(SelectedListStore store,Func<SelectedPlaylistItem,bool> scoreAvailable)
    {
        switch(What)
        {
            case 1:store.DeleteSong(Arg1);break;
            case 2:store.DeleteSongBySongId(Arg1);break;
            case 3:store.TopSong(Arg1);break;
            case 4:if(Item is not null) { var entry=Item.ToStoredSong(scoreAvailable(Item));store.AddSong(entry);Item.CustomerId=entry.CustomerId; }break;
            case 5:store.Clear();break;
            case 6:store.SortLocalSong(Arg1,Arg2);break;
            default:throw new ArgumentOutOfRangeException(nameof(What));
        }
    }
}

public sealed class OriginalSelectedQueue
{
    private readonly List<SelectedPlaylistItem> items=new();
    private readonly Func<int,LocalSong?> lookup;
    private readonly Action<SelectedQueueCommand> post;
    private readonly Action changed,startPlay;
    public bool IsInitialized { get; private set; }
    public bool ClearOnInitialize { get; set; }=true;
    public OriginalSelectedQueue(Func<int,LocalSong?> lookup,Action<SelectedQueueCommand> post,
        Action changed,Action startPlay)
    { this.lookup=lookup;this.post=post;this.changed=changed;this.startPlay=startPlay; }
    public IReadOnlyList<SelectedPlaylistItem> Snapshot() { lock(items)return items.ToArray(); }
    public int Count { get { lock(items)return items.Count; } }

    public void Initialize(SelectedListStore store,Func<IReadOnlyList<SelectedPlaylistItem>> restore,
        Action<IReadOnlyList<SelectedPlaylistItem>>? linkedInitialization=null)
    {
        if(ClearOnInitialize)store.Clear();
        var saved=restore();
        foreach(var item in saved)
            if(item.PlayType is not ("youtube" or "mobile" or "mixcloud" or "soundcloud"))Add(item);
        linkedInitialization?.Invoke(saved);
        // Bytecode sets this AFTER the restore loop; Add deliberately does not
        // append or persist before initialization. Don't invent queue restore.
        IsInitialized=true;
    }

    public bool Add(SelectedPlaylistItem song)
    {
        var item=song.Copy();
        lock(items)
        {
            // Original uses reference comparisons here, unlike its flow switch.
            if(!ReferenceEquals(song.PlayType,"youtube") && !ReferenceEquals(song.PlayType,"mixcloud") &&
                !ReferenceEquals(song.PlayType,"soundcloud"))item.LocalFlag=lookup(item.SongMetadata.Id)?.LocalFlag??1;
            if(IsInitialized)
            {
                if(song.PlayType=="youtube")
                {
                    if(song.PlayUrl.Contains("https://www.youtube.com/",StringComparison.Ordinal))
                        song.PlayUrl=song.PlayUrl[(song.PlayUrl.IndexOf("?v=",StringComparison.Ordinal)+3)..];
                    item.FlowId=song.PlayUrl; // Original clone keeps its pre-normalization URL.
                }
                else if(song.PlayType is "soundcloud" or "mixcloud")item.FlowId=song.CloudKey??"";
                else item.FlowId=Guid.NewGuid().ToString();
                items.Add(item);
            }
            if(items.Count==1 && IsInitialized)startPlay();
        }
        if(IsInitialized) { post(new(4,Item:item));changed(); }return true;
    }

    public bool Top(SelectedPlaylistItem? song,bool exists,bool repeatNormal)
    {
        if(song is null)return false;
        lock(items)
        {
            var item=song.Copy();
            if(!exists || (repeatNormal && item.PlayType=="normal"))
            {
                var size=items.Count;var result=Add(item);
                return size<2?result:result|TopByIndex(size);
            }
            var index=item.PlayId=="-1"
                ?Find(item.CloudKey is { Length:>0 }?item.CloudKey:item.PlayName,
                    item.CloudKey is { Length:>0 }?entry=>entry.FlowId:entry=>entry.PlayName)
                :Find(item.PlayId,entry=>entry.PlayId);
            return TopByIndex(index);
        }
    }
    private int Find(string? key,Func<SelectedPlaylistItem,string> field)
    { if(string.IsNullOrEmpty(key))return 0;var index=items.FindIndex(entry=>field(entry)==key);return index<0?0:index; }

    public bool TopByIndex(int index)
    {
        lock(items)
        {
            if(index<=1 || index>=items.Count)return false;
            var item=items[index];items.RemoveAt(index);items.Insert(1,item);
            post(new(3,index+1));changed();return true;
        }
    }
    public bool SortByIndex(int source,int target)
    {
        lock(items)
        {
            if(source<1 || source>=items.Count || target<1 || target>=items.Count)return false;
            var item=items[source];items.RemoveAt(source);items.Insert(target,item);
            // Original forwards zero-based indices without Top's +1 conversion.
            post(new(6,source,target));changed();return true;
        }
    }
    public bool DeleteByIndex(int index,Action<string>? removeYoutube=null)
    {
        var result=false;
        lock(items)
        {
            if(index>=0 && index<items.Count)
            {
                post(new(1,index+1));if(items[index].PlayType=="youtube")removeYoutube?.Invoke(items[index].PlayUrl);
                items.RemoveAt(index);result=true;
            }
        }
        if(index==0)startPlay();changed();return result;
    }
    public bool Exists(SelectedPlaylistItem? item)
    {
        if(item is null)return false;
        lock(items)return items.Any(entry=>item.PlayType switch
        {
            "youtube"=>entry.PlayUrl==item.PlayUrl,
            "mixcloud" or "soundcloud"=>entry.CloudKey is not null && entry.CloudKey==item.CloudKey,
            _=>entry.InfoId==item.InfoId
        });
    }
    public void ClearWithoutNext() { lock(items)items.Clear();post(new(5)); }
}
