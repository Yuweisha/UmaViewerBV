using System.Collections.Generic;
using Gallop.Live.Cutt;

namespace Gallop.Live
{
    public partial class StageController
    {
        List<BgLiveWave> _bgLiveWaveList;
        void InitializeWaveStagePart(UnityEngine.GameObject instance)
        {
            var waves=instance.GetComponentsInChildren<BgLiveWave>();
            foreach(var wave in waves)wave.Initialize();
            if(_bgLiveWaveList==null)_bgLiveWaveList=new List<BgLiveWave>();
            _bgLiveWaveList.AddRange(waves);
        }
        void BindWaveTimeline(LiveTimelineControl control)
        {control.OnUpdateWaveObject-=UpdateWaveParam;control.OnUpdateWaveObject+=UpdateWaveParam;}
        void UnbindWaveTimeline(LiveTimelineControl control)
        {if(control!=null)control.OnUpdateWaveObject-=UpdateWaveParam;}
        public void UpdateWaveParam(ref WaveObjectUpdateInfo info)
        {if(_bgLiveWaveList!=null)foreach(var wave in _bgLiveWaveList)wave.UpdateParam(ref info);}
        public void UpdateWaveTime(float time)
        {if(_bgLiveWaveList!=null)foreach(var wave in _bgLiveWaveList)wave.AlterUpdate(time);}
    }
}
