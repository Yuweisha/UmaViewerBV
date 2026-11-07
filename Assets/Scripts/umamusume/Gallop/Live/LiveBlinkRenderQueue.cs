using System;
using System.Collections.Generic;
using UnityEngine;

namespace Gallop.Live
{
    public static class LiveBlinkRenderQueue
    {
        // Original static constructor 0x58775b0 and Initialize 0x4e64674..
        // 0x4e64701 execute in native-blink-render-queue.json.
        private static readonly Dictionary<string,int> Offsets=new Dictionary<string,int>(StringComparer.Ordinal)
        {
            {"LightBlend",0},{"LightBlink",10},{"LightBlinkSimple",11},
            {"LightBlinkBlend",12},{"StageLightBlinkCutoff",13},{"StageLightBlinkFadeout",14},
            {"StageBeamLight",20},{"StageBeamLightCutoff",21},{"StageBeamLightFadeout",22}
        };
        public static bool TryGetQueue(string shaderName,out int queue)
        {
            string leaf=shaderName??string.Empty;int slash=leaf.LastIndexOf('/');
            if(slash>=0)leaf=leaf.Substring(slash+1);
            if(Offsets.TryGetValue(leaf,out int offset)){queue=3100+offset;return true;}
            queue=0;return false;
        }
        public static void Apply(Material material)
        {
            if(material!=null&&material.shader!=null&&TryGetQueue(material.shader.name,out int queue))
                material.renderQueue=queue;
        }
    }
}
