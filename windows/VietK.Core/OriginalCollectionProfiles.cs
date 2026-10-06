using System.Text;
using System.Text.Json;

namespace VietK.Core;

public enum CollectionLoginResult { Success,InvalidLength,InvalidFilename,WrongPassword,StorageError }
public enum CollectionToggleResult { Added,Removed,LoginRequired,LimitReached,InvalidSong,StorageError }

// CollectInputPwdView + CollectFileManager + MuiltCollectListManager.
// These are local collection profiles, independent of cloud music entitlement.
public sealed class OriginalCollectionProfiles
{
    private const string Separator="-VietK-";
    private readonly string directory,preferences;
    private readonly List<int> songs=[];
    public string CurrentUser { get; private set; }="";
    public event Action? Changed;
    public OriginalCollectionProfiles(string stateDirectory)
    {
        directory=Path.Combine(stateDirectory,"kmbox","muilt_collect");
        preferences=Path.Combine(stateDirectory,"collection-login.json");
        Directory.CreateDirectory(directory);
        try { if(File.Exists(preferences))CurrentUser=JsonSerializer.Deserialize<string>(File.ReadAllText(preferences))??"";Load(); }
        catch(Exception error) when(error is IOException or JsonException or UnauthorizedAccessException) { CurrentUser="";songs.Clear(); }
    }
    public IReadOnlyList<int> Snapshot()=>songs.ToArray();
    public bool Contains(int id)=>songs.Contains(id);
    public static string Calibrate(string value)=>value.Replace('/','_');
    private static bool SafePart(string value)=>!value.Contains(Separator,StringComparison.Ordinal)&&
        !value.Any(c=>char.IsControl(c)||"\\:*?\"<>|".Contains(c))&&!value.EndsWith('.')&&!value.EndsWith(' ');
    private string? Find(string user)=>Directory.EnumerateFiles(directory).FirstOrDefault(path=>
        Path.GetFileName(path).Split(Separator,StringSplitOptions.None) is { Length:>=2 } pieces && pieces[0]==user);
    public CollectionLoginResult Login(string user,string password)
    {
        user=Calibrate(user);password=Calibrate(password);
        if(user.Length is <4 or >12 || password.Length is <4 or >12)return CollectionLoginResult.InvalidLength;
        if(!SafePart(user)||!SafePart(password))return CollectionLoginResult.InvalidFilename;
        try
        {
            var existing=Find(user);
            if(existing is null)using(File.Open(Path.Combine(directory,user+Separator+password),FileMode.CreateNew,FileAccess.Write)) { }
            else if(!Directory.EnumerateFiles(directory).Any(path=>Path.GetFileName(path)==user+Separator+password))return CollectionLoginResult.WrongPassword;
            File.WriteAllText(preferences,JsonSerializer.Serialize(user));CurrentUser=user;Load();Changed?.Invoke();
            return CollectionLoginResult.Success;
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException) { return CollectionLoginResult.StorageError; }
    }
    public void Logout()
    { File.WriteAllText(preferences,JsonSerializer.Serialize(""));CurrentUser="";songs.Clear();Changed?.Invoke(); }
    private void Load()
    {
        songs.Clear();if(CurrentUser.Length==0)return;
        if(Find(CurrentUser) is not { } path)return;
        foreach(var line in File.ReadLines(path))if(int.TryParse(line,out var id)&&id>0&&!songs.Contains(id))songs.Add(id);
    }
    public CollectionToggleResult Toggle(int id)
    {
        if(id<=0)return CollectionToggleResult.InvalidSong;
        if(CurrentUser.Length==0)return CollectionToggleResult.LoginRequired;
        var old=songs.ToArray();var index=songs.IndexOf(id);var added=index<0;
        if(added&&songs.Count>=150)return CollectionToggleResult.LimitReached;
        if(added)songs.Add(id);else songs.RemoveAt(index);
        try
        {
            if(Find(CurrentUser) is not { } path)throw new IOException();
            var pending=Path.Combine(directory,".pending-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(var writer=new StreamWriter(pending,false,new UTF8Encoding(false)) { NewLine="\n" })
                    foreach(var song in songs)writer.WriteLine(song);
                File.Move(pending,path,true);
            }
            finally { if(File.Exists(pending))File.Delete(pending); }
        }
        catch(Exception error) when(error is IOException or UnauthorizedAccessException)
        { songs.Clear();songs.AddRange(old);return CollectionToggleResult.StorageError; }
        Changed?.Invoke();return added?CollectionToggleResult.Added:CollectionToggleResult.Removed;
    }
    public IReadOnlyList<LocalSong> Visible(Func<int,LocalSong?> lookup,SongPage page,SongQueryContext context)
    {
        if(page.Index<0||page.Size<=0)throw new ArgumentOutOfRangeException(nameof(page));
        return songs.Skip(checked(page.Index*page.Size)).Take(page.Size).Select(lookup).Where(song=>song is not null&&
            (song.LocalFlag!=0 || (context.OnlineNamesEnabled&&context.DataCenterConnected&&song.HasRemote!=-1))&&
            (context.PslEnabled||song.IsPsl==0)).Cast<LocalSong>().ToArray();
    }
}
