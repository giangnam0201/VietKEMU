using VietK.Core;

internal static class BarrageRetainerChecks
{
    public static void Run()
    {
        void Require(bool value,string message) { if(!value)throw new InvalidDataException(message); }
        var motion=new OriginalBarrageMotion(1280,1293,10800);
        OriginalBarrageRetainer.Item Item(long start,float height)=>new(motion,start,height);
        var retainer=new OriginalBarrageRetainer(500);
        var a=Item(0,90);var b=Item(0,120);var c=Item(0,40);
        Require(retainer.Place(a,1).Accepted && a.Top==0,"First original barrage row did not start at the top");
        Require(retainer.Place(b,1).Accepted && b.Top==90 && retainer.Place(c,1).Accepted && c.Top==210,
            "Barrage retention substituted fixed row heights for measured bottoms");
        var reused=Item(6000,50);var placement=retainer.Place(reused,6001);
        Require(placement.Accepted && placement.Top==0 && placement.Lines==1 && !placement.Overlap && retainer.Count==3,
            "A clear row did not replace its previous representative");
        var next=Item(6000,60);placement=retainer.Place(next,6001);
        Require(placement.Accepted && placement.Top==50 && placement.Lines==2 && retainer.Count==3,
            "Reused rows did not preserve the original previous-bottom placement");
        retainer.Clear();Require(retainer.Count==0,"Original retainer clear retained row references");
        retainer=new(720);
        for(var i=0;i<9;i++)Require(retainer.Place(Item(0,74),1).Accepted,"Visible original TV row was filtered prematurely");
        placement=retainer.Place(Item(0,74),1);
        Require(!placement.Accepted && placement.VerticalEdge && placement.Overlap && retainer.Count==9,
            "Vertical overflow must reject a colliding message without clearing existing rows");
        retainer=new(720);
        for(var i=0;i<10;i++)Require(retainer.Place(Item(0,60),1).Accepted,"Ten-line filter rejected an allowed original row");
        placement=retainer.Place(Item(0,60),1);
        Require(!placement.Accepted && placement.Lines==10 && !placement.VerticalEdge,"Original maximum-lines threshold differs");
        // Original removal happens BEFORE filtering, even when maximum-lines
        // rejects a replacement. Preserve this observable retention side effect.
        retainer=new(720,1);Require(retainer.Place(Item(0,60),1).Accepted,"Single-row setup failed");
        placement=retainer.Place(Item(6000,60),6001);
        Require(!placement.Accepted && placement.Lines==1 && retainer.Count==0,"Pre-filter representative removal was repaired instead of ported");
        retainer=new(100,10,false);
        Require(retainer.Place(Item(0,74),1).Accepted,"Unfiltered original row setup failed");
        placement=retainer.Place(Item(0,74),1);
        Require(placement.Accepted && placement.VerticalEdge && placement.Top==0 && retainer.Count==1,
            "Permitted vertical-edge overwrite did not clear and restart the original retainer");
        Console.WriteLine("Original barrage measured-height retention, replacement, pre-filter removal, maximum lines, vertical overflow and overwrite verified against bytecode.");
    }
}
