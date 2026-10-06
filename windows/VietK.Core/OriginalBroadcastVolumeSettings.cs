using System.Text.Json;

namespace VietK.Core;

// BroadcastVolumeSettingDialog / AudioManagerUtil: persisted settings are
// distinct from the cached broadcast volume used during this app session.
public sealed class OriginalBroadcastVolumeSettings
{
    public const string VolumeKey="broadcast_volume",MuteKey="is_broadcast_mute";
    public const string RestartTip="Hiệu quả sau khi khởi động lại";
    private readonly string file;
    public int Volume { get; private set; }=15;
    public bool Muted { get; private set; }
    public OriginalBroadcastVolumeSettings(string directory)
    {
        file=Path.Combine(directory,"original-broadcast-volume.json");
        if(!File.Exists(file))return;
        try
        {
            using var data=JsonDocument.Parse(File.ReadAllText(file));
            if(data.RootElement.TryGetProperty(VolumeKey,out var volume)&&volume.TryGetInt32(out var step)&&step is >=0 and <=20)Volume=step;
            if(data.RootElement.TryGetProperty(MuteKey,out var mute)&&mute.ValueKind is JsonValueKind.True or JsonValueKind.False)Muted=mute.GetBoolean();
        }
        catch(Exception error) when(error is JsonException or InvalidOperationException) { }
    }
    public void Save(int volume,bool muted)
    {
        if(volume is <0 or >20)throw new ArgumentOutOfRangeException(nameof(volume));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,JsonSerializer.Serialize(new Dictionary<string,object> { [VolumeKey]=volume,[MuteKey]=muted }));File.Move(temporary,file,true);Volume=volume;Muted=muted; }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
