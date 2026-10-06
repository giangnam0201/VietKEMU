using System.Text.Json;

namespace VietK.Core;

// KeyName.KEY_CLEAR_SEARCH_TEXT; GeneralView defaults to false.
public sealed class OriginalSearchSettings
{
    public const string ClearKey="key_clear_search_text";
    public const int ClearDelayMilliseconds=500;
    private readonly string file;
    public bool ClearAfterOrder { get; private set; }
    public OriginalSearchSettings(string stateDirectory)
    {
        file=Path.Combine(stateDirectory,"original-search-settings.json");
        if(!File.Exists(file))return;
        try { ClearAfterOrder=JsonSerializer.Deserialize<Dictionary<string,bool>>(File.ReadAllText(file))?.GetValueOrDefault(ClearKey)??false; }
        catch(JsonException) { }
    }
    public void SetClearAfterOrder(bool enabled)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try
        { File.WriteAllText(temporary,JsonSerializer.Serialize(new Dictionary<string,bool> { [ClearKey]=enabled }));File.Move(temporary,file,true);ClearAfterOrder=enabled; }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
    public void ScheduleClear(bool hasText,Action<int,Action> schedule,Action clearCurrentSearch)
    {
        // Check preference/text at order time only. The original Handler callback
        // clears the current shared keyboard, including subsequently edited text.
        if(ClearAfterOrder&&hasText)schedule(ClearDelayMilliseconds,clearCurrentSearch);
    }
}
