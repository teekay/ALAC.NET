/**
** Copyright (c) 2015 Tomas Kohl
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/

using System;
using System.IO;
using ALACdotNET.Decoder;
using NAudio.Wave;

namespace AlacNetNAudioAdapter
{
    /// <summary>
    /// NAudio adapter for the ALACSharp library
    /// Open an ALAC file, instantiate this class and pass the ALAC stream to it
    /// In order to be able to use this library across all .NET platforms including Metro and Windows Phone,
    /// this class does not provide a constructor taking a filename as an argument.
    /// </summary>
    public class ALACFileReader : WaveStream
    {
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
            _alacContext = new AlacContext(baseStream, disposeAfterUse);
            _waveFormat = new WaveFormat(_alacContext.GetSampleRate(), _alacContext.GetBytesPerSample() * 8, _alacContext.GetNumChannels());
            Length = _alacContext.GetNumSamples() * _waveFormat.BlockAlign;
            _decompressBuffer = new byte[65546 * _waveFormat.BitsPerSample / 8 * _waveFormat.Channels];
        }

        private readonly WaveFormat _waveFormat;
        private readonly AlacContext _alacContext;
        private int _decompressLeftovers;
        private int _decompressBufferOffset;
        private readonly byte[] _decompressBuffer;
        // private const int DestBufferSize = 1024 * 24 * 3; // 24kb buffer = 4096 frames = 1 alac sample (we support max 24bps)
        private readonly object _repositionLock = new object();

        /// <summary>
        /// Get the length of the uncompressed wave stream in bytes
        /// </summary>
        public override long Length { get; }

        /// <summary>
        /// Get / set the position within the wave stream
        /// </summary>
        public override long Position
        {
            get => _alacContext.LastSampleNumber * _waveFormat.BlockAlign;
            set
            {
                lock(_repositionLock)
                {
                    _alacContext.SetPosition(value / _waveFormat.BlockAlign);
                    _decompressLeftovers = 0; // ... so that after repositioning, we don't return any more data from the buffer
                }
            }
        }

        /// <summary>
        /// Get the WaveFormat for this stream
        /// </summary>
        public override WaveFormat WaveFormat => _waveFormat;

        /// <inheritdoc />
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
            lock (_repositionLock)
            {
                while (bytesRead < count)
                {
                    if (_decompressLeftovers > 0)
                    {
                        var toCopy = Math.Min(_decompressLeftovers, count - bytesRead);
                        Array.Copy(_decompressBuffer, _decompressBufferOffset, buffer, offset, toCopy);
                        _decompressLeftovers -= toCopy;
                        _decompressBufferOffset = _decompressLeftovers == 0
                            ? 0
                            : _decompressBufferOffset + toCopy;
                        bytesRead += toCopy;
                        offset += toCopy;
                    }
                    if (bytesRead >= count) break;
                    _decompressBufferOffset = 0;

                    int bytesUnpacked = _alacContext.Read(_decompressBuffer);
                    if (bytesUnpacked == 0) break;
                    _decompressLeftovers += bytesUnpacked;
                }
            }
            return bytesRead;
        }

        protected override void Dispose(bool disposing)
        {
            if (!disposing) return;
            lock(_repositionLock)
            {
                _alacContext.Dispose();
            }
        }
    }
}
