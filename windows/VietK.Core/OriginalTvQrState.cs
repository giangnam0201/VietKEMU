namespace VietK.Core;

// QRCodeManager.showQRCode/changeQrCodeState/HideQRCodeTask. The mode-one
// change branch hides the image immediately in the supplied APK; delayed hiding
// is a separate call. Do not silently replace these with an invented policy.
public sealed class OriginalTvQrState
{
    public int Mode { get; private set; }
    public bool ImageVisible { get; private set; }=true;
    public long? HideAt { get; private set; }
    public long? SlideStartedAt { get; private set; }
    public void ChangeMode(int mode)
    {
        if(mode is <0 or >2)throw new ArgumentOutOfRangeException(nameof(mode));
        Mode=mode;
        if(mode==2) { HideImmediately();CancelDelay(); }
        else ImageVisible=mode==0;
    }
    public void Show()=>ImageVisible=Mode==0;
    public void HideImmediately()=>ImageVisible=false;
    public void ScheduleHide(long now)=>HideAt=now+20000;
    public void CancelDelay()=>HideAt=null;
    public void HideAnimated(long now)=>SlideStartedAt=now;
    public double Tick(long now,double panelWidth)
    {
        if(HideAt is long deadline && now>=deadline) { HideAt=null;HideAnimated(now); }
        if(SlideStartedAt is not long start)return 0;
        var elapsed=Math.Max(0,now-start);
        if(elapsed>=1000) { SlideStartedAt=null;ImageVisible=false;return 0; }
        return panelWidth*elapsed/1000d;
    }
}
