namespace VietK.Core;

// SetBroadcastFromUsb: the removable source is copied to kmbox/video/Demo.mp4.
public sealed class OriginalUsbBroadcastStore(string stateDirectory)
{
    private readonly string root=Path.GetFullPath(stateDirectory);
    private readonly SemaphoreSlim operation=new(1,1);
    public string DestinationPath=>Path.Combine(root,"kmbox","video","Demo.mp4");
    public async Task<bool> ImportAsync(string source,Action? beforePublish=null)
    {
        await operation.WaitAsync();string? temporary=null;
        try
        {
            EnsureDestination();source=Path.GetFullPath(source);
            if(!File.Exists(source)||new FileInfo(source).Length==0)return false;
            if(string.Equals(source,DestinationPath,StringComparison.OrdinalIgnoreCase))return true;
            temporary=Path.Combine(Path.GetDirectoryName(DestinationPath)!,".Demo-"+Guid.NewGuid().ToString("N")+".tmp");
            var staged=temporary;
            await Task.Run(()=>
            {
                using var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read);
                using var output=new FileStream(staged,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                var expected=input.Length;input.CopyTo(output);output.Flush(true);
                if(output.Length!=expected)throw new IOException("Incomplete idle video copy");
            });
            EnsureDestination();beforePublish?.Invoke();File.Move(temporary,DestinationPath,true);temporary=null;
            return true;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException or ArgumentException)
        { return false; }
        finally
        {
            try { if(temporary is not null&&File.Exists(temporary))File.Delete(temporary); }
            finally { operation.Release(); }
        }
    }
    public bool Delete()
    {
        if(!operation.Wait(0))return false;
        try { EnsureDestination();File.Delete(DestinationPath);return true; }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { return false; }
        finally { operation.Release(); }
    }
    private void EnsureDestination()
    {
        var directory=root;
        foreach(var part in new[]{"","kmbox","video"})
        {
            if(part.Length>0)directory=Path.Combine(directory,part);
            if(Directory.Exists(directory)&&(File.GetAttributes(directory)&FileAttributes.ReparsePoint)!=0)
                throw new IOException("Idle video directory cannot be a link");
            Directory.CreateDirectory(directory);
        }
        if(File.Exists(DestinationPath)&&(File.GetAttributes(DestinationPath)&FileAttributes.ReparsePoint)!=0)
            throw new IOException("Idle video destination cannot be a link");
    }
}
