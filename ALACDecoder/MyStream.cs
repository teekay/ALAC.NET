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

namespace ALACdotNET.Decoder
{
    internal sealed class MyStream
    {
        public MyStream(BinaryReader bReader)
        {
            _binaryReader = bReader;
        }

        private readonly BinaryReader _binaryReader;
        private readonly byte[] _readBuffer = new byte[8];

        public long Position { get; private set; }

        /// <summary>
        /// Indicates whether the stream has reached the end
        /// </summary>
        public bool EOF => _binaryReader.BaseStream.Position >= _binaryReader.BaseStream.Length;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="size"></param>
        /// <param name="buf"></param>
        /// <param name="startPos"></param>
        public void Read(int size, int[] buf, int startPos)
        {
            byte[] byteBuf = new byte[size];
            int bytes_read = Read(size, byteBuf, 0);
            for (int i = 0; i < bytes_read; i++)
            {
                buf[startPos + i] = byteBuf[i];
            }
        }

        public int Read(int size, byte[] buf, int startPos)
        {
            var bytes_read = _binaryReader.Read(buf, startPos, size);
            Position = Position + bytes_read;
            return bytes_read;
        }

        public int ReadUint32()
        {
            var bytes_read = _binaryReader.Read(_readBuffer, 0, 4);
            Position = Position + bytes_read;
            var interMediate = _readBuffer[0] & 0xff;
            var value = interMediate << 24;
            interMediate = _readBuffer[1] & 0xff;
            value = value | (interMediate << 16);
            interMediate = _readBuffer[2] & 0xff;
            value = value | (interMediate << 8);
            interMediate = _readBuffer[3] & 0xff;
            value = value | interMediate;
            return value;
        }

        public int ReadInt16()
        {
            int v = _binaryReader.ReadInt16();
            Position = Position + 2;
            return v;
        }

        public int ReadUint16()
        {
            var bytesRead = _binaryReader.Read(_readBuffer, 0, 2);
            Position = Position + bytesRead;
            var interMediate = _readBuffer[0] & 0xff;
            var value = interMediate << 8;
            interMediate = _readBuffer[1] & 0xff;
            value = value | interMediate;
            return value;
        }

        public int ReadUint8()
        {
            _binaryReader.Read(_readBuffer, 0, 1);
            var value = _readBuffer[0] & 0xff;
            Position = Position + 1;
            return value;
        }

        public void Skip(int skip)
        {
            if (skip < 0)
                throw new ArgumentException("Request to seek backwards in stream is not supported");
            var bytes_read = _binaryReader.BaseStream.Seek(skip, SeekOrigin.Current);
            Position = Position + bytes_read;
        }

        public long Seek(long pos)
        {
            try
            {
                _binaryReader.BaseStream.Seek(pos, SeekOrigin.Begin);
                return _binaryReader.BaseStream.Position;
            }
            catch
            {
                return -1;
            }
        }
    }
}