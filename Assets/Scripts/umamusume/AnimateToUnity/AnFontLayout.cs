using System;
using UnityEngine;

namespace AnimateToUnity
{
    // Original as_flGlobalData font rows; selection is _GetFontSizeParameter.
    [Serializable] public sealed class AnFontLayout
    {
        [Serializable] sealed class Table{public FontRow[] fonts;public int[] quality;}
        [Serializable] sealed class FontRow{public string name;public LocalizedRow[] localizations;}
        [Serializable] sealed class LocalizedRow{public string target,path;public bool common;public float[] quality;public AnFontLayout[] sizes;}
        public int size,sizeOffset;
        public float left,center,right,upper,middle,lower,lineSpacing,iconX,iconY,iconSize;
        static Table table;
        static void Load()
        {if(table==null)table=JsonUtility.FromJson<Table>(Resources.Load<TextAsset>("AnimateToUnity/FontLayouts").text);}
        // AnTextParameter._CreateHierarchy0x1fa7469..0x1fa74bc and the four
        // RootManager quality getters. The authored value is not a copy count.
        public static int OutlineQuality(string name,int fontSize,int authored,float offset)
        {
            Load();float[] local=null;
            foreach(var font in table.fonts)if(font.name==name){local=font.localizations[0].quality;break;}
            float Value(int index,float fallback)
            {
                if(local!=null&&index<local.Length&&local[index]>0f)return local[index];
                return table.quality!=null&&index<table.quality.Length?table.quality[index]:fallback;
            }
            if(fontSize>=Value(1,50))return (int)Value(0,20);
            if(offset>=Value(3,2))return (int)Value(2,16);
            return authored<5?6:authored<10?8:12;
        }
        public static AnFontLayout Get(string name,int fontSize)
        {
            Load();
            foreach(var font in table.fonts)if(font.name==name)
            {
                var rows=font.localizations[0].sizes;
                if(rows.Length==0)return new AnFontLayout();
                for(int i=0;i<rows.Length-1;i++)if(fontSize>=rows[i].size&&fontSize<rows[i+1].size)return rows[i];
                return rows[rows.Length-1];
            }
            return new AnFontLayout();
        }
        public Vector3 TextOffset(TextAnchor anchor,Vector2 range)
        {
            int x=(int)anchor%3,y=(int)anchor/3;
            return new Vector3(x==0?-range.x*.5f+left:x==1?center:range.x*.5f+right,
                y==0?range.y*.5f+upper:y==1?middle:-range.y*.5f+lower,0f);
        }
    }
}
