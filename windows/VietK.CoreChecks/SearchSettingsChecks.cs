using VietK.Core;

internal static class SearchSettingsChecks
{
    internal static void Run()
    {
        var folder=Path.Combine(Path.GetTempPath(),"vietk-search-check-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
            var settings=new OriginalSearchSettings(folder);var scheduled=new List<(int Delay,Action Clear)>();var clears=0;
            void Schedule(int delay,Action clear)=>scheduled.Add((delay,clear));
            settings.ScheduleClear(true,Schedule,()=>clears++);Require(!settings.ClearAfterOrder&&scheduled.Count==0,"Original clear-search default changed");
            settings.SetClearAfterOrder(true);Require(new OriginalSearchSettings(folder).ClearAfterOrder,"Clear-search preference did not survive restart");
            settings.ScheduleClear(false,Schedule,()=>clears++);Require(scheduled.Count==0,"Empty search scheduled a clear");
            var current="first";var cleared="";
            settings.ScheduleClear(true,Schedule,()=> { cleared=current;current="";clears++; });
            Require(scheduled.Single().Delay==500&&clears==0,"Original clear delay or immediate behavior differs");
            current="subsequently edited";settings.SetClearAfterOrder(false);scheduled.Single().Clear();
            Require(cleared=="subsequently edited"&&current==""&&clears==1,"Pending original clear was canceled by text edits or a preference change");
            settings.ScheduleClear(true,Schedule,()=>clears++);Require(scheduled.Count==1&&!new OriginalSearchSettings(folder).ClearAfterOrder,"Disabled preference scheduled a new clear or was not persisted");
            Console.WriteLine("Original clear-search default, persistence, empty guard, 500ms delay and current-text callback verified.");
        }
        finally
        {
            var temporary=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!Path.GetFullPath(folder).StartsWith(temporary,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(folder).StartsWith("vietk-search-check-",StringComparison.Ordinal))throw new InvalidOperationException("Unexpected search fixture cleanup path");
            Directory.Delete(folder,true);
        }
    }
}
