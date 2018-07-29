/**
** Copyright (c) 2015 - 2018 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
using System;
using System.IO;
using System.Diagnostics;
using System.Linq;

namespace ALACdotNET.Decoder
{
    /// <summary>
    /// AlacContext takes responsibility for the decoding of an ALAC stream
    /// and provides an interface to read the converted bytes
    /// </summary>
    public class AlacContext : IDisposable
    {
        /// <summary>
        /// Initialize this AlacContext with a Stream
        /// </summary>
        /// <param name="baseStream">Stream to read from</param>
        /// <param name="disposeStream">Whether to dispose the stream when disposing this object</param>
        public AlacContext(Stream baseStream, bool disposeStream) : this(baseStream)
        {
            _disposeStream = disposeStream;
        }

        /// <summary>
        /// Initialize this AlacContext with a Stream
        /// </summary>
        /// <param name="baseStream">Stream to read from</param>
        public AlacContext(Stream baseStream)
        {
            _demuxRes = new DemuxResT();
            _inputStream = new BinaryReader(baseStream);
            _myStream = new MyStream(_inputStream);
            var qtmovie = new QtMovieT(_myStream, _demuxRes);

            /* if qtmovie_read returns successfully, the stream is up to
             * the movie data, which can be used directly by the decoder */
            // I don't like throwing exceptions in constructors though
            var headerRead = qtmovie.ReadHeader();
            if (headerRead == MdatPosStatus.None || headerRead == MdatPosStatus.CannotSeekToMdatPosition)
            {
                SelfDispose(true);
                throw new IOException("Error while loading the QuickTime movie headers.");
            }

            /* initialise the sound converter */
            _alac = new AlacFile(_demuxRes.SampleSize, _demuxRes.NumChannels);
            _alac.SetInfo(_demuxRes.CodecData);
        }

        private readonly DemuxResT _demuxRes;
        private readonly AlacFile _alac;
        private readonly BinaryReader _inputStream;
        private readonly MyStream _myStream;
        private int _currentSampleBlock;
        private int _offset;
        private readonly byte[] _readBuffer = new byte[1024 * 80]; // sample big enough to hold any input for a single alac frame
        private readonly int[] _formatBuffer = new int[1024 * 80];
        private byte[] _outputBufer = new byte[1024 * 80];
        private readonly bool _disposeStream;
        // private readonly int _destBufferSize = 1024 * 24 * 3; // 24kb buffer = 4096 frames = 1 alac sample (we support max 24bps)
        private bool _disposedValue; // To detect redundant calls


        #region Properties
        /// <summary>
        /// Points to the last sample read - can be used to determine position
        /// </summary>
        public int LastSampleNumber { get; private set; }
        #endregion

        /// <summary>
        /// Returns the sample rate of the specified ALAC file
        /// </summary>
        /// <returns></returns>
        public int GetSampleRate() => _demuxRes.SampleRate != 0 ? _demuxRes.SampleRate : 44100;

        /// <summary>
        /// Get the number of channels for this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetNumChannels() => _demuxRes.NumChannels != 0 ? _demuxRes.NumChannels : 2;

        /// <summary>
        /// Returns the number of bits per sample of this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetBitsPerSample() => _demuxRes.SampleSize != 0 ? _demuxRes.SampleSize :16;

        /// <summary>
        /// Returns the number of bytes per sample of this ALAC stream
        /// </summary>
        /// <returns></returns>
        public int GetBytesPerSample() => _demuxRes.SampleSize != 0 ? (int)Math.Ceiling((double)_demuxRes.SampleSize / 8) : 2;


        /// <summary>
        /// Get total number of samples contained in the Apple Lossless file, or -1 if unknown
        /// </summary>
        /// <returns></returns>
        public int GetNumSamples()
        {
            /* calculate output size */
            try
            {
                return Enumerable.Range(0, _demuxRes.SampleByteSize.Length)
                    .ToList()
                    .Select(SamplesFromSampleInfo)
                    .Sum();
            }
            catch (SampleReadException)
            {
                return -1;
            }
        }

        private int SamplesFromSampleInfo(int sampleNumber)
        {
            var sampleinfo = TryGetSampleInfo(sampleNumber);
            return sampleinfo?.SampleDuration ?? throw new SampleReadException("Could not read some sample");
        }

        private SampleDurationInfo TryGetSampleInfo(int samplenum)
        {
            int durationIndexAccum = 0;
            int durationCurIndex = 0;
            if (samplenum >= _demuxRes.SampleByteSize.Length)
            {
                Debug.WriteLine("sample " + samplenum + " does not exist; last=" + _demuxRes.SampleByteSize.Length);
                return null;
            }
            if (_demuxRes.NumTimeToSamples == 0)     // was null
            {
                Debug.WriteLine("no time to samples");
                return null;
            }
            while ((_demuxRes.TimeToSample[durationCurIndex].SampleCount + durationIndexAccum) <= samplenum)
            {
                durationIndexAccum += _demuxRes.TimeToSample[durationCurIndex].SampleCount;
                durationCurIndex++;
                if (durationCurIndex >= _demuxRes.NumTimeToSamples)
                {
                    Debug.WriteLine("sample " + samplenum + " does not have a duration");
                    return null;
                }
            }
            SampleDurationInfo sampleinfo = new SampleDurationInfo(_demuxRes.SampleByteSize[samplenum], _demuxRes.TimeToSample[durationCurIndex].SampleDuration);
            return sampleinfo;
        }

        /// <summary>
        /// Reads and decodes a single ALAC frame and returns the decoded wave stream
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <returns>Number of bytes read</returns>
        public int Read(byte[] buffer)
        {
            int bytesRead = UnpackSamples(_formatBuffer);
            if (bytesRead > 0)
            {
                _outputBufer = FormatSamples(GetBytesPerSample(), _formatBuffer, bytesRead);
                Array.Copy(_outputBufer, 0, buffer, 0, bytesRead);
            }
            return bytesRead;
        }

        /// <summary>
        /// Decode an ALAC frame
        /// </summary>
        /// <param name="pDestBuffer">Buffer to read into</param>
        /// <returns>Number of bytes decoded</returns>
        private int UnpackSamples(int[] pDestBuffer)
        {
            // if current_sample_block is beyond last block then finished
            if (_currentSampleBlock >= _demuxRes.SampleByteSize.Length)
            {
                // Sample past this stream's length
                return 0;
            }
            var sampleinfo = TryGetSampleInfo(_currentSampleBlock);
            if (sampleinfo == null)
            {
                // getting sample failed
                Debug.WriteLine("getting sample failed");
                return 0;
            }
            var sampleByteSize = sampleinfo.SampleByteSize;
            _myStream.Read(sampleByteSize, _readBuffer, 0);
            /* now fetch */
            var outputBytes = _alac.DecodeFrame(_readBuffer, pDestBuffer);
            _currentSampleBlock = _currentSampleBlock + 1;
            LastSampleNumber += sampleinfo.SampleDuration;
            outputBytes -= _offset * GetBytesPerSample();
            Array.Copy(pDestBuffer, _offset, pDestBuffer, 0, outputBytes);
            _offset = 0;
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
        private static byte[] FormatSamples(int bps, int[] src, int samcnt)
        {
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
                        var temp = src[counter2];
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
            int currentPosition = 0;
            int currentSample = 0;
            for (int i = 0; i < _demuxRes.Stsc.Length; i++)
            {
                var chunkInfo = _demuxRes.Stsc[i];
                var lastChunk = i < _demuxRes.Stsc.Length - 1 ? _demuxRes.Stsc[i + 1].FirstChunk : _demuxRes.Stco.Length;
                for (int chunk = chunkInfo.FirstChunk; chunk <= lastChunk; chunk++)
                {
                    int pos = _demuxRes.Stco[chunk - 1];
                    int sampleCount = chunkInfo.SamplesPerChunk;
                    while (sampleCount > 0)
                    {
                        var sampleInfo = TryGetSampleInfo(currentSample);
                        if (sampleInfo == null) break;
                        currentPosition += sampleInfo.SampleDuration;
                        if (position < currentPosition)
                        {
                            _inputStream.BaseStream.Seek(pos, SeekOrigin.Begin);
                            _currentSampleBlock = currentSample;
                            LastSampleNumber = currentPosition;
                            _offset =
                                    (int)(position - (currentPosition - sampleInfo.SampleDuration))
                                            * GetNumChannels();
                            return;
                        }
                        pos += sampleInfo.SampleByteSize;
                        currentSample++;
                        sampleCount--;
                    }
                }
            }
        }

        protected virtual void Dispose(bool disposing)
        {
            SelfDispose(disposing);
        }

        private void SelfDispose(bool disposing)
        {
            if (_disposedValue) return;
            if (disposing && _disposeStream)
            {
                _inputStream?.Dispose();
            }
            _disposedValue = true;
        }


        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }

        private class SampleDurationInfo
        {
            public SampleDurationInfo(int sampleByteSize, int sampleDuration)
            {
                SampleByteSize = sampleByteSize;
                SampleDuration = sampleDuration;
            }

            public int SampleByteSize { get; }
            public int SampleDuration { get; }
        }

        private class SampleReadException : Exception
        {
            public SampleReadException(string message) : base(message)
            {
            }
        }
    }
}