/**
** Copyright (c) 2015 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
using System;
using System.Diagnostics;
namespace ALACdotNET.Decoder
{
    internal class QtMovieT
    {
        private readonly MyStream _qtStream;
        private readonly DemuxResT _demuxRes;
        private long _savedMdatPos;
        public QtMovieT(MyStream stream, DemuxResT demux)
        {
            _qtStream = stream;
            _demuxRes = demux;
        }

        private static int MakeFourCc(int ch0, int ch1, int ch2, int ch3)
        {
            return (((ch0) << 24) | ((ch1) << 16) | ((ch2) << 8) | ((ch3)));
        }

        private static int MakeFourCc32(int ch0, int ch1, int ch2, int ch3)
        {
            int tmp = ch0;
            var retval = tmp << 24;
            tmp = ch1;
            retval = retval | (tmp << 16);
            tmp = ch2;
            retval = retval | (tmp << 8);
            tmp = ch3;
            retval = retval | tmp;
            return retval;
        }

        private static string SplitFourCc(int code)
        {
            var c1 = (char)((code >> 24) & 0xFF);
            var c2 = (char)((code >> 16) & 0xFF);
            var c3 = (char)((code >> 8) & 0xFF);
            var c4 = (char)(code & 0xFF);
            return c1 + " " + c2 + " " + c3 + " " + c4;
        }

        public MdatPosStatus ReadHeader()
        {
            int foundMoov = 0;
            int foundMdat = 0;
            /* read the chunks */
            while (true)
            {
                var chunkLen = _qtStream.ReadUint32();
                if (_qtStream.EOF) return 0;
                var chunkId = _qtStream.ReadUint32();
                if (chunkId == MakeFourCc32(102, 116, 121, 112))   // fourcc equals ftyp
                {
                    ReadChunkFtyp(chunkLen);
                }
                else if (chunkId == MakeFourCc32(109, 111, 111, 118))  // fourcc equals moov
                {
                    if (ReadChunkMoov(chunkLen) == 0)
                    {
                        return MdatPosStatus.None; // failed to read moov, can't do anything
                    }

                    if (foundMdat != 0)
                    {
                        return SetSavedMdat(); // suspicious - doesn't the caller expect "number of bytes read"?
                    }
                    foundMoov = 1;
                }
                /* if we hit mdat before we've found moov, record the position
				 * and move on. We can then come back to mdat later.
				 * This presumes the stream supports seeking backwards.
				 */
                else if (chunkId == MakeFourCc32(109, 100, 97, 116))   // fourcc equals mdat
                {
                    int notFoundMoov = 0;
                    if (foundMoov == 0)
                        notFoundMoov = 1;
                    ProcReadChunkMdat(chunkLen, notFoundMoov);
                    if (foundMoov != 0)
                    {
                        return MdatPosStatus.Ok;
                    }
                    foundMdat = 1;
                }
                /*  these following atoms can be skipped !!!! */
                else if (chunkId == MakeFourCc32(102, 114, 101, 101))  // fourcc equals free
                {
                    _qtStream.Skip(chunkLen - 8); // FIXME not 8
                }
                else if (chunkId == MakeFourCc32(106, 117, 110, 107))     // fourcc equals junk
                {
                    _qtStream.Skip(chunkLen - 8); // FIXME not 8
                }
                else
                {
                    Debug.WriteLine("(top) unknown chunk id: " + chunkId + ", " + SplitFourCc(chunkId));
                    return MdatPosStatus.None;
                }
            }
        }

        private void ReadChunkFtyp(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME: can't hardcode 8, size may be 64bit
            var type = _qtStream.ReadUint32();
            sizeRemaining -= 4;
            if (type != MakeFourCc32(77, 52, 65, 32))       // "M4A " ascii values
            {
                Debug.WriteLine("not M4A file");
                return;
            }
            // ReSharper disable once UnusedVariable
            int minorVer = _qtStream.ReadUint32();
            sizeRemaining -= 4;
            /* compatible brands */
            while (sizeRemaining != 0)
            {
                /* unused */
                /*fourcc_t cbrand =*/
                _qtStream.ReadUint32();
                sizeRemaining -= 4;
            }
        }

        /* 'trak' - a movie track - contains other atoms */
        private int ReadChunkTrak(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            while (sizeRemaining != 0)
            {
                int subChunkLen;
                try
                {
                    subChunkLen = (_qtStream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_trak) error reading sub_chunk_len - possibly number too large: {0}", e);
                    subChunkLen = 0;
                }
                if (subChunkLen <= 1 || subChunkLen > sizeRemaining)
                {
                    Debug.WriteLine("strange size for chunk inside trak");
                    return 0;
                }
                var subChunkId = _qtStream.ReadUint32();
                if (subChunkId == MakeFourCc32(116, 107, 104, 100))   // fourcc equals tkhd
                {
                    SkipOverChunkTkhd(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(109, 100, 105, 97))   // fourcc equals mdia
                {
                    if (ReadChunkMedia(subChunkLen) == 0)
                        return 0;
                }
                else if (subChunkId == MakeFourCc32(101, 100, 116, 115))  // fourcc equals edts
                {
                    SkipOverChunkEdts(subChunkLen);
                }
                else
                {
                    Debug.WriteLine("(trak) unknown chunk id: " + SplitFourCc(subChunkId));
                    return 0;
                }
                sizeRemaining -= subChunkLen;
            }
            return 1;
        }

        private int ReadChunkStbl(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            while (sizeRemaining != 0)
            {
                int subChunkLen;
                try
                {
                    subChunkLen = (_qtStream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_stbl) error reading sub_chunk_len - possibly number too large: {0}", e);
                    subChunkLen = 0;
                }
                if (subChunkLen <= 1 || subChunkLen > sizeRemaining)
                {
                    Debug.WriteLine("strange size for chunk inside stbl " + subChunkLen + " (remaining: " + sizeRemaining + ")");
                    return 0;
                }
                var subChunkId = _qtStream.ReadUint32();
                if (subChunkId == MakeFourCc32(115, 116, 115, 100))   // fourcc equals stsd
                {
                    if (ReadChunkStsd() == 0)
                        return 0;
                }
                else if (subChunkId == MakeFourCc32(115, 116, 116, 115))  // fourcc equals stts
                {
                    ProcReadOverChunkStts(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(115, 116, 115, 122))  // fourcc equals stsz
                {
                    SkipOverChunkStsz(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(115, 116, 115, 99))   // fourcc equals stsc
                {
                    ReadChunkStsc();
                }
                else if (subChunkId == MakeFourCc32(115, 116, 99, 111))   // fourcc equals stco
                {
                    ReadChunkStco();
                }
                else
                {
                    Debug.WriteLine("(stbl) unknown chunk id: " + SplitFourCc(subChunkId));
                    return 0;
                }
                sizeRemaining -= subChunkLen;
            }
            return 1;
        }
        
        /* chunk to offset box */
        private void ReadChunkStco()
        {
            //skip header and size
            _qtStream.Skip(4);
            int numEntries = _qtStream.ReadUint32();
            _demuxRes.Stco = new int[numEntries];
            for (int i = 0; i < numEntries; i++)
            {
                _demuxRes.Stco[i] = _qtStream.ReadUint32();
            }
        }

        /* sample to chunk box */
        private void ReadChunkStsc()
        {
            //skip header and size
            //skip version and other junk
            _qtStream.Skip(4);
            int numEntries = _qtStream.ReadUint32();
            _demuxRes.Stsc = new ChunkInfo[numEntries];
            for (int i = 0; i < numEntries; i++)
            {
                _demuxRes.Stsc[i] = new ChunkInfo(_qtStream.ReadUint32(), _qtStream.ReadUint32(), _qtStream.ReadUint32());
            }
        }

        private int ReadChunkMediaInfo(int chunkLen)
        {
            int dinfSize;
            int stblSize;
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            int mediaInfoSize;
            /**** SOUND HEADER CHUNK ****/
            try
            {
                mediaInfoSize = (_qtStream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading media_info_size - possibly number too large: {0}", e);
                mediaInfoSize = 0;
            }
            if (mediaInfoSize != 16)
            {
                Debug.WriteLine("unexpected size in media info\n");
                return 0;
            }
            if (_qtStream.ReadUint32() != MakeFourCc32(115, 109, 104, 100))   // "smhd" ascii values
            {
                Debug.WriteLine("not a sound header! can't handle this.");
                return 0;
            }
            /* now skip the rest */
            _qtStream.Skip(16 - 8);
            sizeRemaining -= 16;
            /****/
            /**** DINF CHUNK ****/
            try
            {
                dinfSize = _qtStream.ReadUint32();
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading dinf_size - possibly number too large: {0}", e);
                dinfSize = 0;
            }
            if (_qtStream.ReadUint32() != MakeFourCc32(100, 105, 110, 102))   // "dinf" ascii values
            {
                Debug.WriteLine("expected dinf, didn't get it.");
                return 0;
            }
            /* skip it */
            _qtStream.Skip(dinfSize - 8);
            sizeRemaining -= dinfSize;
            /****/
            /**** SAMPLE TABLE ****/
            try
            {
                stblSize = _qtStream.ReadUint32();
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading stbl_size - possibly number too large: {0}", e);
                stblSize = 0;
            }
            if (_qtStream.ReadUint32() != MakeFourCc32(115, 116, 98, 108))    // "stbl" ascii values
            {
                Debug.WriteLine("expected stbl, didn't get it.");
                return 0;
            }
            if (ReadChunkStbl(stblSize) == 0)
                return 0;
            sizeRemaining -= stblSize;
            if (sizeRemaining != 0)
            {
                Debug.WriteLine("(read_chunk_minf) - size remaining?");
                _qtStream.Skip(sizeRemaining);
            }
            return 1;
        }

        private int ReadChunkMedia(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            while (sizeRemaining != 0)
            {
                int subChunkLen;
                try
                {
                    subChunkLen = (_qtStream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_mdia) error reading sub_chunk_len - possibly number too large: {0}", e);
                    subChunkLen = 0;
                }
                if (subChunkLen <= 1 || subChunkLen > sizeRemaining)
                {
                    Debug.WriteLine("strange size for chunk inside mdia\n");
                    return 0;
                }
                var subChunkId = _qtStream.ReadUint32();
                if (subChunkId == MakeFourCc32(109, 100, 104, 100))   // fourcc equals mdhd
                {
                    SkipOverChunkMdhd(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(104, 100, 108, 114))  // fourcc equals hdlr
                {
                    ProcReadChunkHdlr(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(109, 105, 110, 102))  // fourcc equals minf
                {
                    if (ReadChunkMediaInfo(subChunkLen) == 0)
                        return 0;
                }
                else
                {
                    Debug.WriteLine("(mdia) unknown chunk id: " + SplitFourCc(subChunkId));
                    return 0;
                }
                sizeRemaining -= subChunkLen;
            }
            return 1;
        }

        private void ProcReadChunkHdlr(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            /* version */
            _qtStream.ReadUint8();
            sizeRemaining -= 1;
            /* flags */
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            sizeRemaining -= 3;
            /* component type */
            // ReSharper disable once UnusedVariable
            var comptype = _qtStream.ReadUint32();
            // ReSharper disable once UnusedVariable
            var compsubtype = _qtStream.ReadUint32();
            sizeRemaining -= 8;
            /* component manufacturer */
            _qtStream.ReadUint32();
            sizeRemaining -= 4;
            /* flags */
            _qtStream.ReadUint32();
            _qtStream.ReadUint32();
            sizeRemaining -= 8;
            /* name */
            // ReSharper disable once UnusedVariable
            var strlen = _qtStream.ReadUint8();
            /* 
            ** rewrote this to handle case where we actually read more than required 
            ** so here we work out how much we need to read first
            */
            sizeRemaining -= 1;
            _qtStream.Skip(sizeRemaining);
        }

        private int ReadChunkStsd()
        {
            int numentries;
            /* version */
            _qtStream.ReadUint8();
            /* flags */
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            try
            {
                numentries = (_qtStream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stsd) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }

            if (numentries != 1)
            {
                Debug.WriteLine("only expecting one entry in sample description atom!");
                return 0;
            }
            for (int i = 0; i < numentries; i++)
            {
                /* parse the alac atom contained within the stsd atom */
                var entrySize = _qtStream.ReadUint32();
                _demuxRes.Format = _qtStream.ReadUint32();
                var entryRemaining = entrySize;
                entryRemaining -= 8;
                if (_demuxRes.Format != MakeFourCc32(97, 108, 97, 99))    // "alac" ascii values
                {
                    Debug.WriteLine("(read_chunk_stsd) error reading description atom - expecting alac, got " + SplitFourCc(_demuxRes.Format));
                    return 0;
                }
                /* sound info: */
                _qtStream.Skip(6); // reserved
                entryRemaining -= 6;
                var version = _qtStream.ReadUint16();
                if (version != 1)
                    Debug.WriteLine("unknown version??");
                entryRemaining -= 2;
                /* revision level */
                _qtStream.ReadUint16();
                /* vendor */
                _qtStream.ReadUint32();
                entryRemaining -= 6;
                /* EH?? spec doesn't say theres an extra 16 bits here.. but there is! */
                _qtStream.ReadUint16();
                entryRemaining -= 2;
                /* skip 4 - this is the top level num of channels and bits per sample */
                _qtStream.Skip(4);
                entryRemaining -= 4;
                /* compression id */
                _qtStream.ReadUint16();
                /* packet size */
                _qtStream.ReadUint16();
                entryRemaining -= 4;
                /* skip 4 - this is the top level sample rate */
                _qtStream.Skip(4);
                entryRemaining -= 4;
                /* remaining is codec data */
                /* 12 = audio format atom, 8 = padding */
                _demuxRes.CodecDataLength = entryRemaining + 12 + 8;
                if (_demuxRes.CodecDataLength > _demuxRes.CodecData.Length)
                {
                    Debug.WriteLine("(read_chunk_stsd) unexpected codec data length read from atom " + _demuxRes.CodecDataLength);
                    return 0;
                }
                for (int count = 0; count < _demuxRes.CodecDataLength; count++)
                {
                    _demuxRes.CodecData[count] = 0;
                }
                /* audio format atom */
                _demuxRes.CodecData[0] = 0x0c000000;
                _demuxRes.CodecData[1] = MakeFourCc(97, 109, 114, 102);       // "amrf" ascii values
                _demuxRes.CodecData[2] = MakeFourCc(99, 97, 108, 97);     // "cala" ascii values
                _qtStream.Read(entryRemaining, _demuxRes.CodecData, 12);  // codecdata buffer should be +12
                entryRemaining -= entryRemaining;
                /* We need to read the bits per sample, number of channels and sample rate from the codec data i.e. the alac atom within 
                ** the stsd atom the 'alac' atom contains a number of pieces of information which we can skip just now, its processed later 
                ** in the alac_set_info() method. This atom contains the following information
                ** 
                ** samples_per_frame
                ** compatible version
                ** bits per sample
                ** history multiplier
                ** initial history
                ** maximum K
                ** channels
                ** max run
                ** max coded frame size
                ** bitrate
                ** sample rate
                */
                int ptrIndex = 29;  // position of bits per sample
                _demuxRes.SampleSize = (_demuxRes.CodecData[ptrIndex] & 0xff);
                ptrIndex = 33;  // position of num of channels
                _demuxRes.NumChannels = (_demuxRes.CodecData[ptrIndex] & 0xff);
                ptrIndex = 44;      // position of sample rate within codec data buffer
                _demuxRes.SampleRate = (((_demuxRes.CodecData[ptrIndex] & 0xff) << 24) | ((_demuxRes.CodecData[ptrIndex + 1] & 0xff) << 16) | ((_demuxRes.CodecData[ptrIndex + 2] & 0xff) << 8) | (_demuxRes.CodecData[ptrIndex + 3] & 0xff));
                if (entryRemaining != 0)   // was comparing to null
                    _qtStream.Skip(entryRemaining);
                _demuxRes.FormatRead = 1;
                if (_demuxRes.Format != MakeFourCc32(97, 108, 97, 99))        // "alac" ascii values
                {
                    return 0;
                }
            }
            return 1;
        }

        private void ProcReadOverChunkStts(int chunkLen)
        {
            int i;
            int numentries;
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            /* version */
            _qtStream.ReadUint8();
            sizeRemaining -= 1;
            /* flags */
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            sizeRemaining -= 3;
            try
            {
                numentries = _qtStream.ReadUint32();
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stts) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }
            sizeRemaining -= 4;
            _demuxRes.NumTimeToSamples = numentries;
            for (i = 0; i < numentries; i++)
            {
                _demuxRes.TimeToSample[i] = new SampleInfo(sampleCount: _qtStream.ReadUint32(), sampleDuration: _qtStream.ReadUint32());
                sizeRemaining -= 8;
            }
            if (sizeRemaining != 0)
            {
                Debug.WriteLine("(read_chunk_stts) size remaining?");
                _qtStream.Skip(sizeRemaining);
            }
        }

        private void SkipOverChunkStsz(int chunkLen)
        {
            int i;
            int numentries;
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            /* version */
            _qtStream.ReadUint8();
            sizeRemaining -= 1;
            /* flags */
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            _qtStream.ReadUint8();
            sizeRemaining -= 3;
            /* default sample size */
            var uniformSize = _qtStream.ReadUint32();
            if (uniformSize != 0)
            {
                /*
                ** Normally files have intiable sample sizes, this handles the case where
                ** they are all the same size
                */
                var uniformNum = (_qtStream.ReadUint32());
                _demuxRes.SampleByteSize = new int[uniformNum];
                for (i = 0; i < uniformNum; i++)
                {
                    _demuxRes.SampleByteSize[i] = uniformSize;
                }
                // sizeRemaining -= 4;
                return;
            }
            sizeRemaining -= 4;
            try
            {
                numentries = (_qtStream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stsz) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }
            sizeRemaining -= 4;
            _demuxRes.SampleByteSize = new int[numentries];
            for (i = 0; i < numentries; i++)
            {
                _demuxRes.SampleByteSize[i] = (_qtStream.ReadUint32());
                sizeRemaining -= 4;
            }
            if (sizeRemaining != 0)
            {
                Debug.WriteLine("(read_chunk_stsz) size remaining?");
                _qtStream.Skip(sizeRemaining);
            }
        }

        private void SkipOverChunkTkhd(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        private void SkipOverChunkMdhd(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        private void SkipOverChunkEdts(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        /* 'mvhd' movie header atom */
        private void SkipOverChunkMvhd(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }
        
        /* 'udta' user data.. contains tag info */
        private void SkipOverChunkUdta(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        /* 'iods' */
        private void SkipOverChunkIods(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        private void SkipOverChunkElst(int chunkLen)
        {
            /* don't need anything from here atm, skip */
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            _qtStream.Skip(sizeRemaining);
        }

        /* 'moov' movie atom - contains other atoms */
        private int ReadChunkMoov(int chunkLen)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            while (sizeRemaining != 0)
            {
                int subChunkLen;
                try
                {
                    subChunkLen = (_qtStream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_moov) error reading sub_chunk_len - possibly number too large: {0}", e);
                    subChunkLen = 0;
                }
                if (subChunkLen <= 1 || subChunkLen > sizeRemaining)
                {
                    Debug.WriteLine("strange size for chunk inside moov");
                    return 0;
                }
                var subChunkId = _qtStream.ReadUint32();
                if (subChunkId == MakeFourCc32(109, 118, 104, 100))   // fourcc equals mvhd
                {
                    SkipOverChunkMvhd(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(116, 114, 97, 107))   // fourcc equals trak
                {
                    if (ReadChunkTrak(subChunkLen) == 0)
                        return 0;
                }
                else if (subChunkId == MakeFourCc32(117, 100, 116, 97))   // fourcc equals udta
                {
                    SkipOverChunkUdta(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(101, 108, 115, 116))  // fourcc equals elst
                {
                    SkipOverChunkElst(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(105, 111, 100, 115))  // fourcc equals iods
                {
                    SkipOverChunkIods(subChunkLen);
                }
                else if (subChunkId == MakeFourCc32(102, 114, 101, 101))     // fourcc equals free
                {
                    _qtStream.Skip(subChunkLen - 8); // FIXME not 8
                }
                else
                {
                    Debug.WriteLine("(moov) unknown chunk id: " + SplitFourCc(subChunkId));
                    return 0;
                }
                sizeRemaining -= subChunkLen;
            }
            return 1;
        }

        private void ProcReadChunkMdat(int chunkLen, int skipMdat)
        {
            int sizeRemaining = chunkLen - 8; // FIXME WRONG
            if (sizeRemaining == 0) return;
            _demuxRes.MdatLen = sizeRemaining;
            if (skipMdat != 0)
            {
                _savedMdatPos = _qtStream.Position;
                _qtStream.Skip(sizeRemaining);
            }
        }

        private MdatPosStatus SetSavedMdat()
        {
            // returns as follows
            // 1 - all ok
            // 2 - do not have valid saved mdat pos
            // 3 - have valid saved mdat pos, but cannot seek there - need to close/reopen stream
            if (_savedMdatPos == -1)
            {
                return MdatPosStatus.NoValidSaveMdatPosition;
            }
            if (_qtStream.Seek(_savedMdatPos) != 0)
            {
                return MdatPosStatus.CannotSeekToMdatPosition;
            }
            return MdatPosStatus.Ok;
        }

    }

    internal enum MdatPosStatus
    {
        None = 0,
        Ok = 1,
        NoValidSaveMdatPosition = 2,
        CannotSeekToMdatPosition = 3
    }
}
