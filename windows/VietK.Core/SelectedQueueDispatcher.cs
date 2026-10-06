namespace VietK.Core;

// PlayListDAOManager: independent PlayListHandler connection and FIFO messages.
public sealed class SelectedQueueDispatcher : IDisposable
{
    private readonly QueueDatabaseWorker<SelectedQueueCommand> worker;
    public SelectedQueueDispatcher(string statePath,Func<SelectedPlaylistItem,bool> scoreAvailable)
    {
        worker=new(statePath,"PlayListHandler",database=>
        { var store=new SelectedListStore(database);return command=>command.Apply(store,scoreAvailable); });
    }
    public Task Ready=>worker.Ready;
    public bool Post(SelectedQueueCommand command)=>worker.Post(command);
    public Task FlushAsync()=>worker.FlushAsync();
    public void Dispose()=>worker.Dispose();
}
