namespace VietK.Core;

// APK classes11.dex: BaseDanmakuParser.getViewportSizeFactor,
// DanmakuFactory.updateViewportState, Duration.setFactor, R2LDanmaku.measure.
public sealed record OriginalBarrageMotion(int ViewWidth,float PaintWidth,long Duration)
{
    public static long ScrollDuration(int width,float density,float factor=1.2f)
    {
        if(width<=0 || density<=.6f || !float.IsFinite(density) || factor<=0 || !float.IsFinite(factor))
            throw new ArgumentOutOfRangeException(nameof(width));
        var initial=(long)(3800f*((1f/(density-.6f)*width)/682f));
        return (long)(Math.Clamp(initial,4000,9000)*factor);
    }
    public float Left(long elapsed)
    {
        if(elapsed>=Duration)return -PaintWidth;
        var distance=(int)(ViewWidth+PaintWidth);var step=distance/(float)Duration;
        return ViewWidth-elapsed*step;
    }
    public bool WillHit(OriginalBarrageMotion next,long previousStart,long nextStart,long now)
    {
        // DanmakuUtils.willHitInDuration for two scrolling items.
        var gap=nextStart-previousStart;
        if(now>=previousStart+Duration)return false;
        if(gap<0)return true;
        if(gap>=next.Duration)return false;
        return Left(now-previousStart)+PaintWidth>next.Left(now-nextStart) ||
            Left(Duration)+PaintWidth>next.Left(previousStart+Duration-nextStart);
    }
}
