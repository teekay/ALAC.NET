/**
** Copyright (c) 2015 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
using System;
using System.IO;
using System.Diagnostics;

namespace ALACdotNET.Decoder
{
    /// <summary>
    /// AlacContext takes responsibility for the decoding of an ALAC stream
    /// and provides an interface to read the converted bytes
    /// </summary>
    public class AlacContext : IDisposable
    {
        #region Private members
        DemuxResT demuxRes;
        AlacFile alac;
        BinaryReader inputStream;
        MyStream myStream;
        int currentSampleBlock;
        int offset;
        byte[] readBuffer = new byte[1024 * 80]; // sample big enough to hold any input for a single alac frame
        int[] formatBuffer= new int[1024 * 80];
        byte[] outputBufer = new byte[1024 * 80];
        bool disposeStream;
        int destBufferSize = 1024 * 24 * 3; // 24kb buffer = 4096 frames = 1 alac sample (we support max 24bps)
        #endregion

        #region Properties
        /// <summary>
        /// Points to the last sample read - can be used to determine position
        /// </summary>
        internal int LastSampleNumber { get; private set; }
        #endregion

        /// <summary>
        /// Initialize this AlacContext with a Stream
        /// </summary>
        /// <param name="baseStream">Stream to read from</param>
        /// <param name="disposeStream">Whether to dispose the stream when disposing this object</param>
        public AlacContext(Stream baseStream, bool disposeStream) : this(baseStream)
        {
            this.disposeStream = disposeStream;
        }

        /// <summary>
        /// Initialize this AlacContext with a Stream
        /// </summary>
        /// <param name="baseStream">Stream to read from</param>
        public AlacContext(Stream baseStream)
        {
            demuxRes = new DemuxResT();
            inputStream = new BinaryReader(baseStream);
            myStream = new MyStream(inputStream);
            QTMovieT qtmovie = new QTMovieT(myStream, demuxRes);

            /* if qtmovie_read returns successfully, the stream is up to
             * the movie data, which can be used directly by the decoder */
            int headerRead = qtmovie.Read();

            if (headerRead == 0 || headerRead == 3)
            {
                Dispose(true);
                throw new System.IO.IOException("Error while loading the QuickTime movie headers.");
            }

            /* initialise the sound converter */
            alac = new AlacFile(demuxRes.sample_size, demuxRes.num_channels);
            alac.SetInfo(demuxRes.codecdata);
        }

        /// <summary>
        /// Returns the sample rate of the specified ALAC file
        /// </summary>
        /// <returns></returns>
        public int GetSampleRate()
        {
            if (demuxRes.sample_rate != 0)
            {
                return demuxRes.sample_rate;
            }
            else
            {
                return (44100);
            }
        }

        /// <summary>
        /// Get the number of channels for this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetNumChannels()
        {
            if (demuxRes.num_channels != 0)
            {
                return demuxRes.num_channels;
            }
            else
            {
                return 2;
            }
        }

        /// <summary>
        /// Returns the number of bits per sample of this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetBitsPerSample()
        {
            if (demuxRes.sample_size != 0)
            {
                return demuxRes.sample_size;
            }
            else
            {
                return 16;
            }
        }


        /// <summary>
        /// Returns the number of bytes per sample of this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetBytesPerSample()
        {
            if (demuxRes.sample_size != 0)
            {
                return (int)Math.Ceiling((double)(demuxRes.sample_size / 8));
            }
            else
            {
                return 2;
            }
        }

        /// <summary>
        /// Get total number of samples contained in the Apple Lossless file, or -1 if unknown
        /// </summary>
        /// <returns></returns>
        public int GetNumSamples()
        {
            /* calculate output size */
            int num_samples = 0;
            int thissample_duration;
            int thissample_bytesize = 0;
            SampleDuration sampleinfo = new SampleDuration();
            int i;
            int retval = 0;

            for (i = 0; i < demuxRes.sample_byte_size.Length; i++)
            {
                thissample_duration = 0;
                thissample_bytesize = 0;

                retval = GetSampleInfo(i, sampleinfo);

                if (retval == 0)
                {
                    return (-1);
                }
                thissample_duration = sampleinfo.sample_duration;
                thissample_bytesize = sampleinfo.sample_byte_size;

                num_samples += thissample_duration;
            }

            return (num_samples);
        }

        int GetSampleInfo(int samplenum, SampleDuration sampleinfo)
        {
            int duration_index_accum = 0;
            int duration_cur_index = 0;

            if (samplenum >= demuxRes.sample_byte_size.Length)
            {
                Debug.WriteLine("sample " + samplenum + " does not exist; last="+ demuxRes.sample_byte_size.Length);
                return 0;
            }

            if (demuxRes.num_time_to_samples == 0)     // was null
            {
                Debug.WriteLine("no time to samples");
                return 0;
            }
            while ((demuxRes.time_to_sample[duration_cur_index].sample_count + duration_index_accum) <= samplenum)
            {
                duration_index_accum += demuxRes.time_to_sample[duration_cur_index].sample_count;
                duration_cur_index++;
                if (duration_cur_index >= demuxRes.num_time_to_samples)
                {
                    Debug.WriteLine("sample " + samplenum + " does not have a duration");
                    return 0;
                }
            }

            sampleinfo.sample_duration = demuxRes.time_to_sample[duration_cur_index].sample_duration;
            sampleinfo.sample_byte_size = demuxRes.sample_byte_size[samplenum];

            return 1;
        }

        /// <summary>
        /// Reads and decodes a single ALAC frame and returns the decoded wave stream
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <returns>Number of bytes read</returns>
        public int Read(byte[] buffer)
        {
            int bytesRead = UnpackSamples(formatBuffer);
            if (bytesRead > 0)
            {
                outputBufer = formatSamples(GetBytesPerSample(), formatBuffer, bytesRead);
                Array.Copy(outputBufer, 0, buffer, 0, bytesRead);
            }
            return bytesRead;
        }

        /// <summary>
        /// Decode an ALAC frame
        /// </summary>
        /// <param name="pDestBuffer">Buffer to read into</param>
        /// <returns>Number of bytes decoded</returns>
        int UnpackSamples(int[] pDestBuffer)
        {
            int sample_byte_size;
            SampleDuration sampleinfo = new SampleDuration();
            int outputBytes;            

 
            // if current_sample_block is beyond last block then finished

            if (currentSampleBlock >= demuxRes.sample_byte_size.Length)
            {
                // Sample past this stream's length
                return 0;
            }

            if (GetSampleInfo(currentSampleBlock, sampleinfo) == 0)
            {
                // getting sample failed
                System.Diagnostics.Debug.WriteLine("getting sample failed");
                return 0;
            }

            sample_byte_size = sampleinfo.sample_byte_size;

            myStream.Read(sample_byte_size, readBuffer, 0);

            /* now fetch */
            outputBytes = destBufferSize;

            outputBytes = alac.DecodeFrame(readBuffer, pDestBuffer, outputBytes);

            currentSampleBlock = currentSampleBlock + 1;
            LastSampleNumber += sampleinfo.sample_duration;
            outputBytes -= offset * GetBytesPerSample();
            Array.Copy(pDestBuffer, offset, pDestBuffer, 0, outputBytes);
             offset = 0;
            return outputBytes;

        }

        /// <summary>
        /// Reformat samples from longs in processor's native endian mode to
        /// little-endian data with (possibly) less than 3 bytes / sample.
        /// </summary>
        /// <param name="bps"></param>
        /// <param name="src"></param>
        /// <param name="samcnt"></param>
        /// <returns></returns>
        static byte[] formatSamples(int bps, int[] src, int samcnt)
        {
            int temp = 0;
            int counter = 0;
            int counter2 = 0;
            byte[] dst = new byte[65536];

            switch (bps)
            {
                case 1:
                    while (samcnt > 0)
                    {
                        dst[counter] = (byte)(0x00FF & (src[counter] + 128));
                        counter++;
                        samcnt--;
                    }
                    break;

                case 2:
                    while (samcnt > 0)
                    {
                        temp = src[counter2];
                        dst[counter] = (byte)temp;
                        counter++;
                        dst[counter] = (byte)((uint)temp >> 8);
                        counter++;
                        counter2++;
                        samcnt = samcnt - 2;
                    }
                    break;

                case 3:
                    while (samcnt > 0)
                    {
                        dst[counter] = (byte)src[counter2];
                        counter++;
                        counter2++;
                        samcnt--;
                    }
                    break;
            }

            return dst;
        }



        ///<summary>
        /// Sets position in pcm samples
        ///<param name="position">position in pcm samples to go to</param> 
        ///</summary>
        public void SetPosition(long position)
        {
            int current_position = 0;
            int current_sample = 0;
            SampleDuration sample_info = new SampleDuration();
            ChunkInfo chunkInfo;
            for (int i = 0; i < demuxRes.stsc.Length; i++)
            {
                chunkInfo = demuxRes.stsc[i];
                int last_chunk;

                if (i < demuxRes.stsc.Length - 1)
                {
                    last_chunk = demuxRes.stsc[i + 1].first_chunk;
                }
                else
                {
                    last_chunk = demuxRes.stco.Length;
                }

                for (int chunk = chunkInfo.first_chunk; chunk <= last_chunk; chunk++)
                {
                    int pos = demuxRes.stco[chunk - 1];
                    int sample_count = chunkInfo.samples_per_chunk;
                    while (sample_count > 0)
                    {
                        int ret = GetSampleInfo(current_sample, sample_info);
                        if (ret == 0) break;
                        current_position += sample_info.sample_duration;
                        if (position < current_position)
                        {
                            inputStream.BaseStream.Seek(pos, SeekOrigin.Begin);
                            currentSampleBlock = current_sample;
                            LastSampleNumber = current_position;
                            offset =
                                    (int)(position - (current_position - sample_info.sample_duration))
                                            * GetNumChannels();
                            return;
                        }
                        pos += sample_info.sample_byte_size;
                        current_sample++;
                        sample_count--;
                    }
                }
            }
        }


        #region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing && disposeStream && inputStream != null)
                {
                    inputStream.Dispose();
                }
                disposedValue = true;
            }
        }


        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion

        class SampleDuration
        {
            public int sample_byte_size { get; set; }
            public int sample_duration { get; set; }
        }
    }
}
