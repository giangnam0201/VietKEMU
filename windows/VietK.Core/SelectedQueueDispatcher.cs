using System.Collections.Concurrent;
using Microsoft.Data.Sqlite;

namespace VietK.Core;

// PlayListDAOManager's single HandlerThread. Its connection belongs to the
// worker, independent of UI search/read connections to the same state file.
public sealed class SelectedQueueDispatcher : IDisposable
{
    private sealed record Work(SelectedQueueCommand? Command,TaskCompletionSource? Barrier=null);
    private readonly BlockingCollection<Work> pending=new();
    private readonly CancellationTokenSource stopped=new();
    private readonly object gate=new();
    private readonly Thread worker;
    private readonly TaskCompletionSource ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Exception? failure;
    private bool quitting;
    private bool disposed;
    public Task Ready=>ready.Task;

    public SelectedQueueDispatcher(string statePath,Func<SelectedPlaylistItem,bool> scoreAvailable)
    {
        var path=Path.GetFullPath(statePath);
        worker=new Thread(()=>Run(path,scoreAvailable)) { IsBackground=true,Name="PlayListHandler" };
        worker.Start();
    }
    public bool Post(SelectedQueueCommand command)
    {
        lock(gate)
        {
            if(quitting || failure is not null)return false;
            // Keep the original message object, not a snapshot. Flow and null
            // customer normalization occur on the same runtime item as Android.
            pending.Add(new(command));return true;
        }
    }
    public Task FlushAsync()
    {
        lock(gate)
        {
            if(failure is not null)return Task.FromException(failure);
            if(quitting)return Task.FromException(new ObjectDisposedException(nameof(SelectedQueueDispatcher)));
            var barrier=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            pending.Add(new(null,barrier));return barrier.Task;
        }
    }
    private void Run(string path,Func<SelectedPlaylistItem,bool> scoreAvailable)
    {
        try
        {
            using var database=new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource=path,Mode=SqliteOpenMode.ReadWrite,Pooling=false }.ToString());
            database.Open();var store=new SelectedListStore(database);ready.TrySetResult();
            foreach(var work in pending.GetConsumingEnumerable(stopped.Token))
            {
                if(work.Command is not null)work.Command.Apply(store,scoreAvailable);
                else work.Barrier!.TrySetResult();
            }
        }
        catch(OperationCanceledException) when(stopped.IsCancellationRequested) { }
        catch(Exception error)
        {
            lock(gate)failure=error;
            ready.TrySetException(error);System.Diagnostics.Trace.TraceError(error.ToString());
        }
        finally
        {
            lock(gate)
            {
                quitting=true;
                while(pending.TryTake(out var work))
                    if(failure is not null)work.Barrier?.TrySetException(failure);
                    else work.Barrier?.TrySetCanceled();
            }
        }
    }
    public void Dispose()
    {
        if(Thread.CurrentThread==worker)throw new InvalidOperationException("Worker cannot join itself");
        lock(gate)
        {
            if(!quitting) { quitting=true;pending.CompleteAdding();stopped.Cancel(); }
        }
        // Looper.quit drops pending messages; tests/host may explicitly flush
        // first when they need durable completion before shutdown.
        worker.Join();
        lock(gate)if(!disposed) { disposed=true;pending.Dispose();stopped.Dispose(); }
    }
}
