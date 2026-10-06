namespace VietK.Core;

// MarqueeSettingDialog.MAX_INPUT_WORD counts UTF-16 units like String.length.
// Reuse the port's existing local text file rather than losing saved messages.
public sealed class OriginalMarqueeSettings(string directory)
{
    public const int MaximumLength=240;
    private readonly string file=Path.Combine(directory,"tv-marquee.txt");
    public string LocalText=>File.Exists(file)?File.ReadAllText(file):"";
    public void SaveLocal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if(text.Length>MaximumLength||text.Any(c=>char.IsControl(c)&&c is not ('\n' or '\r' or '\t')))throw new ArgumentException("Invalid local marquee text",nameof(text));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        var temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
        try { File.WriteAllText(temporary,text);File.Move(temporary,file,true); }
        finally { if(File.Exists(temporary))File.Delete(temporary); }
    }
}
