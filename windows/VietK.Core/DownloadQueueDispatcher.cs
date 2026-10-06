namespace VietK.Core;

// DownListDAOManager: separate SongDownListHandler connection and FIFO messages.
public sealed class DownloadQueueDispatcher : IDisposable
{
    private readonly QueueDatabaseWorker<DownloadQueueCommand> worker;
    public DownloadQueueDispatcher(string statePath)
    {
        worker=new(statePath,"SongDownListHandler",database=>
        { var store=new DownloadListStore(database);store.UpgradeSchema();return store.Apply; });
    }
    public Task Ready=>worker.Ready;
    public bool Post(DownloadQueueCommand command)=>worker.Post(command);
    public Task FlushAsync()=>worker.FlushAsync();
    public void Dispose()=>worker.Dispose();
}
