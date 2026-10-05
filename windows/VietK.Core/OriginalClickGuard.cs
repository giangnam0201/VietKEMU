namespace VietK.Core;

// NoDoubleClickListener.onClick: strict >500 ms, or reset when the clock goes
// backwards. Rejected clicks do not update the last accepted timestamp.
public sealed class OriginalClickGuard
{
    private long lastClick;
    public bool TryClick(long currentTime)
    {
        var interval = currentTime - lastClick;
        if (interval <= 500 && interval >= 0) return false;
        lastClick = currentTime;
        return true;
    }
}
