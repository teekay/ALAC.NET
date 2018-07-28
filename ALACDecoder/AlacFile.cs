/**
** Copyright (c) 2015 - 2018 Tomas Kohl
** Based on Java Apple Lossless Decoder - Copyright (c) 2011 Peter McQuillan, https://github.com/soiaf/Java-Apple-Lossless-decoder
**
** All Rights Reserved.
**                       
** Distributed under the BSD Software License (see license.txt)  
**/
using System;
using System.Collections.Generic;
using System.Linq;

namespace ALACdotNET.Decoder
{
    internal class AlacFile
    {
        public AlacFile(int samplesize, int numchannels)
        {
            _numchannels = numchannels;
            _bytespersample = samplesize / 8 * numchannels;
        }

        private byte[] _inputBuffer;
        private int _ibIdx;

        private int _inputBufferBitaccumulator; /* used so we can do arbitary bit reads */

        private readonly int _numchannels;
        private readonly int _bytespersample;

        private const int BufferSize = 16384;
        /* buffers */
        private readonly int[] _predicterrorBufferA = new int[BufferSize];
        private readonly int[] _predicterrorBufferB = new int[BufferSize];

        private int[] _outputsamplesBufferA = new int[BufferSize];
        private int[] _outputsamplesBufferB = new int[BufferSize];

        private readonly int[] _uncompressedBytesBufferA = new int[BufferSize];
        private readonly int[] _uncompressedBytesBufferB = new int[BufferSize];

        /* stuff from setinfo */
        private int _setinfoMaxSamplesPerFrame; // 0x1000 = 4096 /* max samples per frame? */

        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo_7A; // 0x00
        private int _setinfoSampleSize; // 0x10
        private int _setinfoRiceHistorymult; // 0x28
        private int _setinfoRiceInitialhistory; // 0x0a
        private int _setinfoRiceKmodifier; // 0x0e
        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo_7F; // 0x02
        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo80; // 0x00ff
        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo82; // 0x000020e7 /* max sample size?? */
        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo86; // 0x00069fe4 /* bit rate (avarge)?? */
        // ReSharper disable once NotAccessedField.Local - may be used in the future to provide some interesting info
        private int _setinfo_8ARate; // 0x0000ac44

        private readonly int[] _predictorCoefTable = new int[1024];
        private readonly int[] _predictorCoefTableA = new int[1024];
        private readonly int[] _predictorCoefTableB = new int[1024];

        private const int RiceThreshold = 8;
        
        public void SetInfo(int[] inputbuffer)
        {
            int ptrIndex = 0;
            ptrIndex += 4; // size
            ptrIndex += 4; // frma
            ptrIndex += 4; // alac
            ptrIndex += 4; // size
            ptrIndex += 4; // alac

            ptrIndex += 4; // 0 ?

            _setinfoMaxSamplesPerFrame = (inputbuffer[ptrIndex] << 24) + (inputbuffer[ptrIndex + 1] << 16) + (inputbuffer[ptrIndex + 2] << 8) + inputbuffer[ptrIndex + 3]; // buffer size / 2 ?
            ptrIndex += 4;
            _setinfo_7A = inputbuffer[ptrIndex];
            ptrIndex += 1;
            _setinfoSampleSize = inputbuffer[ptrIndex];
            ptrIndex += 1;
            _setinfoRiceHistorymult = inputbuffer[ptrIndex] & 0xff;
            ptrIndex += 1;
            _setinfoRiceInitialhistory = inputbuffer[ptrIndex] & 0xff;
            ptrIndex += 1;
            _setinfoRiceKmodifier = inputbuffer[ptrIndex] & 0xff;
            ptrIndex += 1;
            _setinfo_7F = inputbuffer[ptrIndex];
            ptrIndex += 1;
            _setinfo80 = (inputbuffer[ptrIndex] << 8) + inputbuffer[ptrIndex + 1];
            ptrIndex += 2;
            _setinfo82 = (inputbuffer[ptrIndex] << 24) + (inputbuffer[ptrIndex + 1] << 16) + (inputbuffer[ptrIndex + 2] << 8) + inputbuffer[ptrIndex + 3];
            ptrIndex += 4;
            _setinfo86 = (inputbuffer[ptrIndex] << 24) + (inputbuffer[ptrIndex + 1] << 16) + (inputbuffer[ptrIndex + 2] << 8) + inputbuffer[ptrIndex + 3];
            ptrIndex += 4;
            _setinfo_8ARate = (inputbuffer[ptrIndex] << 24) + (inputbuffer[ptrIndex + 1] << 16) + (inputbuffer[ptrIndex + 2] << 8) + inputbuffer[ptrIndex + 3];
            //ptrIndex += 4;
        }

        /* stream reading */

        /* supports reading 1 to 16 bits, in big endian format */
        private int Readbits16(int bits)
        {
            var part1 = _inputBuffer[_ibIdx] & 0xff;
            var part2 = _inputBuffer[_ibIdx + 1] & 0xff;
            var part3 = _inputBuffer[_ibIdx + 2] & 0xff;
            /* shift left by the number of bits we've already read,
             * so that the top 'n' bits of the 24 bits we read will
             * be the return bits */
            /* and then only want the top 'n' bits from that, where
             * n is 'bits' */
            var result = ((((part1 << 16) | (part2 << 8) | part3) << _inputBufferBitaccumulator) & 0x00ffffff) >> (24 - bits);
            var newAccumulator = _inputBufferBitaccumulator + bits;
            /* increase the buffer pointer if we've read over n bytes. */
            _ibIdx += newAccumulator >> 3;
            /* and the remainder goes back into the bit accumulator */
            _inputBufferBitaccumulator = newAccumulator & 7;
            return result;
        }

        /* supports reading 1 to 32 bits, in big endian format */
        private int Readbits(int bitsParam)
        {
            int bits = bitsParam <= 16 ? bitsParam : bitsParam - 16;
            return (bitsParam <= 16 ? 0 : Readbits16(16) << bits) | Readbits16(bits);
        }

        /* reads a single bit */
        private int Readbit()
        {
            var part1 = _inputBuffer[_ibIdx] & 0xff;
            var result = (part1 << _inputBufferBitaccumulator) >> 7 & 1;
            var newAccumulator = _inputBufferBitaccumulator + 1;
            _ibIdx += newAccumulator / 8;
            _inputBufferBitaccumulator = newAccumulator % 8;
            return result;
        }

        private void Unreadbits(int bits)
        {
            int newAccumulator = _inputBufferBitaccumulator - bits;
            _ibIdx += newAccumulator >> 3;
            _inputBufferBitaccumulator = newAccumulator & 7;
            if (_inputBufferBitaccumulator < 0)
                _inputBufferBitaccumulator *= -1;
        }

        private int CountLeadingZerosExtra(int curbyteParam, int initialZeroes)
        {
            var initialCondition = (curbyteParam & 0xf0) == 0;
            var zeroes = initialCondition ? initialZeroes + 4 : initialZeroes;
            var curbyte = !initialCondition ? curbyteParam : curbyteParam >> 4;
            var conditions = new List<(bool, int)>{
                ((curbyte & 0x8) != 0, 0),
                ((curbyte & 0x4) != 0, 1),
                ((curbyte & 0x2) != 0, 2),
                ((curbyte & 0x1) != 0, 3),
                /* shouldn't get here: */
                (true, 4)
            };
            return conditions.First(c => c.Item1).Item2 + zeroes;
        }

        private int CountLeadingZeros(int input)
        {
            int zeros = 0;
            var curbyte = input >> 24;
            if (curbyte != 0)
            {
                return CountLeadingZerosExtra(curbyte, zeros);
            }
            zeros += 8;

            curbyte = input >> 16;
            if ((curbyte & 0xFF) != 0)
            {
                return CountLeadingZerosExtra(curbyte, zeros);
            }
            zeros += 8;

            curbyte = input >> 8;
            if ((curbyte & 0xFF) != 0)
            {
                return CountLeadingZerosExtra(curbyte, zeros);
            }
            zeros += 8;

            curbyte = input;
            if ((curbyte & 0xFF) != 0)
            {
                return CountLeadingZerosExtra(curbyte, zeros);
            }

            return zeros + 8;
        }

        private int EntropyDecodeValue(int readSampleSize, int k, int riceKmodifierMask)
        {
            int decodedValue = 0;
            // read x, number of 1s before 0 represent the rice value.
            while (decodedValue <= RiceThreshold && Readbit() != 0)
            {
                decodedValue++;
            }
            if (decodedValue > RiceThreshold)
            {
                // read the number from the bit stream (raw value), and mask value
                decodedValue = Readbits(readSampleSize) & (int)(0xffffffff >> (32 - readSampleSize));
            }
            else
            {
                if (k != 1)
                {
                    int extraBits = Readbits(k);
                    decodedValue *= ((1 << k) - 1) & riceKmodifierMask;
                    if (extraBits > 1)
                        decodedValue += extraBits - 1;
                    else
                        Unreadbits(1);
                }
            }
            return decodedValue;
        }

        private void EntropyRiceDecode(int[] outputBuffer, int outputSize, int readSampleSize, int riceInitialhistory, int riceKmodifier, int riceHistorymult, int riceKmodifierMask)
        {
            int history = riceInitialhistory;
            int outputCount = 0;
            int signModifier = 0;

            while (outputCount < outputSize)
            {
                var k = 31 - riceKmodifier - CountLeadingZeros((history >> 9) + 3);
                if (k < 0)
                    k += riceKmodifier;
                else
                    k = riceKmodifier;

                // note: don't use rice_kmodifier_mask here (set mask to 0xFFFFFFFF)
                var decodedValue = EntropyDecodeValue(readSampleSize, k, 0xFFFFFFFF);

                decodedValue += signModifier;
                var finalValue = (decodedValue + 1) / 2;
                if ((decodedValue & 1) != 0) // the sign is stored in the low bit
                    finalValue *= -1;

                outputBuffer[outputCount] = finalValue;
                signModifier = 0;

                // update history
                history += decodedValue * riceHistorymult - ((history * riceHistorymult) >> 9);

                if (decodedValue > 0xFFFF)
                    history = 0xFFFF;

                // special case, for compressed blocks of 0
                if (history < 128 && outputCount + 1 < outputSize)
                {
                    signModifier = 1;
                    k = CountLeadingZeros(history) + (history + 16) / 64 - 24;

                    // note: blockSize is always 16bit
                    var blockSize = EntropyDecodeValue(16, k, riceKmodifierMask);

                    // got blockSize 0s
                    if (blockSize > 0)
                    {
                        for (int j = 0; j < blockSize; j++)
                        {
                            outputBuffer[outputCount + 1 + j] = 0;
                        }
                        outputCount += blockSize;
                    }

                    if (blockSize > 0xFFFF)
                        signModifier = 0;

                    history = 0;
                }

                outputCount++;
            }
        }

        private int EntropyDecodeValue(int readSampleSize, int k, uint v)
        {
            return EntropyDecodeValue(readSampleSize, k, (int)v);
        }

        private int[] PredictorDecompressFirAdapt(int[] errorBuffer, int outputSize, int readsamplesize, int[] predictorCoefTable, int predictorCoefNum, int predictorQuantitization)
        {
            int bitsmove;

            /* first sample always copies */
            var bufferOut = errorBuffer;
            if (predictorCoefNum == 0)
            {
                if (outputSize <= 1)
                    return bufferOut;
                var sizeToCopy = (outputSize - 1) * 4;
                Array.Copy(errorBuffer, 1, bufferOut, 1, sizeToCopy);
                return bufferOut;
            }

            if (predictorCoefNum == 0x1f) // 11111 - max value of predictor_coef_num
            {
                /* second-best case scenario for fir decompression,
                   * error describes a small difference from the previous sample only
                   */
                if (outputSize <= 1)
                    return bufferOut;

                for (int i = 0; i < outputSize - 1; i++)
                {
                    var prevValue = bufferOut[i];
                    var errorValue = errorBuffer[i + 1];
                    bitsmove = 32 - readsamplesize;
                    bufferOut[i + 1] = ((prevValue + errorValue) << bitsmove) >> bitsmove;
                }

                return bufferOut;
            }

            /* read warm-up samples */
            if (predictorCoefNum > 0)
            {
                for (int i = 0; i < predictorCoefNum; i++)
                {
                    var val = bufferOut[i] + errorBuffer[i + 1];
                    bitsmove = 32 - readsamplesize;
                    val = (val << bitsmove) >> bitsmove;
                    bufferOut[i + 1] = val;
                }
            }

            if (predictorCoefNum <= 0) return bufferOut;
            /* general case */
            var bufferOutIdx = 0;
            for (int i = predictorCoefNum + 1; i < outputSize; i++)
            {
                int j;
                int sum = 0;
                int errorVal = errorBuffer[i];
                for (j = 0; j < predictorCoefNum; j++)
                {
                    sum += (bufferOut[bufferOutIdx + predictorCoefNum - j] - bufferOut[bufferOutIdx]) *
                           predictorCoefTable[j];
                }

                bitsmove = 32 - readsamplesize;
                var outval = ((((1 << (predictorQuantitization - 1)) + sum) >>
                               predictorQuantitization + bufferOut[bufferOutIdx] + errorVal)
                              << bitsmove) >> bitsmove;
                bufferOut[bufferOutIdx + predictorCoefNum + 1] = outval;

                if (errorVal != 0)
                {
                    Func<int, bool> whileValGt0 = (xxx) => xxx > 0;
                    Func<int, bool> whileValLt0 = (xxx) => xxx < 0;
                    Func<int, bool> conditionToUse = errorVal > 0 ? whileValGt0 : whileValLt0;
                    Func<int, int> intAsIs = x => x;
                    Func<int, int> intNeg = x => -x;
                    Func<int, int> intPosOrNeg = conditionToUse == whileValGt0 ? intAsIs : intNeg;

                    int predictorNum = predictorCoefNum - 1;
                    while (predictorNum >= 0 && conditionToUse(errorVal))
                    {
                        int val = bufferOut[bufferOutIdx] - bufferOut[bufferOutIdx + predictorCoefNum - predictorNum];
                        int sign = intPosOrNeg(val < 0 ? -1 : (val > 0 ? 1 : 0));

                        predictorCoefTable[predictorNum] -= sign;
                        val *= sign; // absolute, or negative, value                        
                        errorVal -= (val >> predictorQuantitization) * (predictorCoefNum - predictorNum);
                        predictorNum--;
                    }
                }

                bufferOutIdx++;
            }
            return bufferOut;
        }


        private void Deinterlace16(int[] bufferA, int[] bufferB, int[] bufferOut, int numchannels, int numsamples, int interlacingShift, int interlacingLeftweight)
        {
            if (numsamples <= 0) return;

            /* weighted interlacing */
            if (0 != interlacingLeftweight)
            {
                for (int i = 0; i < numsamples; i++)
                {
                    var midright = bufferA[i];
                    var difference = bufferB[i];

                    var right = midright - ((difference * interlacingLeftweight) >> interlacingShift);
                    var left = right + difference;

                    /* output is always little endian */
                    bufferOut[i * numchannels] = left;
                    bufferOut[i * numchannels + 1] = right;
                }
                return;
            }

            /* otherwise basic interlacing took place */
            for (int i = 0; i < numsamples; i++)
            {
                var left = bufferA[i];
                var right = bufferB[i];

                /* output is always little endian */
                bufferOut[i * numchannels] = left;
                bufferOut[i * numchannels + 1] = right;
            }
        }


        private void Deinterlace24(int[] bufferA, int[] bufferB, int uncompressedBytes, int[] uncompressedBytesBufferA, int[] uncompressedBytesBufferB, int[] bufferOut, int numchannels, int numsamples, int interlacingShift, int interlacingLeftweight)
        {
            if (numsamples <= 0) return;

            /* weighted interlacing */
            if (interlacingLeftweight != 0)
            {
                for (int i = 0; i < numsamples; i++)
                {
                    var midright = bufferA[i];
                    var difference = bufferB[i];

                    var right = midright - ((difference * interlacingLeftweight) >> interlacingShift);
                    var left = right + difference;

                    if (uncompressedBytes != 0)
                    {
                        int mask = (int)~(0xFFFFFFFF << (uncompressedBytes * 8));
                        left <<= uncompressedBytes * 8;
                        right <<= uncompressedBytes * 8;

                        left = left | (uncompressedBytesBufferA[i] & mask);
                        right = right | (uncompressedBytesBufferB[i] & mask);
                    }

                    bufferOut[i * numchannels * 3] = left & 0xFF;
                    bufferOut[i * numchannels * 3 + 1] = (left >> 8) & 0xFF;
                    bufferOut[i * numchannels * 3 + 2] = (left >> 16) & 0xFF;

                    bufferOut[i * numchannels * 3 + 3] = right & 0xFF;
                    bufferOut[i * numchannels * 3 + 4] = (right >> 8) & 0xFF;
                    bufferOut[i * numchannels * 3 + 5] = (right >> 16) & 0xFF;
                }
                return;
            }

            /* otherwise basic interlacing took place */
            for (int i = 0; i < numsamples; i++)
            {
                var left = bufferA[i];
                var right = bufferB[i];

                if (uncompressedBytes != 0)
                {
                    int mask = (int)~(0xFFFFFFFF << (uncompressedBytes * 8));
                    left <<= uncompressedBytes * 8;
                    right <<= uncompressedBytes * 8;

                    left = left | (uncompressedBytesBufferA[i] & mask);
                    right = right | (uncompressedBytesBufferB[i] & mask);
                }

                bufferOut[i * numchannels * 3] = left & 0xFF;
                bufferOut[i * numchannels * 3 + 1] = (left >> 8) & 0xFF;
                bufferOut[i * numchannels * 3 + 2] = (left >> 16) & 0xFF;

                bufferOut[i * numchannels * 3 + 3] = right & 0xFF;
                bufferOut[i * numchannels * 3 + 4] = (right >> 8) & 0xFF;
                bufferOut[i * numchannels * 3 + 5] = (right >> 16) & 0xFF;
            }

        }

        /// <summary>
        /// Decode an ALAC frame
        /// </summary>
        /// <param name="inbuffer">Input buffer</param>
        /// <param name="outbuffer">Output buffer</param>
        /// <param name="outputsizeParam">Output size in bytes</param>
        /// <returns></returns>
        public int DecodeFrame(byte[] inbuffer, int[] outbuffer, int outputsizeParam)
        {
            int outputsamples = _setinfoMaxSamplesPerFrame;
            /* setup the stream */
            _inputBuffer = inbuffer;
            _inputBufferBitaccumulator = 0;
            _ibIdx = 0;
            var channels = Readbits(3);
            int outputsize = outputsamples * _bytespersample;

            if (channels == 0) // 1 channel
            {
                /* 2^result = something to do with output waiting.
                 * perhaps matters if we read > 1 frame in a pass?
                 */
                Readbits(4);
                Readbits(12); // unknown, skip 12 bits

                var hassize = Readbits(1);
                var uncompressedBytes = Readbits(2);
                var isnotcompressed = Readbits(1);

                if (hassize != 0)
                {
                    /* now read the number of samples,
                     * as a 32bit integer */
                    outputsamples = Readbits(32);
                    outputsize = outputsamples * _bytespersample;
                }

                var readsamplesize = _setinfoSampleSize - uncompressedBytes * 8;

                if (isnotcompressed == 0)
                { // so it is compressed

                    /* skip 16 bits, not sure what they are. seem to be used in
                     * two channel case */
                    Readbits(8);
                    Readbits(8);

                    var predictionType = Readbits(4);
                    var predictionQuantitization = Readbits(4);

                    var ricemodifier = Readbits(3);
                    var predictorCoefNum = Readbits(5);

                    /* read the predictor table */

                    for (int i = 0; i < predictorCoefNum; i++)
                    {
                        var tempPred = Readbits(16);
                        if (tempPred > 32767)
                        {
                            // the predictor coef table values are only 16 bit signed
                            tempPred = tempPred - 65536;
                        }
                        _predictorCoefTable[i] = tempPred;
                    }

                    if (uncompressedBytes != 0)
                    {
                        for (int i = 0; i < outputsamples; i++)
                        {
                            _uncompressedBytesBufferA[i] = Readbits(uncompressedBytes * 8);
                        }
                    }

                    EntropyRiceDecode(_predicterrorBufferA, outputsamples, readsamplesize, _setinfoRiceInitialhistory, _setinfoRiceKmodifier, ricemodifier * (_setinfoRiceHistorymult / 4), (1 << _setinfoRiceKmodifier) - 1);

                    if (predictionType == 0)
                    { // adaptive fir
                        _outputsamplesBufferA = PredictorDecompressFirAdapt(_predicterrorBufferA, outputsamples, readsamplesize, _predictorCoefTable, predictorCoefNum, predictionQuantitization);
                    }
                    else
                    {

                        /* i think the only other prediction type (or perhaps this is just a
                         * boolean?) runs adaptive fir twice.. like:
                         * predictor_decompress_fir_adapt(predictor_error, tempout, ...)
                         * predictor_decompress_fir_adapt(predictor_error, outputsamples ...)
                         * little strange..
                         */
                    }

                }
                else
                { // not compressed, easy case
                    if (_setinfoSampleSize <= 16)
                    {
                        for (int i = 0; i < outputsamples; i++)
                        {
                            int audiobits = Readbits(_setinfoSampleSize);
                            var bitsmove = 32 - _setinfoSampleSize;
                            audiobits = (audiobits << bitsmove) >> bitsmove;
                            _outputsamplesBufferA[i] = audiobits;
                        }
                    }
                    else
                    {
                        const int m = 1 << (24 - 1);
                        for (int i = 0; i < outputsamples; i++)
                        {
                            var audiobits = Readbits(16);
                            /* special case of sign extension..
                             * as we'll be ORing the low 16bits into this */
                            audiobits = audiobits << (_setinfoSampleSize - 16);
                            audiobits = audiobits | Readbits(_setinfoSampleSize - 16);
                            var x = audiobits & ((1 << 24) - 1);
                            audiobits = (x ^ m) - m;    // sign extend 24 bits
                            _outputsamplesBufferA[i] = audiobits;
                        }
                    }
                    uncompressedBytes = 0; // always 0 for uncompressed
                }

                switch (_setinfoSampleSize)
                {
                    case 16:
                        {

                            for (int i = 0; i < outputsamples; i++)
                            {
                                int sample = _outputsamplesBufferA[i];
                                outbuffer[i * _numchannels] = sample;

                                /*
                                ** We have to handle the case where the data is actually mono, but the stsd atom says it has 2 channels
                                ** in this case we create a stereo file where one of the channels is silent. If mono and 1 channel this value 
                                ** will be overwritten in the next iteration
                                */

                                outbuffer[i * _numchannels + 1] = 0;
                            }
                            break;
                        }
                    case 24:
                        {
                            for (int i = 0; i < outputsamples; i++)
                            {
                                int sample = _outputsamplesBufferA[i];

                                if (uncompressedBytes != 0)
                                {
                                    sample = sample << (uncompressedBytes * 8);
                                    var mask = (int)~(0xFFFFFFFF << (uncompressedBytes * 8));
                                    sample = sample | (_uncompressedBytesBufferA[i] & mask);
                                }

                                outbuffer[i * _numchannels * 3] = sample & 0xFF;
                                outbuffer[i * _numchannels * 3 + 1] = (sample >> 8) & 0xFF;
                                outbuffer[i * _numchannels * 3 + 2] = (sample >> 16) & 0xFF;

                                /*
                                ** We have to handle the case where the data is actually mono, but the stsd atom says it has 2 channels
                                ** in this case we create a stereo file where one of the channels is silent. If mono and 1 channel this value 
                                ** will be overwritten in the next iteration
                                */

                                outbuffer[i * _numchannels * 3 + 3] = 0;
                                outbuffer[i * _numchannels * 3 + 4] = 0;
                                outbuffer[i * _numchannels * 3 + 5] = 0;

                            }
                            break;
                        }
                    // ReSharper disable once RedundantCaseLabel
                    case 20:
                    // ReSharper disable once RedundantCaseLabel
                    case 32:
                    default:
                        throw new Exception("FIXME: unimplemented sample size " + _setinfoSampleSize);
                }
            }
            else if (channels == 1) // 2 channels
            {
                int interlacingShift;
                int interlacingLeftweight;

                /* 2^result = something to do with output waiting.
                 * perhaps matters if we read > 1 frame in a pass?
                 */
                Readbits(4);
                Readbits(12); // unknown, skip 12 bits

                var hassize = Readbits(1);
                var uncompressedBytes = Readbits(2);           
                var isnotcompressed = Readbits(1);

                if (hassize != 0)
                {
                    /* now read the number of samples,
                     * as a 32bit integer */
                    outputsamples = Readbits(32);
                    outputsize = outputsamples * _bytespersample;
                }

                var readsamplesize = _setinfoSampleSize - uncompressedBytes * 8 + 1;
                if (isnotcompressed == 0)
                { // compressed
                    int tempPred;
                    interlacingShift = Readbits(8);
                    interlacingLeftweight = Readbits(8);

                    /******** channel 1 ***********/
                    var predictionTypeA = Readbits(4);
                    var predictionQuantitizationA = Readbits(4);

                    var ricemodifierA = Readbits(3);
                    var predictorCoefNumA = Readbits(5);

                    /* read the predictor table */

                    for (int i = 0; i < predictorCoefNumA; i++)
                    {
                        tempPred = Readbits(16);
                        if (tempPred > 32767)
                        {
                            // the predictor coef table values are only 16 bit signed
                            tempPred = tempPred - 65536;
                        }
                        _predictorCoefTableA[i] = tempPred;
                    }

                    /******** channel 2 *********/
                    var predictionTypeB = Readbits(4);
                    var predictionQuantitizationB = Readbits(4);

                    var ricemodifierB = Readbits(3);
                    var predictorCoefNumB = Readbits(5);

                    /* read the predictor table */
                    for (int i = 0; i < predictorCoefNumB; i++)
                    {
                        tempPred = Readbits(16);
                        if (tempPred > 32767)
                        {
                            // the predictor coef table values are only 16 bit signed
                            tempPred = tempPred - 65536;
                        }
                        _predictorCoefTableB[i] = tempPred;
                    }

                    /*********************/
                    if (uncompressedBytes != 0)
                    { // see mono case
                        for (int i = 0; i < outputsamples; i++)
                        {
                            _uncompressedBytesBufferA[i] = Readbits(uncompressedBytes * 8);
                            _uncompressedBytesBufferB[i] = Readbits(uncompressedBytes * 8);
                        }
                    }

                    /* channel 1 */
                    EntropyRiceDecode(_predicterrorBufferA, outputsamples, readsamplesize, _setinfoRiceInitialhistory, _setinfoRiceKmodifier, ricemodifierA * (_setinfoRiceHistorymult / 4), (1 << _setinfoRiceKmodifier) - 1);

                    if (predictionTypeA == 0)
                    { // adaptive fir
                        _outputsamplesBufferA = PredictorDecompressFirAdapt(_predicterrorBufferA, outputsamples, readsamplesize, _predictorCoefTableA, predictorCoefNumA, predictionQuantitizationA);

                    }
                    else
                    { // see mono case
                        throw new Exception("FIXME: unhandled predicition type: " + predictionTypeA);
                    }

                    /* channel 2 */
                    EntropyRiceDecode(_predicterrorBufferB, outputsamples, readsamplesize, _setinfoRiceInitialhistory, _setinfoRiceKmodifier, ricemodifierB * (_setinfoRiceHistorymult / 4), (1 << _setinfoRiceKmodifier) - 1);

                    if (predictionTypeB == 0)
                    { // adaptive fir
                        _outputsamplesBufferB = PredictorDecompressFirAdapt(_predicterrorBufferB, outputsamples, readsamplesize, _predictorCoefTableB, predictorCoefNumB, predictionQuantitizationB);
                    }
                    else
                    {
                        throw new Exception("FIXME: unhandled predicition type: " + predictionTypeB);
                    }
                }
                else
                { // not compressed, easy case
                    if (_setinfoSampleSize <= 16)
                    {
                        for (int i = 0; i < outputsamples; i++)
                        {
                            var audiobitsA = Readbits(_setinfoSampleSize);
                            var audiobitsB = Readbits(_setinfoSampleSize);

                            var bitsmove = 32 - _setinfoSampleSize;

                            audiobitsA = (audiobitsA << bitsmove) >> bitsmove;
                            audiobitsB = (audiobitsB << bitsmove) >> bitsmove;

                            _outputsamplesBufferA[i] = audiobitsA;
                            _outputsamplesBufferB[i] = audiobitsB;
                        }
                    }
                    else
                    {
                        const int m = 1 << (24 - 1);
                        for (int i = 0; i < outputsamples; i++)
                        {
                            var audiobitsA = Readbits(16);
                            audiobitsA = audiobitsA << (_setinfoSampleSize - 16);
                            audiobitsA = audiobitsA | Readbits(_setinfoSampleSize - 16);
                            var x = audiobitsA & ((1 << 24) - 1);
                            audiobitsA = (x ^ m) - m;        // sign extend 24 bits

                            var audiobitsB = Readbits(16);
                            audiobitsB = audiobitsB << (_setinfoSampleSize - 16);
                            audiobitsB = audiobitsB | Readbits(_setinfoSampleSize - 16);
                            x = audiobitsB & ((1 << 24) - 1);
                            audiobitsB = (x ^ m) - m;        // sign extend 24 bits

                            _outputsamplesBufferA[i] = audiobitsA;
                            _outputsamplesBufferB[i] = audiobitsB;
                        }
                    }
                    uncompressedBytes = 0; // always 0 for uncompressed
                    interlacingShift = 0;
                    interlacingLeftweight = 0;
                }

                switch (_setinfoSampleSize)
                {
                    case 16:
                        {
                            Deinterlace16(_outputsamplesBufferA, _outputsamplesBufferB, outbuffer, _numchannels, outputsamples, interlacingShift, interlacingLeftweight);
                            break;
                        }
                    case 24:
                        {
                            Deinterlace24(_outputsamplesBufferA, _outputsamplesBufferB, uncompressedBytes, _uncompressedBytesBufferA, _uncompressedBytesBufferB, outbuffer, _numchannels, outputsamples, interlacingShift, interlacingLeftweight);
                            break;
                        }
                    case 20:
                    case 32:
                        throw new Exception("FIXME: unimplemented sample size " + _setinfoSampleSize);

                }
            }
            return outputsize;
        }
    }
}