/**
** Copyright (c) 2015 Tomas Kohl
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
using System;
using System.IO;
using NAudio.Wave;

namespace ALACdotNET.Decoder
{
    /// <summary>
    /// NAudio adapter for the ALACSharp library
    /// Open an ALAC file, instantiate this class and pass the ALAC stream to it
    /// In order to be able to use this library across all .NET platforms including Metro and Windows Phone,
    /// this class does not provide a constructor taking a filename as an argument.
    /// </summary>
    public class ALACFileReader : WaveStream, IDisposable
    {
        readonly WaveFormat waveFormat;
        long length;
        AlacContext ac;
        int decompressLeftovers;
        int decompressBufferOffset;
        readonly byte[] decompressBuffer;
        byte[] pcmBuffer = new byte[65536];
        const int destBufferSize = 1024 * 24 * 3; // 24kb buffer = 4096 frames = 1 alac sample (we support max 24bps)
        int[] pDestBuffer = new int[destBufferSize];
        object repositionLock = new object();

        /// <summary>
        /// Create a new ALACFileReader
        /// The underlying ALAC stream will NOT be disposed after use
        /// </summary>
        /// <param name="baseStream">Stream that is ready for reading and supports seeking</param>
        public ALACFileReader(Stream baseStream) : this(baseStream, false)
        {
        }

        /// <summary>
        /// Create a new ALACFileReader
        /// You can specify to dispose the underlying stream 
        /// </summary>
        /// <param name="baseStream">Stream that is ready for reading and supports seeking</param>
        /// <param name="disposeAfterUse">Whether to dispose of the underlying stream on disposal</param>
        public ALACFileReader(Stream baseStream, bool disposeAfterUse)
        {
            ac = new AlacContext(baseStream, disposeAfterUse);
            waveFormat = new WaveFormat(ac.GetSampleRate(), ac.GetBytesPerSample() * 8, ac.GetNumChannels());
            length = ac.GetNumSamples() * waveFormat.BlockAlign;
            decompressBuffer = new byte[65546 * waveFormat.BitsPerSample / 8 * waveFormat.Channels];
        }

        /// <summary>
        /// Get the length of the uncompressed wave stream in bytes
        /// </summary>
        public override long Length
        {
            get { return length; }
        }

        /// <summary>
        /// Get / set the position within the wave stream
        /// </summary>
        public override long Position
        {
            get
            {
                var pos = ac.LastSampleNumber * waveFormat.BlockAlign;
                return pos;
            }

            set
            {
                lock(repositionLock)
                {
                    ac.SetPosition(value / waveFormat.BlockAlign);
                    decompressLeftovers = 0; // ... so that after repositioning, we don't return any more data from the buffer
                }
            }
        }

        /// <summary>
        /// Get the WaveFormat for this stream
        /// </summary>
        public override WaveFormat WaveFormat
        {
            get
            {
                return waveFormat;
            }
        }

        /// <summary>
        /// Read from this stream
        /// </summary>
        /// <param name="buffer">Buffer to read into</param>
        /// <param name="offset">Offset within the buffer</param>
        /// <param name="count">Number of bytes to return</param>
        /// <returns>Number of bytes read</returns>
        public override int Read(byte[] buffer, int offset, int count)
        {
            int bytesRead = 0;
            lock (repositionLock)
            {
                while (bytesRead < count)
                {
                    if (decompressLeftovers > 0)
                    {
                        var toCopy = Math.Min(decompressLeftovers, count - bytesRead);
                        Array.Copy(decompressBuffer, decompressBufferOffset, buffer, offset, toCopy);
                        decompressLeftovers -= toCopy;
                        if (decompressLeftovers == 0)
                        {
                            decompressBufferOffset = 0;
                        }
                        else
                        {
                            decompressBufferOffset += toCopy;
                        }
                        bytesRead += toCopy;
                        offset += toCopy;
                    }
                    if (bytesRead == count) break;
                    decompressBufferOffset = 0;

                    int bytes_unpacked = ac.Read(decompressBuffer);
                    if (bytes_unpacked == 0)
                        break;
                    decompressLeftovers += bytes_unpacked;
                }
            }
            return bytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && ac != null)
            {
                lock(repositionLock)
                {
                    ac.Dispose();
                }
                ac = null;
            }
        }
    }
}
