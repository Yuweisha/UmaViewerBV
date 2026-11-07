using CriWareFormats.Common;
using System;
using System.Collections.Generic;
using System.IO;

namespace CriWareFormats
{
    public sealed class AcbReader : IDisposable
    {
        private readonly Stream outerStream;
        private readonly long positionOffset;
        private readonly uint awbOffset;
        private readonly uint awbLength;

        private readonly AcbNameLoader acbNameLoader;

        public AcbReader(Stream acbStream) : this(acbStream, 0) { }

        public AcbReader(Stream acbStream, long offset)
        {
            outerStream = acbStream;
            positionOffset = offset;

            acbStream.Position = positionOffset;

            UtfTable utfTable = new UtfTable(acbStream, (uint)offset, out int rows, out string name);

            if (rows != 1 || !name.Equals("Header"))
                throw new InvalidDataException("No Header table.");

            if (!utfTable.Query(0, "AwbFile", out VLData awbValueData))
                throw new InvalidDataException("No embedded AWB file.");

            awbOffset = awbValueData.Offset;
            awbLength = awbValueData.Size;

            outerStream.Position = positionOffset;
            acbNameLoader = new AcbNameLoader(outerStream);
        }

        public AwbReader GetAwb()
        {
            return new AwbReader(new SpliceStream(outerStream, awbOffset, awbLength), true);
        }

        public string GetStreamingAwbName()
        {
            var header=new UtfTable(outerStream,(uint)positionOffset);
            if (!header.Query(0,"StreamAwbHash",out VLData data) || data.Size==0) return null;
            var table=new UtfTable(outerStream,data.Offset);
            return table.Query(0,"Name",out string name)?name:null;
        }

        public string GetWaveName(int waveId, int port, bool memory)
        {
            outerStream.Position = positionOffset;
            return acbNameLoader.LoadWaveName(waveId, port, memory);
        }

        public void Dispose()
        {
            outerStream.Dispose();
        }
    }
}
