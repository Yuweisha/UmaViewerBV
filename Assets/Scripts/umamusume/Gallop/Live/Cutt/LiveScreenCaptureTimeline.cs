using System;

namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl
    {
        Action<int,float> _screenCaptureExecute;
        int _screenCaptureFrame = -1;
        public void AddExecuteScreenCapture(Action<int,float> execute) => _screenCaptureExecute += execute;
        // Native 0x504a720: latest key, once per authored key.frame. It is not
        // an exact-frame equality test, so a dropped playback frame still fires.
        void AlterUpdate_ScreenCapture(LiveTimelineWorkSheet sheet,int frame)
        {
            var keys=sheet?.ScreenCaptureDataList;
            if(_screenCaptureExecute==null||keys==null)return;
            var key=keys.FindKeyCached(frame,true).key as LiveTimelineKeyScreenCaptureData;
            if(key==null||key.frame==_screenCaptureFrame)return;
            _screenCaptureExecute(key.CaptureId,key.CaptureScale);
            _screenCaptureFrame=key.frame;
        }
    }
}
