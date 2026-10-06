namespace VietK.Core;

// SelectedPullListView.onDraw: retain the last valid insertion target. Java
// integer division/remainder are intentionally preserved for partially scrolled
// rows; the marker may accept count, while the sort manager rejects that index.
public sealed class OriginalQueueDragPosition
{
    public int Target { get; private set; }
    public int MarkerY { get; private set; }
    public bool HasMarker { get; private set; }
    public bool Update(int dragY,int firstVisiblePosition,int firstVisibleTop,int count)
    {
        var insertTop=dragY/65*65+firstVisibleTop%65;
        var insertPosition=dragY-(dragY-insertTop)%65;
        var target=insertPosition/65+firstVisiblePosition;
        if(target==0 || target>count)return false;
        Target=target;MarkerY=insertPosition-11;HasMarker=true;return true;
    }
}
