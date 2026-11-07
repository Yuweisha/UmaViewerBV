using System;

namespace Gallop.Live
{
    // Original Generate/Get 0x4ea7350/0x4ea74d0. Draws are an explicit engine
    // boundary: a matching integer seed alone does not prove matching histories.
    public sealed class RandomRatioTable
    {
        private float[,] _values;
        private readonly Func<float,float,float> _drawFloat;
        private readonly Func<int,int,int> _drawInt;
        private readonly Action<int> _setSeed;
        public int MajorLength { get; private set; }
        public int MinorLength { get; private set; }
        public RandomRatioTable(Func<float,float,float> drawFloat = null,
            Func<int,int,int> drawInt = null, Action<int> setSeed = null)
        {
            _drawFloat = drawFloat ?? LiveRandomBoundary.RangeFloat;
            _drawInt = drawInt ?? LiveRandomBoundary.RangeInt;
            _setSeed = setSeed ?? UnityEngine.Random.InitState;
        }
        public void Generate(int majorLength,int minorLength,int randomSeed=0)
        {
            MajorLength=majorLength<2?1:majorLength;
            MinorLength=minorLength<2?1:minorLength;
            _values=new float[MajorLength,MinorLength];
            if(randomSeed!=0)_setSeed(randomSeed);
            for(int major=0;major<MajorLength;major++)
                for(int minor=0;minor<MinorLength;minor++)
                    _values[major,minor]=_drawFloat(0f,1f);
        }
        public float Get(int majorIndex,int minorIndex)
        {
            if(majorIndex<0||minorIndex<0)
            {
                majorIndex=_drawInt(0,MajorLength);
                minorIndex=_drawInt(0,MinorLength);
            }
            else
            {
                majorIndex%=MajorLength;
                minorIndex%=MinorLength;
            }
            return _values[majorIndex,minorIndex];
        }
    }
}
