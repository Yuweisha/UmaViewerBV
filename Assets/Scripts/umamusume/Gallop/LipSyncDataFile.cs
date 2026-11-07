using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Gallop
{
    // LipSyncDataFile.SetLipSyncPackFile (0x563fd70). Pack and row versions
    // are independent; byte/short offsets intentionally retain the old layouts.
    public sealed class LipSyncDataFile
    {
        public sealed class LipSyncData
        {
            public int Time,MouthId,MouthId2,Volume;
            public float Decibel;
            public float[] VowelWeightArray=new float[5];
        }
        public readonly byte[] magicNumber=new byte[4];
        public int fileSize,dataOffset,dataSize,dataCount,version,voiceTime,dataId,gender,variation,lipSyncInterval;
        public LipSyncData[] datas;
        public static void SetLipSyncPackFile(TextAsset asset,List<LipSyncDataFile> target)=>Parse(asset.bytes,target);
        public static void Parse(byte[] bytes,List<LipSyncDataFile> target)
        {
            target.Clear();
            int count=BitConverter.ToInt32(bytes,8),table=BitConverter.ToInt16(bytes,12),packVersion=BitConverter.ToInt16(bytes,14);
            if(packVersion<0||packVersion>2)throw new InvalidDataException("Unsupported native lip-sync pack version: "+packVersion);
            int stride=packVersion==0?8:16;
            for(int index=0;index<count;index++)
            {
                int slot=unchecked(table+index*stride),id=packVersion==0?index:BitConverter.ToInt32(bytes,slot);
                int start=BitConverter.ToInt32(bytes,slot+(packVersion==0?0:4));
                var file=new LipSyncDataFile{dataId=id,gender=packVersion==0?0:BitConverter.ToInt16(bytes,slot+12),
                    variation=packVersion==2?BitConverter.ToInt16(bytes,slot+14):0};
                Array.Copy(bytes,start,file.magicNumber,0,4);
                file.fileSize=BitConverter.ToInt32(bytes,start+4);file.dataOffset=BitConverter.ToInt16(bytes,start+8);
                file.dataSize=BitConverter.ToInt16(bytes,start+10);file.dataCount=BitConverter.ToInt16(bytes,start+12);
                file.version=BitConverter.ToInt16(bytes,start+14);
                if(file.version>=1)file.voiceTime=BitConverter.ToInt32(bytes,start+16);
                file.datas=new LipSyncData[file.dataCount];int seek=unchecked(start+file.dataOffset);
                for(int row=0;row<file.dataCount;row++)
                {
                    var data=new LipSyncData();file.datas[row]=data;
                    if(file.version==2)
                    {
                        data.Time=BitConverter.ToInt32(bytes,seek);seek+=4;
                        var sorted=new KeyValuePair<int,float>[5];
                        for(int vowel=0;vowel<5;vowel++)
                        {float value=BitConverter.ToSingle(bytes,seek);seek+=4;data.VowelWeightArray[vowel]=value;sorted[vowel]=new KeyValuePair<int,float>(vowel,value);}
                        data.Decibel=BitConverter.ToSingle(bytes,seek);seek+=8;
                        Array.Sort(sorted,(a,b)=>a.Value>b.Value?-1:1);
                        for(int vowel=0;vowel<2;vowel++)
                        {
                            int mouth=sorted[vowel].Value<.001f?0:sorted[vowel].Key+1;
                            if(vowel==0)data.MouthId=mouth;else data.MouthId2=mouth;
                        }
                    }
                    else if(file.version==0||file.version==1)
                    {
                        data.Time=BitConverter.ToInt32(bytes,seek);data.MouthId=bytes[seek+4];
                        data.Volume=BitConverter.ToInt16(bytes,seek+(file.version==0?5:6));seek+=8;
                        data.Decibel=ConvertVolumeToDecibel(data.Volume);
                    }
                }
                file.lipSyncInterval=file.dataCount>1?unchecked(file.datas[1].Time-file.datas[0].Time):42;
                if(file.version<1)file.voiceTime=unchecked(file.datas[file.dataCount-1].Time+file.lipSyncInterval);
                target.Add(file);
            }
        }
        public static float ConvertVolumeToDecibel(int volume)
            =>Live.Cutt.LiveTimelineControl.MonitorFloat32(Mathf.Log10(Live.Cutt.LiveTimelineControl.MonitorFloat32(Mathf.Max(volume,.1f)/10000f))*20f);
    }
}
