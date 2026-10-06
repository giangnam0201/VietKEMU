using VietK.Core;

internal static class BarrageMotionChecks
{
    public static void Run()
    {
        void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
        Require(OriginalBarrageMotion.ScrollDuration(1280,4f/3)==10800,"TV density duration differs from the bytecode clamp/factor");
        Require(OriginalBarrageMotion.ScrollDuration(682,2)==4800,"Minimum duration clamp/factor differs");
        var motion=new OriginalBarrageMotion(1280,1293,10800);
        Require(motion.Left(0)==1280 && Math.Abs(motion.Left(5400)+6.5)<.01 && motion.Left(10800)==-1293,
            "Original R2L distance, midpoint or timeout differs");
        Require(motion.WillHit(motion,0,0,0),"Simultaneous scrolling messages were allowed to overlap");
        Require(!motion.WillHit(motion,0,6000,6000),"Separated messages cannot reuse a free line");
        Require(motion.WillHit(new(1280,3000,10800),0,6000,6000),"A wider following message can catch its predecessor");
        Require(!motion.WillHit(motion,0,10800,10800),"Expired messages still occupy a line");
        Console.WriteLine("Original barrage viewport/density duration, float R2L motion and catch-up collision verified against APK bytecode.");
    }
}
