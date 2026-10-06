using System.Text.Json;

namespace VietK.Core;

// DefaultVolumeSettingDialog stores a separate default, not the live volume.
public sealed class OriginalDefaultVolumeSettings
{
    public const string Key="key_vga_room_default_volume";
    public const string SettingTip="Sau khi hệ thống khởi động và đóng, phòng sẽ sử dụng mức âm lượng này,\nkhuyến nghị từ 15, tối đa là 20.";
    private readonly string file;
    public int Volume { get; private set; }=15;
    public OriginalDefaultVolumeSettings(string directory)
    {
        file=Path.Combine(directory,"original-default-volume.json");
        if(!File.Exists(file))return;
        try
        {
            var value=JsonSerializer.Deserialize<Dictionary<string,int>>(File.ReadAllText(file))?.GetValueOrDefault(Key,15)??15;
            if(value is >=0 and <=20)Volume=value;
        }
        catch(JsonException) { }
    }
    public void Save(int volume)
    {
        if(volume is <0 or >20)throw new ArgumentOutOfRangeException(nameof(volume));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,JsonSerializer.Serialize(new Dictionary<string,int> { [Key]=volume }));File.Move(temporary,file,true);Volume=volume; }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
