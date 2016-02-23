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
    class QTMovieT
    {
        MyStream qtstream;
        DemuxResT res;
        long saved_mdat_pos;

        public QTMovieT(MyStream stream, DemuxResT demux)
        {
            qtstream = stream;
            res = demux;
        }

        static int MakeFourCC(int ch0, int ch1, int ch2, int ch3)
        {
            return (((ch0) << 24) | ((ch1) << 16) | ((ch2) << 8) | ((ch3)));
        }

        static int MakeFourCC32(int ch0, int ch1, int ch2, int ch3)
        {
            int retval = 0;
            int tmp = ch0;

            retval = tmp << 24;

            tmp = ch1;

            retval = retval | (tmp << 16);
            tmp = ch2;

            retval = retval | (tmp << 8);
            tmp = ch3;

            retval = retval | tmp;

            return (retval);
        }

        static string SplitFourCC(int code)
        {
            string retstr;
            char c1;
            char c2;
            char c3;
            char c4;

            c1 = (char)((code >> 24) & 0xFF);
            c2 = (char)((code >> 16) & 0xFF);
            c3 = (char)((code >> 8) & 0xFF);
            c4 = (char)(code & 0xFF);
            retstr = c1 + " " + c2 + " " + c3 + " " + c4;

            return retstr;

        }

        public int Read()
        {
            int found_moov = 0;
            int found_mdat = 0;

            /* read the chunks */
            while (true)
            {
                int chunk_len;
                int chunk_id = 0;

                chunk_len = qtstream.ReadUint32();

                if (qtstream.EOF)
                {
                    return 0;
                }

                chunk_id = qtstream.ReadUint32();

                if (chunk_id == MakeFourCC32(102, 116, 121, 112))   // fourcc equals ftyp
                {
                    read_chunk_ftyp(chunk_len);
                }
                else if (chunk_id == MakeFourCC32(109, 111, 111, 118))  // fourcc equals moov
                {
                    if (read_chunk_moov(chunk_len) == 0)
                        return 0; // failed to read moov, can't do anything
                    if (found_mdat != 0)
                    {
                        return set_saved_mdat();
                    }
                    found_moov = 1;
                }
                /* if we hit mdat before we've found moov, record the position
				 * and move on. We can then come back to mdat later.
				 * This presumes the stream supports seeking backwards.
				 */
                else if (chunk_id == MakeFourCC32(109, 100, 97, 116))   // fourcc equals mdat
                {
                    int not_found_moov = 0;
                    if (found_moov == 0)
                        not_found_moov = 1;
                    read_chunk_mdat(chunk_len, not_found_moov);
                    if (found_moov != 0)
                    {
                        return 1;
                    }
                    found_mdat = 1;
                }
                /*  these following atoms can be skipped !!!! */
                else if (chunk_id == MakeFourCC32(102, 114, 101, 101))  // fourcc equals free
                {
                    qtstream.Skip(chunk_len - 8); // FIXME not 8
                }
                else if (chunk_id == MakeFourCC32(106, 117, 110, 107))     // fourcc equals junk
                {
                    qtstream.Skip(chunk_len - 8); // FIXME not 8
                }
                else
                {
                    Debug.WriteLine("(top) unknown chunk id: " + chunk_id + ", " + SplitFourCC(chunk_id));
                    return 0;
                }
            }
        }

        void read_chunk_ftyp(int chunk_len)
        {
            int type = 0;
            int minor_ver = 0;
            int size_remaining = chunk_len - 8; // FIXME: can't hardcode 8, size may be 64bit

            type = qtstream.ReadUint32();
            size_remaining -= 4;

            if (type != MakeFourCC32(77, 52, 65, 32))       // "M4A " ascii values
            {
                Debug.WriteLine("not M4A file");
                return;
            }
            minor_ver = qtstream.ReadUint32();
            size_remaining -= 4;

            /* compatible brands */
            while (size_remaining != 0)
            {
                /* unused */
                /*fourcc_t cbrand =*/
                qtstream.ReadUint32();
                size_remaining -= 4;
            }
        }

        /* 'trak' - a movie track - contains other atoms */
        int read_chunk_trak(int chunk_len)
        {
            int size_remaining = chunk_len - 8; // FIXME WRONG

            while (size_remaining != 0)
            {
                int sub_chunk_len;
                int sub_chunk_id = 0;

                try
                {
                    sub_chunk_len = (qtstream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_trak) error reading sub_chunk_len - possibly number too large: {0}", e);
                    sub_chunk_len = 0;
                }

                if (sub_chunk_len <= 1 || sub_chunk_len > size_remaining)
                {
                    Debug.WriteLine("strange size for chunk inside trak");
                    return 0;
                }

                sub_chunk_id = qtstream.ReadUint32();

                if (sub_chunk_id == MakeFourCC32(116, 107, 104, 100))   // fourcc equals tkhd
                {
                    read_chunk_tkhd(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(109, 100, 105, 97))   // fourcc equals mdia
                {
                    if (read_chunk_mdia(sub_chunk_len) == 0)
                        return 0;
                }
                else if (sub_chunk_id == MakeFourCC32(101, 100, 116, 115))  // fourcc equals edts
                {
                    read_chunk_edts(sub_chunk_len);
                }
                else
                {
                    Debug.WriteLine("(trak) unknown chunk id: " + SplitFourCC(sub_chunk_id));
                    return 0;
                }

                size_remaining -= sub_chunk_len;
            }

            return 1;
        }


        int read_chunk_stbl(int chunk_len)
        {
            int size_remaining = chunk_len - 8; // FIXME WRONG

            while (size_remaining != 0)
            {
                int sub_chunk_len;
                int sub_chunk_id = 0;

                try
                {
                    sub_chunk_len = (qtstream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_stbl) error reading sub_chunk_len - possibly number too large: {0}", e);
                    sub_chunk_len = 0;
                }

                if (sub_chunk_len <= 1 || sub_chunk_len > size_remaining)
                {
                    Debug.WriteLine("strange size for chunk inside stbl " + sub_chunk_len + " (remaining: " + size_remaining + ")");
                    return 0;
                }

                sub_chunk_id = qtstream.ReadUint32();

                if (sub_chunk_id == MakeFourCC32(115, 116, 115, 100))   // fourcc equals stsd
                {
                    if (read_chunk_stsd(sub_chunk_len) == 0)
                        return 0;
                }
                else if (sub_chunk_id == MakeFourCC32(115, 116, 116, 115))  // fourcc equals stts
                {
                    read_chunk_stts(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(115, 116, 115, 122))  // fourcc equals stsz
                {
                    read_chunk_stsz(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(115, 116, 115, 99))   // fourcc equals stsc
                {
                    read_chunk_stsc(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(115, 116, 99, 111))   // fourcc equals stco
                {
                    read_chunk_stco(sub_chunk_len);
                }
                else
                {
                    Debug.WriteLine("(stbl) unknown chunk id: " + SplitFourCC(sub_chunk_id));
                    return 0;
                }

                size_remaining -= sub_chunk_len;
            }

            return 1;
        }

        /*
         * chunk to offset box
         */
        private void read_chunk_stco(int sub_chunk_len)
        {
            //skip header and size
            qtstream.Skip(4);

            int num_entries = qtstream.ReadUint32();

            res.stco = new int[num_entries];
            for (int i = 0; i < num_entries; i++)
            {
                res.stco[i] = qtstream.ReadUint32();
            }
        }

        /*
         * sample to chunk box
         */
        private void read_chunk_stsc(int sub_chunk_len)
        {
            //skip header and size
            //skip version and other junk
            qtstream.Skip(4);
            int num_entries = qtstream.ReadUint32();
            res.stsc = new ChunkInfo[num_entries];
            for (int i = 0; i < num_entries; i++)
            {
                ChunkInfo entry = new ChunkInfo();
                entry.first_chunk = qtstream.ReadUint32();
                entry.samples_per_chunk = qtstream.ReadUint32();
                entry.sample_desc_index = qtstream.ReadUint32();
                res.stsc[i] = entry;
            }
        }

        int read_chunk_minf(int chunk_len)
        {
            int dinf_size;
            int stbl_size;
            int size_remaining = chunk_len - 8; // FIXME WRONG
            int media_info_size;

            /**** SOUND HEADER CHUNK ****/

            try
            {
                media_info_size = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading media_info_size - possibly number too large: {0}", e);
                media_info_size = 0;
            }

            if (media_info_size != 16)
            {
                Debug.WriteLine("unexpected size in media info\n");
                return 0;
            }
            if (qtstream.ReadUint32() != MakeFourCC32(115, 109, 104, 100))   // "smhd" ascii values
            {
                Debug.WriteLine("not a sound header! can't handle this.");
                return 0;
            }
            /* now skip the rest */
            qtstream.Skip(16 - 8);
            size_remaining -= 16;
            /****/

            /**** DINF CHUNK ****/

            try
            {
                dinf_size = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading dinf_size - possibly number too large: {0}", e);
                dinf_size = 0;
            }

            if (qtstream.ReadUint32() != MakeFourCC32(100, 105, 110, 102))   // "dinf" ascii values
            {
                Debug.WriteLine("expected dinf, didn't get it.");
                return 0;
            }
            /* skip it */
            qtstream.Skip(dinf_size - 8);
            size_remaining -= dinf_size;
            /****/


            /**** SAMPLE TABLE ****/
            try
            {
                stbl_size = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_minf) error reading stbl_size - possibly number too large: {0}", e);
                stbl_size = 0;
            }

            if (qtstream.ReadUint32() != MakeFourCC32(115, 116, 98, 108))    // "stbl" ascii values
            {
                Debug.WriteLine("expected stbl, didn't get it.");
                return 0;
            }
            if (read_chunk_stbl(stbl_size) == 0)
                return 0;
            size_remaining -= stbl_size;

            if (size_remaining != 0)
            {
                Debug.WriteLine("(read_chunk_minf) - size remaining?");
                qtstream.Skip(size_remaining);
            }

            return 1;
        }

        int read_chunk_mdia(int chunk_len)
        {
            int size_remaining = chunk_len - 8; // FIXME WRONG

            while (size_remaining != 0)
            {
                int sub_chunk_len;
                int sub_chunk_id = 0;

                try
                {
                    sub_chunk_len = (qtstream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_mdia) error reading sub_chunk_len - possibly number too large: {0}", e);
                    sub_chunk_len = 0;
                }

                if (sub_chunk_len <= 1 || sub_chunk_len > size_remaining)
                {
                    Debug.WriteLine("strange size for chunk inside mdia\n");
                    return 0;
                }

                sub_chunk_id = qtstream.ReadUint32();

                if (sub_chunk_id == MakeFourCC32(109, 100, 104, 100))   // fourcc equals mdhd
                {
                    read_chunk_mdhd(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(104, 100, 108, 114))  // fourcc equals hdlr
                {
                    read_chunk_hdlr(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(109, 105, 110, 102))  // fourcc equals minf
                {
                    if (read_chunk_minf(sub_chunk_len) == 0)
                        return 0;
                }
                else
                {
                    Debug.WriteLine("(mdia) unknown chunk id: " + SplitFourCC(sub_chunk_id));
                    return 0;
                }

                size_remaining -= sub_chunk_len;
            }

            return 1;
        }

        void read_chunk_hdlr(int chunk_len)
        {
            int comptype = 0;
            int compsubtype = 0;
            int size_remaining = chunk_len - 8; // FIXME WRONG

            int strlen;

            /* version */
            qtstream.ReadUint8();
            size_remaining -= 1;
            /* flags */
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            size_remaining -= 3;

            /* component type */
            comptype = qtstream.ReadUint32();
            compsubtype = qtstream.ReadUint32();
            size_remaining -= 8;

            /* component manufacturer */
            qtstream.ReadUint32();
            size_remaining -= 4;

            /* flags */
            qtstream.ReadUint32();
            qtstream.ReadUint32();
            size_remaining -= 8;

            /* name */
            strlen = qtstream.ReadUint8();

            /* 
            ** rewrote this to handle case where we actually read more than required 
            ** so here we work out how much we need to read first
            */

            size_remaining -= 1;

            qtstream.Skip(size_remaining);
        }

        int read_chunk_stsd(int chunk_len)
        {
            int i;
            int numentries = 0;
            int size_remaining = chunk_len - 8; // FIXME WRONG

            /* version */
            qtstream.ReadUint8();
            size_remaining -= 1;
            /* flags */
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            size_remaining -= 3;

            try
            {
                numentries = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stsd) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }


            size_remaining -= 4;

            if (numentries != 1)
            {
                Debug.WriteLine("only expecting one entry in sample description atom!");
                return 0;
            }

            for (i = 0; i < numentries; i++)
            {
                /* parse the alac atom contained within the stsd atom */
                int entry_size;
                int version;

                int entry_remaining;

                entry_size = qtstream.ReadUint32();
                res.format = qtstream.ReadUint32();
                entry_remaining = entry_size;
                entry_remaining -= 8;

                if (res.format != MakeFourCC32(97, 108, 97, 99))    // "alac" ascii values
                {
                    Debug.WriteLine("(read_chunk_stsd) error reading description atom - expecting alac, got " + SplitFourCC(res.format));
                    return 0;
                }

                /* sound info: */

                qtstream.Skip(6); // reserved
                entry_remaining -= 6;

                version = qtstream.ReadUint16();

                if (version != 1)
                    Debug.WriteLine("unknown version??");
                entry_remaining -= 2;

                /* revision level */
                qtstream.ReadUint16();
                /* vendor */
                qtstream.ReadUint32();
                entry_remaining -= 6;

                /* EH?? spec doesn't say theres an extra 16 bits here.. but there is! */
                qtstream.ReadUint16();
                entry_remaining -= 2;

                /* skip 4 - this is the top level num of channels and bits per sample */
                qtstream.Skip(4);
                entry_remaining -= 4;

                /* compression id */
                qtstream.ReadUint16();
                /* packet size */
                qtstream.ReadUint16();
                entry_remaining -= 4;

                /* skip 4 - this is the top level sample rate */
                qtstream.Skip(4);
                entry_remaining -= 4;

                /* remaining is codec data */

                /* 12 = audio format atom, 8 = padding */
                res.codecdata_len = entry_remaining + 12 + 8;

                if (res.codecdata_len > res.codecdata.Length)
                {
                    Debug.WriteLine("(read_chunk_stsd) unexpected codec data length read from atom " + res.codecdata_len);
                    return 0;
                }

                for (int count = 0; count < res.codecdata_len; count++)
                {
                    res.codecdata[count] = 0;
                }

                /* audio format atom */
                res.codecdata[0] = 0x0c000000;
                res.codecdata[1] = MakeFourCC(97, 109, 114, 102);       // "amrf" ascii values
                res.codecdata[2] = MakeFourCC(99, 97, 108, 97);     // "cala" ascii values

                qtstream.Read(entry_remaining, res.codecdata, 12);  // codecdata buffer should be +12
                entry_remaining -= entry_remaining;

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

                res.sample_size = (res.codecdata[ptrIndex] & 0xff);

                ptrIndex = 33;  // position of num of channels

                res.num_channels = (res.codecdata[ptrIndex] & 0xff);

                ptrIndex = 44;      // position of sample rate within codec data buffer

                res.sample_rate = (((res.codecdata[ptrIndex] & 0xff) << 24) | ((res.codecdata[ptrIndex + 1] & 0xff) << 16) | ((res.codecdata[ptrIndex + 2] & 0xff) << 8) | (res.codecdata[ptrIndex + 3] & 0xff));

                if (entry_remaining != 0)   // was comparing to null
                    qtstream.Skip(entry_remaining);

                res.format_read = 1;
                if (res.format != MakeFourCC32(97, 108, 97, 99))        // "alac" ascii values
                {
                    return 0;
                }
            }

            return 1;
        }

        void read_chunk_stts(int chunk_len)
        {
            int i;
            int numentries = 0;
            int size_remaining = chunk_len - 8; // FIXME WRONG

            /* version */
            qtstream.ReadUint8();
            size_remaining -= 1;
            /* flags */
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            size_remaining -= 3;

            try
            {
                numentries = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stts) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }

            size_remaining -= 4;

            res.num_time_to_samples = numentries;

            for (i = 0; i < numentries; i++)
            {
                res.time_to_sample[i].sample_count = (qtstream.ReadUint32());
                res.time_to_sample[i].sample_duration = (qtstream.ReadUint32());
                size_remaining -= 8;
            }

            if (size_remaining != 0)
            {
                Debug.WriteLine("(read_chunk_stts) size remaining?");
                qtstream.Skip(size_remaining);
            }
        }

        void read_chunk_stsz(int chunk_len)
        {
            int i;
            int numentries = 0;
            int uniform_size = 0;
            int size_remaining = chunk_len - 8; // FIXME WRONG

            /* version */
            qtstream.ReadUint8();
            size_remaining -= 1;
            /* flags */
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            qtstream.ReadUint8();
            size_remaining -= 3;

            /* default sample size */
            uniform_size = (qtstream.ReadUint32());
            if (uniform_size != 0)
            {
                /*
                ** Normally files have intiable sample sizes, this handles the case where
                ** they are all the same size
                */

                int uniform_num = 0;

                uniform_num = (qtstream.ReadUint32());

                res.sample_byte_size = new int[uniform_num];

                for (i = 0; i < uniform_num; i++)
                {
                    res.sample_byte_size[i] = uniform_size;
                }
                size_remaining -= 4;
                return;
            }
            size_remaining -= 4;

            try
            {
                numentries = (qtstream.ReadUint32());
            }
            catch (Exception e)
            {
                Debug.WriteLine("(read_chunk_stsz) error reading numentries - possibly number too large: {0}", e);
                numentries = 0;
            }

            size_remaining -= 4;

            res.sample_byte_size = new int[numentries];

            for (i = 0; i < numentries; i++)
            {
                res.sample_byte_size[i] = (qtstream.ReadUint32());

                size_remaining -= 4;
            }

            if (size_remaining != 0)
            {
                Debug.WriteLine("(read_chunk_stsz) size remaining?");
                qtstream.Skip(size_remaining);
            }
        }


        void read_chunk_tkhd(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        void read_chunk_mdhd(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        void read_chunk_edts(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        /* 'mvhd' movie header atom */
        void read_chunk_mvhd(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        /* 'udta' user data.. contains tag info */
        void read_chunk_udta(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        /* 'iods' */
        void read_chunk_iods(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        void read_chunk_elst(int chunk_len)
        {
            /* don't need anything from here atm, skip */
            int size_remaining = chunk_len - 8; // FIXME WRONG

            qtstream.Skip(size_remaining);
        }

        /* 'moov' movie atom - contains other atoms */
        int read_chunk_moov(int chunk_len)
        {
            int size_remaining = chunk_len - 8; // FIXME WRONG

            while (size_remaining != 0)
            {
                int sub_chunk_len;
                int sub_chunk_id = 0;

                try
                {
                    sub_chunk_len = (qtstream.ReadUint32());
                }
                catch (Exception e)
                {
                    Debug.WriteLine("(read_chunk_moov) error reading sub_chunk_len - possibly number too large: {0}", e);
                    sub_chunk_len = 0;
                }

                if (sub_chunk_len <= 1 || sub_chunk_len > size_remaining)
                {
                    Debug.WriteLine("strange size for chunk inside moov");
                    return 0;
                }

                sub_chunk_id = qtstream.ReadUint32();

                if (sub_chunk_id == MakeFourCC32(109, 118, 104, 100))   // fourcc equals mvhd
                {
                    read_chunk_mvhd(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(116, 114, 97, 107))   // fourcc equals trak
                {
                    if (read_chunk_trak(sub_chunk_len) == 0)
                        return 0;
                }
                else if (sub_chunk_id == MakeFourCC32(117, 100, 116, 97))   // fourcc equals udta
                {
                    read_chunk_udta(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(101, 108, 115, 116))  // fourcc equals elst
                {
                    read_chunk_elst(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(105, 111, 100, 115))  // fourcc equals iods
                {
                    read_chunk_iods(sub_chunk_len);
                }
                else if (sub_chunk_id == MakeFourCC32(102, 114, 101, 101))     // fourcc equals free
                {
                    qtstream.Skip(sub_chunk_len - 8); // FIXME not 8
                }
                else
                {
                    Debug.WriteLine("(moov) unknown chunk id: " + SplitFourCC(sub_chunk_id));
                    return 0;
                }

                size_remaining -= sub_chunk_len;
            }

            return 1;
        }

        void read_chunk_mdat(int chunk_len, int skip_mdat)
        {
            int size_remaining = chunk_len - 8; // FIXME WRONG

            if (size_remaining == 0)
                return;

            res.mdat_len = size_remaining;
            if (skip_mdat != 0)
            {
                saved_mdat_pos = qtstream.Position;

                qtstream.Skip(size_remaining);
            }
        }

        int set_saved_mdat()
        {
            // returns as follows
            // 1 - all ok
            // 2 - do not have valid saved mdat pos
            // 3 - have valid saved mdat pos, but cannot seek there - need to close/reopen stream

            if (saved_mdat_pos == -1)
            {
                Debug.WriteLine("stream contains mdat before moov but is not seekable");
                return 2;
            }

            if (qtstream.Seek(saved_mdat_pos) != 0)
            {
                return 3;
            }

            return 1;
        }


    }
}
