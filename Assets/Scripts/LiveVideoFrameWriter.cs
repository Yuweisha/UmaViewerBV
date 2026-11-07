using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// Only byte buffers and pipe I/O run on the worker. Unity animation, physics
// and graphics APIs stay on their owning thread and retain frame ordering.
internal sealed class LiveVideoFrameWriter : IDisposable
{
    private readonly Stream stream;
    private readonly BlockingCollection<byte[]> available=new BlockingCollection<byte[]>(3);
    private readonly BlockingCollection<byte[]> pending=new BlockingCollection<byte[]>(3);
    private readonly CancellationTokenSource abort=new CancellationTokenSource();
    private readonly Task worker;
    private Exception failure;
    private int written;
    public int WrittenFrames => Volatile.Read(ref written);
    public Task Completion => worker??Task.CompletedTask;
    public double WriteMilliseconds {get;private set;}

    public LiveVideoFrameWriter(Stream destination,int frameBytes,bool parallel)
    {
        stream=destination;
        for(int i=0;i<3;i++)available.Add(new byte[frameBytes]);
        if(parallel)worker=Task.Run(Consume);
    }
    public byte[] Rent()
    {
        ThrowIfFailed();
        var buffer=available.Take(abort.Token);
        return buffer;
    }
    public void Submit(byte[] data)
    {
        ThrowIfFailed();
        if(worker==null)Write(data);
        else pending.Add(data,abort.Token);
    }
    private void Consume()
    {
        try {foreach(var data in pending.GetConsumingEnumerable(abort.Token))Write(data);}
        catch(Exception error)
        {
            if(!abort.IsCancellationRequested)failure=error;
            abort.Cancel();
        }
    }
    private void Write(byte[] data)
    {
        long start=Stopwatch.GetTimestamp();
        try { stream.Write(data,0,data.Length);Interlocked.Increment(ref written); }
        finally
        {
            WriteMilliseconds+=(Stopwatch.GetTimestamp()-start)*1000.0/Stopwatch.Frequency;
            available.TryAdd(data);
        }
    }
    public void Complete() {pending.CompleteAdding();}
    public void Abort() {abort.Cancel();}
    public void ThrowIfFailed()
    {if(failure!=null)throw new IOException("Video pipe write failed",failure);}
    public void Dispose()
    {
        abort.Cancel();
        worker?.GetAwaiter().GetResult();
        available.Dispose();pending.Dispose();abort.Dispose();
    }
}
