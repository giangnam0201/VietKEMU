namespace VietK.Core;

// SearchInputKeyboardView's local Vietnamese panel (id 5). The caller supplies
// the UI scheduler; this preserves requestCount behavior, including callbacks
// that remain queued after Clear, instead of inventing a different debounce.
public sealed class VietnameseSearchInput(Action<int, Action> schedule)
{
    public string Text { get; private set; } = "";
    private int requestCount;
    public event Action? TextChanged;
    public event Action<string>? SpellRequested;

    public void Letter(string letters)
    {
        Text += letters; TextChanged?.Invoke(); requestCount++;
        schedule(200, () =>
        {
            if (requestCount > 1) { requestCount--; return; }
            SpellRequested?.Invoke(Text); requestCount = 0;
        });
    }

    public void Space()
    {
        if (Text.Length == 0 || Text[^1] == ' ') return;
        Text += " "; TextChanged?.Invoke(); SpellRequested?.Invoke(Text);
    }

    public void Back()
    {
        var hadText = Text.Length > 0;
        if (hadText) { Text = Text[..^1]; TextChanged?.Invoke(); }
        if (hadText && requestCount <= 1)
        { SpellRequested?.Invoke(Text); requestCount = 0; }
        else requestCount--;
    }

    public void Clear()
    {
        var notify = Text.Length > 0;
        Text = ""; requestCount = 0; TextChanged?.Invoke();
        if (notify) schedule(50, () => SpellRequested?.Invoke(""));
    }
}
