using System.Runtime.CompilerServices;
using System.Windows.Controls;
using System.Windows.Threading;
using VietK.Core;

namespace VietK.NativePort;

// Android's shared SearchBySpellManager view is resolved when the delayed
// callback runs, rather than clearing a retained screen that is no longer shown.
public sealed class NativeSearchOptions(OriginalSearchSettings settings,Func<Canvas?> currentPanel) : IDisposable
{
    private sealed record SearchTarget(Func<string> Text,Action Clear);
    private readonly ConditionalWeakTable<Canvas,SearchTarget> targets=new();
    private readonly HashSet<DispatcherTimer> timers=[];
    public bool ClearAfterOrder=>settings.ClearAfterOrder;
    public void SetClearAfterOrder(bool enabled)=>settings.SetClearAfterOrder(enabled);
    public void Register(Canvas panel,Func<string> text,Action clear)
    { targets.Remove(panel);targets.Add(panel,new(text,clear)); }
    public void Ordered()
    {
        var panel=currentPanel();var hasText=panel is not null&&targets.TryGetValue(panel,out var target)&&target.Text().Length>0;
        settings.ScheduleClear(hasText,(delay,callback)=>
        {
            var timer=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(delay) };
            timer.Tick+=(_,_)=> { timer.Stop();timers.Remove(timer);callback(); };timers.Add(timer);timer.Start();
        },()=> { if(currentPanel() is { } active&&targets.TryGetValue(active,out var current))current.Clear(); });
    }
    public void Dispose() { foreach(var timer in timers)timer.Stop();timers.Clear(); }
}
