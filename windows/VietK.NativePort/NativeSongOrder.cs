using VietK.Core;

namespace VietK.NativePort;

public sealed record FirmwarePluginAction(string Package,string Component,string Kind,bool EnabledByManifest,string Action,bool HasData);
public sealed record FirmwareManifest(string App,string Package,string OriginalManifestSha256,string DecodedManifestSha256);
public sealed record OrderDependencies(FirmwareManifest[] Manifests,FirmwarePluginAction[] Actions,Dictionary<string,string> Feedback,string Scope)
{
    public int ReportTableActivityCount=>Actions.Where(entry=>entry.Action==OriginalReportTableRoute.PluginAction &&
        entry.Kind is "activity" or "activity-alias" && entry.EnabledByManifest && !entry.HasData)
        .Select(entry=>(entry.Package,entry.Component)).Distinct().Count();
}

// Grid order/Top routes retain the original plugin/NAS/admission dependencies.
// Toast rendering, order animation and report plugin execution are separate ports.
public sealed class NativeSongOrder(LocalSongDatabase local,OrderDependencies dependencies,
    OriginalOrderExecutor executor,Func<OrderContext> context,Func<SongMedia,string?> localPath,
    Func<bool> nasConnected,Action<string> feedback,Action<string,int> launchReport)
{
    public OriginalReportTableRoute ReportTable { get; }=new();
    public OrderExecution? LastExecution { get; private set; }
    public bool Request(int songId,bool top)
    {
        LastExecution=null;var song=local.GetSongById(songId);
        if(song is null)return false;
        if(ReportTable.Check(song,top,true,dependencies.ReportTableActivityCount,launchReport))return true;
        var media=local.GetMedia(songId);
        if(!nasConnected() && OriginalOrderExecutor.IsNasSong(media))
        { feedback(dependencies.Feedback["add_song_from_nas_error"]);return false; }
        var item=OriginalOrderExecutor.CreateSongItem(song,"",media,localPath);
        LastExecution=executor.Execute(item,top,context());return LastExecution.Handled;
    }
}
