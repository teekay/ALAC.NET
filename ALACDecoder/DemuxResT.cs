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
    class DemuxResT
    {
        public int format_read { get; set; }

        public int num_channels { get; set; }
        public int sample_size { get; set; }
        public int sample_rate { get; set; }
        public int format { get; set; }
        //public int[] buf { get; set; } = new int[1024 * 80];
        public SampleInfo[] time_to_sample { get; set; } = new SampleInfo[16];
        public int num_time_to_samples { get; set; }
        public int[] sample_byte_size { get; set; } = new int[0];
        public int codecdata_len { get; set; }
        public int[] codecdata { get; set; } = new int[1024];
        public int[] stco { get; set; }
        public ChunkInfo[] stsc { get; set; }
        public int mdat_len { get; set; }

        public DemuxResT()
        {
            // not sure how many of these I need, so make 16
            for (int i = 0; i < 16; i++)
            {
                time_to_sample[i] = new SampleInfo();
            }
        }

    }

    class SampleInfo
    {
        public int sample_count { get; set; }
        public int sample_duration { get; set; }
    }

}
