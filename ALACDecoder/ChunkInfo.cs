/**
** Copyright (c) 2015 - 2018 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/

namespace ALACdotNET.Decoder
{
    /// <summary>
    /// Seems to hold metadata about a chunk (of bytes).
    /// </summary>
    internal class ChunkInfo
    {
        public ChunkInfo(int firstChunk, int samplesPerChunk, int sampleDescIndex)
        {
            FirstChunk = firstChunk;
            SamplesPerChunk = samplesPerChunk;
            SampleDescIndex = sampleDescIndex;
        }

        public int FirstChunk { get; }
        public int SamplesPerChunk { get; }
        public int SampleDescIndex { get; }
    }
}
