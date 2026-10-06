using VietK.Core;

internal static class MarqueeSettingsChecks
{
    internal static void Run()
    {
        var directory=Path.Combine(Path.GetTempPath(),"vietk-marquee-check-"+Guid.NewGuid().ToString("N"));
        try
        {
            var settings=new OriginalMarqueeSettings(directory);Require(settings.LocalText=="","Local marquee default differs");
            Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"tv-marquee.txt"),"Legacy local text");
            Require(settings.LocalText=="Legacy local text","Existing port marquee was lost");
            var text="Chào mừng\n<b>literal text</b>\t"+char.ConvertFromUtf32(0x1f3b5);
            settings.SaveLocal(text);Require(new OriginalMarqueeSettings(directory).LocalText==text,"Local marquee did not persist exactly");
            settings.SaveLocal(new string('x',240));Require(settings.LocalText.Length==240,"Original maximum was rejected");
            foreach(var invalid in new[]{new string('x',241),new string('x',239)+char.ConvertFromUtf32(0x1f3b5),"bad\0text"})
            { try { settings.SaveLocal(invalid);throw new InvalidDataException("Invalid marquee accepted"); }catch(ArgumentException) { } }
            Require(settings.LocalText.Length==240,"Rejected marquee changed saved text");settings.SaveLocal("");Require(settings.LocalText=="","Local marquee could not be cleared");
            Console.WriteLine("Local marquee legacy text, Unicode persistence, UTF-16 240 limit, rejected input and empty clearing verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(directory).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(directory).StartsWith("vietk-marquee-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected marquee fixture cleanup path");
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }
    private static void Require(bool value,string error) { if(!value)throw new InvalidDataException(error); }
}
