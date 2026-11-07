using System;
using Gallop;

namespace Gallop.Live
{
    // ModelController.SetFace/StoreFaceGroupSet. Submissions enter the existing
    // programmable eye/brow/mouth groups; stored parts also supply LipSync.Stop.
    public sealed class LiveSystemTextModelFace
    {
        readonly LiveSystemTextFaceParts faces;
        readonly Action<int,FaceParts[],float> submit;
        public readonly string[] Labels=new string[7];
        public readonly FacePartsSet Stored=new FacePartsSet();
        public Action<FaceTypeData> MouthSetChanged;
        public Action<string,string,string,string> EyeChanged;
        public LiveSystemTextModelFace(LiveSystemTextFaceParts faces,Action<int,FaceParts[],float> submit)
        {
            this.faces=faces;this.submit=submit;var baseline=faces.GetParts("Base");
            Stored._eyeR=baseline._eyeR;Stored._eyeL=baseline._eyeL;Stored._eyebrowR=baseline._eyebrowR;Stored._eyebrowL=baseline._eyebrowL;Stored._mouth=baseline._mouth;
        }
        public int Set(FaceTypeData face,float duration,int group=-1)
        {
            if(face==null)return int.MinValue;
            if(face.set_face_group==1){if(group==-1)group=0;else if(group!=0)return int.MinValue;}
            else if(face.set_face_group==2){if(group==-1)group=2;else if(group!=2)return int.MinValue;}
            if(group< -1||group>2)return int.MinValue;
            var parts=faces.GetParts(face.label);
            if(group==-1||group==0){submit(2,parts._eyeR,duration);submit(3,parts._eyeL,duration);}
            if(group==-1||group==1){submit(4,parts._eyebrowR,duration);submit(5,parts._eyebrowL,duration);}
            if(group==-1||group==2)submit(6,parts._mouth,duration);
            string oldR=Labels[2],oldL=Labels[3];
            if(group==-1)for(int i=0;i<Labels.Length;i++)Labels[i]=face.label;
            else if(group==0)Labels[2]=Labels[3]=face.label;
            else if(group==1)Labels[4]=Labels[5]=face.label;
            else Labels[6]=face.label;
            if(group==-1||group==0){Stored._eyeR=parts._eyeR;Stored._eyeL=parts._eyeL;}
            if(group==-1||group==1){Stored._eyebrowR=parts._eyebrowR;Stored._eyebrowL=parts._eyebrowL;}
            if(group==-1||group==2){Stored._mouth=parts._mouth;MouthSetChanged?.Invoke(face);}
            if(group==-1||group==0)EyeChanged?.Invoke(oldR,Labels[2],oldL,Labels[3]);
            return group;
        }
    }
}
