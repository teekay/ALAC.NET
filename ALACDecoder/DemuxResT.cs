/**
** Copyright (c) 2015 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
namespace ALACdotNET.Decoder
{
    /// <summary>
    /// Seems to hold various metadata about an ALAC file.
    /// Refactoring candidate - all these read-write properties need to be readonly,
    /// and the values to be either set via the constructor, or computed.
    /// </summary>
    internal class DemuxResT
    {
        public DemuxResT()
        {
        }

        public int FormatRead { get; set; }
        public int NumChannels { get; set; }
        public int SampleSize { get; set; }
        public int SampleRate { get; set; }
        public int Format { get; set; }
        public SampleInfo[] TimeToSample { get; set; } = new SampleInfo[16]; // not sure how many of these I need, so make 16
        public int NumTimeToSamples { get; set; }
        public int[] SampleByteSize { get; set; } = new int[0];
        public int CodecDataLength { get; set; }
        public int[] CodecData { get; set; } = new int[1024];
        public int[] Stco { get; set; }
        public ChunkInfo[] Stsc { get; set; }
        public int MdatLen { get; set; }
    }   
}