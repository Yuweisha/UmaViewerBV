using Gallop.Live;
namespace Gallop.Live.Cutt
{
    public partial class LiveTimelineControl
    {
        public event ProjectorUpdateInfoDelegate OnUpdateProjector;
        private sealed class ProjectorWork
        {
            public int lineHash;
            public int startFrame=-1;
            public float progressTime;
        }
        private ProjectorWork[] _projectorWorks;
        private void InitializeProjectorWork(int count)
        {
            if(_projectorWorks!=null&&_projectorWorks.Length>=count)return;
            // Native 4ef10a0 replaces every entry when growing the array.
            _projectorWorks=new ProjectorWork[count];
            for(int i=0;i<count;i++)_projectorWorks[i]=new ProjectorWork();
        }
        private void AlterUpdate_Projector(LiveTimelineWorkSheet sheet,int frame)
        {
            if(OnUpdateProjector==null)return;
            var groups=sheet.projecterList;
            InitializeProjectorWork(groups.Count);
            for(int i=0;i<groups.Count;i++)
            {
                var group=groups[i];var keys=group.keys;
                if(keys.HasAttribute(LiveTimelineKeyDataListAttr.Disable)||!keys.EnablePlayModeTimeline(_playMode))continue;
                var selected=keys.FindKeyCached(frame,true);
                var a=selected.key as LiveTimelineKeyProjectorData;
                if(a==null)continue;
                var b=keys.At(selected.index+1) as LiveTimelineKeyProjectorData;
                bool interpolate=b!=null&&b.IsInterpolateKey();
                float rate=interpolate?CalculateInterpolationValue(a,b,frame):0f;
                var info=new ProjectorUpdateInfo{progressTime=(frame-a.frame)/60f,nameHash=group.nameHash,
                    motionId=a.motionID,materialId=a.materialID,speed=a.speed,color1=a.color1,power=a.power,
                    position=a.position,rotate=a.rotate,size=a.size,LightBlendMode=a.LightBlendMode};
                if(interpolate)
                {
                    info.speed=CySpringWindParam.NativeLerp(a.speed,b.speed,rate);
                    for(int n=0;n<4;n++)info.color1[n]=CySpringWindParam.NativeLerp(a.color1[n],b.color1[n],rate);
                    info.power=CySpringWindParam.NativeLerp(a.power,b.power,rate);
                    for(int n=0;n<3;n++)info.position[n]=CySpringWindParam.NativeLerp(a.position[n],b.position[n],rate);
                    info.rotate=CySpringWindParam.NativeLerp(a.rotate,b.rotate,rate);
                    for(int n=0;n<2;n++)info.size[n]=CySpringWindParam.NativeLerp(a.size[n],b.size[n],rate);
                }
                var work=_projectorWorks[i];
                if(a.IsAnimContinuous())
                {
                    if(work.startFrame!=a.frame||work.lineHash!=group.nameHash)
                    {
                        work.lineHash=group.nameHash;
                        int firstFrame=a.frame,index=selected.index;
                        while(--index>=0)
                        {
                            var previous=keys.At(index) as LiveTimelineKeyProjectorData;
                            firstFrame=previous.frame;
                            if(!previous.IsAnimContinuous())break;
                        }
                        work.progressTime=(a.frame-firstFrame)/60f;
                    }
                    info.progressTime+=work.progressTime;
                }
                work.startFrame=a.frame;
                OnUpdateProjector(ref info);
            }
        }
    }
}
