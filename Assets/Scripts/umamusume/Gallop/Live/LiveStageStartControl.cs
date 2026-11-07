using System;

namespace Gallop.Live
{
    // Original array traversal with the engine SetActive boundary explicit.
    public static class LiveStageStartControl
    {
        public static void SetActive<T>(T[] objects,bool active,Action<T,bool> setter) where T:class
        {
            foreach(var obj in objects)
            {
                if(obj==null)throw new NullReferenceException();
                setter(obj,active);
            }
        }
    }
}
