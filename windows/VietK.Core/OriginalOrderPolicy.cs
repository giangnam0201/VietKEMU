namespace VietK.Core;

public sealed record OrderCandidate(string PlayType,int LocalFlag,bool AlreadyQueued);
public sealed record OrderContext(bool OrderingAvoided=false,bool MicroServiceLinked=false,
    bool MicroServicePaused=false,bool NetworkConnected=false,bool TcpConnected=false,
    int ScannedVolumes=0,bool VietnamRegion=true,bool CloudLocked=false,
    bool RepeatOrderingEnabled=false,int QueueCount=0);

public enum OrderDecision
{
    OrderingAvoided,MissingItem,MicroServicePaused,MicroServiceDisconnected,NoStorage,
    CloudLocked,AlreadyQueued,QueueLimit,NoNetwork,AppendLocal,TopLocal,AppendDownload,TopDownload
}

// PlayListManager.addSong(top,item), checked against classes26.dex smali.
// This is the original gate/routing rule, not a simulated queue or downloader.
public static class OriginalOrderPolicy
{
    public static OrderDecision Evaluate(OrderCandidate? item,bool top,OrderContext context)
    {
        if(context.OrderingAvoided)return OrderDecision.OrderingAvoided;
        if(item is null)return OrderDecision.MissingItem;
        if(context.MicroServiceLinked && context.MicroServicePaused)return OrderDecision.MicroServicePaused;
        if(context.MicroServiceLinked && (!context.NetworkConnected || !context.TcpConnected))
            return OrderDecision.MicroServiceDisconnected;
        var serviceType=item.PlayType is "youtube" or "mixcloud" or "soundcloud";
        if(context.ScannedVolumes==0 && !serviceType && !context.MicroServiceLinked)return OrderDecision.NoStorage;
        if(item.LocalFlag==0 && !context.VietnamRegion && !context.MicroServiceLinked && context.CloudLocked)
            return OrderDecision.CloudLocked;
        var repeat=context.RepeatOrderingEnabled && item.PlayType=="normal";
        if(!repeat && item.AlreadyQueued && !top)return OrderDecision.AlreadyQueued;
        // Original maximum check is >299 before insertion. An existing Top
        // operation is permitted at the limit because it doesn't add an item.
        if(context.QueueCount>299 && (!item.AlreadyQueued || !top))return OrderDecision.QueueLimit;
        if(item.LocalFlag==0)
        {
            if(!context.NetworkConnected)return OrderDecision.NoNetwork;
            return top?OrderDecision.TopDownload:OrderDecision.AppendDownload;
        }
        return top?OrderDecision.TopLocal:OrderDecision.AppendLocal;
    }
}
