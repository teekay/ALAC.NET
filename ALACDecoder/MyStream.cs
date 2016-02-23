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

namespace ALACdotNET.Decoder
{
    class MyStream
    {
        public MyStream(BinaryReader bReader)
        {
            binaryReader = bReader;
        }

        BinaryReader binaryReader;
        public long Position { get; private set; }

        /// <summary>
        /// Indicates whether the stream has reached the end
        /// </summary>
        public bool EOF
        {
            // TODO
            get { return binaryReader.BaseStream.Position == binaryReader.BaseStream.Length; }
        }

        public byte[] read_buf { get; set; } = new byte[8];

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
            int bytes_read = 0;
            bytes_read = binaryReader.Read(buf, startPos, size);
            Position = Position + bytes_read;
            return bytes_read;
        }

        public int ReadUint32()
        {
            int v = 0;
            int tmp = 0;
            int bytes_read = 0;

            bytes_read = binaryReader.Read(read_buf, 0, 4);
            Position = Position + bytes_read;
            tmp = (read_buf[0] & 0xff);

            v = tmp << 24;
            tmp = (read_buf[1] & 0xff);

            v = v | (tmp << 16);
            tmp = (read_buf[2] & 0xff);

            v = v | (tmp << 8);

            tmp = (read_buf[3] & 0xff);
            v = v | tmp;

            return v;
        }

        public int ReadInt16()
        {
            int v = 0;
            v = binaryReader.ReadInt16();
            Position = Position + 2;
            return v;
        }

        public int ReadUint16()
        {
            int v = 0;
            int tmp = 0;
            int bytes_read = 0;

            bytes_read = binaryReader.Read(read_buf, 0, 2);
            Position = Position + bytes_read;
            tmp = (read_buf[0] & 0xff);
            v = tmp << 8;
            tmp = (read_buf[1] & 0xff);

            v = v | tmp;

            return v;
        }

        public int ReadUint8()
        {
            int v = 0;
            int bytes_read = binaryReader.Read(read_buf, 0, 1);
            v = (read_buf[0] & 0xff);
            Position = Position + 1;

            return v;
        }

        public void Skip(int skip)
        {
            if (skip < 0)
                throw new ArgumentException("Request to seek backwards in stream is not supported");
            var bytes_read = binaryReader.BaseStream.Seek(skip, SeekOrigin.Current);
            Position = Position + bytes_read;
        }

        public long Seek(long pos)
        {
            try
            {
                var bytesRead = binaryReader.BaseStream.Seek(pos, SeekOrigin.Begin);
                return binaryReader.BaseStream.Position;
            }
            catch
            {
                return -1;
            }
        }

    }
}
