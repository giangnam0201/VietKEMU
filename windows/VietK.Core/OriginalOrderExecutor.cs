namespace VietK.Core;

public sealed record OrderExecution(OrderDecision Decision,bool Handled,bool BackendSucceeded);

// PlayListManager.addSong's effects, from its original DEX (JADX duplicates
// branches). Callbacks are real backends/observers, not simulated admission.
public sealed class OriginalOrderExecutor(
    Func<SelectedPlaylistItem,bool> exists,
    Func<SelectedPlaylistItem,bool> appendLocal,
    Func<SelectedPlaylistItem,bool,bool,bool> topLocal,
    Func<SelectedPlaylistItem,bool> appendDownload,
    Func<SelectedPlaylistItem,bool,bool> topDownload,
    Action<SelectedPlaylistItem> synchronizeRate,
    Action<SelectedPlaylistItem> countOrder,
    Action<OrderDecision> rejected)
{
    public OrderExecution Execute(SelectedPlaylistItem? item,bool top,OrderContext context)
    {
        var candidate=item is null?null:new OrderCandidate(item.PlayType,item.LocalFlag,false);
        var early=OriginalOrderPolicy.Evaluate(candidate,top,context with { QueueCount=0 });
        if(early is OrderDecision.OrderingAvoided or OrderDecision.MissingItem or OrderDecision.MicroServicePaused
            or OrderDecision.MicroServiceDisconnected or OrderDecision.NoStorage or OrderDecision.CloudLocked)
        { rejected(early);return new(early,false,false); }
        item!.InfoId=item.PlayType+"||"+item.PlayId+"||"+item.PlayName;
        var alreadyQueued=exists(item);
        var decision=OriginalOrderPolicy.Evaluate(candidate! with { AlreadyQueued=alreadyQueued },top,context);
        bool success;
        switch(decision)
        {
            case OrderDecision.AppendLocal:success=appendLocal(item);break;
            case OrderDecision.TopLocal:success=topLocal(item,alreadyQueued,context.RepeatOrderingEnabled);break;
            case OrderDecision.AppendDownload:success=appendDownload(item);break;
            case OrderDecision.TopDownload:success=topDownload(item,alreadyQueued);break;
            default:rejected(decision);return new(decision,false,false);
        }
        if(success && item.PlayType is not ("youtube" or "midi" or "mixcloud" or "soundcloud"))synchronizeRate(item);
        countOrder(item);return new(decision,true,success);
    }

    public static SelectedPlaylistItem CreateSongItem(LocalSong song,string? customerId,
        IReadOnlyList<SongMedia> media,Func<SongMedia,string?> localPath)
    {
        SongMedia? video=null;
        foreach(var entry in media)
        { video??=entry;if(!string.IsNullOrEmpty(localPath(entry))) { video=entry;break; } }
        var path=video is null?null:localPath(video);
        return new(song,0,customerId,video)
        {
            PlayType=!string.IsNullOrEmpty(path) && path.EndsWith(".mid",StringComparison.Ordinal)?"midi":"normal",
            PlayName=song.Name,PlayUrl="",CustomerContent="",PlayId=song.Id.ToString(),SingerName=song.Singer,
            SingerIdsText=string.Join(",",song.SingerIds.Take(4).Where(id=>id>0))
        };
    }
    // MediaDAO.isNasSong reads the first UUID only. NAS tag matching is a
    // case-sensitive substring, not evidence of an existing mounted file.
    public static bool IsNasSong(IReadOnlyList<SongMedia> media)=>
        media.Count>0 && media[0].VolumeUuid?.Contains("nas",StringComparison.Ordinal)==true;
    public static string? FeedbackResource(OrderDecision decision)=>decision switch
    {
        OrderDecision.MicroServicePaused=>"try_to_connect_incognito",
        OrderDecision.MicroServiceDisconnected=>"try_to_connect_incognito_error",
        OrderDecision.NoStorage=>"order_song_no_disk_tip",
        OrderDecision.QueueLimit=>"order_song_num_max_tip",
        OrderDecision.NoNetwork=>"order_song_no_net_tip",
        _=>null // Duplicate/avoid/cloud lock are logged, without invented toasts.
    };
}

public sealed class OriginalReportTableRoute
{
    public const string PluginAction="com.evideo.kmbox.plugin.REPORTTABLE";
    public LocalSong? SelectedSong { get; private set; }
    public bool Top { get; private set; }
    public bool Check(LocalSong? song,bool top,bool activityAvailable,int matchingActivities,Action<string,int> launch)
    {
        if(song is null || !activityAvailable || matchingActivities<=0)return false;
        SelectedSong=song;Top=top;launch(PluginAction,2);return true;
    }
}
