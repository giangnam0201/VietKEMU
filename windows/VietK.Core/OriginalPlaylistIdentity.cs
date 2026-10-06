namespace VietK.Core;

// PlayListManager.isExistInList scans the combined local + download list.
public static class OriginalPlaylistIdentity
{
    public static bool Exists(IEnumerable<SelectedPlaylistItem> entries,SelectedPlaylistItem? item)=>
        item is not null && entries.Any(entry=>item.PlayType switch
        {
            "youtube"=>entry.PlayUrl==item.PlayUrl,
            "mixcloud" or "soundcloud"=>entry.CloudKey is not null && entry.CloudKey==item.CloudKey,
            _=>entry.InfoId==item.InfoId
        });
}
