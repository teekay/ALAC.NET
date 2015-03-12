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
    internal class ChunkInfo
    {
        public int first_chunk { get; set; }
        public int samples_per_chunk { get; set; }
        public int sample_desc_index { get; set; }
    }
}
