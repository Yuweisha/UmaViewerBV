using System;

namespace Gallop.Live
{
    public sealed class RandomTable<T>
    {
        private bool _bInitialized;
        private Func<int,int,T> _fnGenerator;
        private int _bucketCount,_stackSize,_curStackPtr;
        private T[,] _arrRandomValue=new T[0,0];
        public bool isInitialized=>_bInitialized;
        public int bucketCount=>_bucketCount;
        public int stackSize=>_stackSize;
        public void Build(int bucketCount,int stackSize,Func<int,int,T> generator)
        {
            _stackSize=stackSize;_bucketCount=unchecked(bucketCount-1);_fnGenerator=generator;
            _arrRandomValue=new T[_bucketCount,_stackSize];
            for(int bucket=0;bucket<_bucketCount;bucket++)
                for(int stack=0;stack<_stackSize;stack++)
                    _arrRandomValue[bucket,stack]=_fnGenerator(bucket+1,stack);
            _bInitialized=true;
        }
        public void Update(){_curStackPtr=unchecked(_curStackPtr+1)%_stackSize;}
        public T this[int bucket]=>!_bInitialized||bucket<1||_bucketCount<bucket?default(T):_arrRandomValue[bucket-1,_curStackPtr];
    }
}
