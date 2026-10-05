namespace VietK.Core;

public sealed record FragmentEntry(int Tag, IReadOnlyDictionary<string, string>? Arguments = null);

// FragmentManagerUtil.backupLastFragmentIndex / recoveryLastFragment. This manages
// history only; callers must provide a ported screen before navigating to it.
public sealed class FragmentHistory
{
    private readonly List<FragmentEntry> history = [];
    public FragmentEntry Current { get; private set; } = new(0);
    public IReadOnlyList<FragmentEntry> Entries => history.AsReadOnly();

    public void Reload(FragmentEntry next)
    {
        if (next.Tag == Current.Tag) { Current = next; return; }
        if (next.Tag == 0) history.Clear();
        else
        {
            var previousIndex = history.FindIndex(item => item.Tag == Current.Tag);
            if (previousIndex >= 0) history.RemoveRange(previousIndex, history.Count - previousIndex);
            history.Add(Current);
        }
        Current = next;
    }

    public void Back(bool youtubeEnabled = true, bool mixcloudEnabled = true, bool soundcloudEnabled = true)
    {
        // recoveryLastFragment removes the current tag and newer history first.
        // Without this, a second Back would revisit the page just left.
        var currentIndex = history.FindIndex(item => item.Tag == Current.Tag);
        if (currentIndex >= 0) history.RemoveRange(currentIndex, history.Count - currentIndex);
        var previous = history.Count > 0 ? history[^1] : new FragmentEntry(0);
        if ((previous.Tag == 34 && !youtubeEnabled) || (previous.Tag == 35 && !mixcloudEnabled)
            || (previous.Tag == 36 && !soundcloudEnabled)) previous = new(0);
        if (previous.Tag == 34) previous = previous with { Arguments = null };
        Reload(previous);
    }
}
