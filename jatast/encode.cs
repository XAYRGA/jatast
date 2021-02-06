using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;
using System.IO;

namespace jatast
{
    public enum EncodeFormat
    {
        ADPCM4 = 0,
        PCM16 = 1, 
        PCM8 = 2
    }

    public class encoder
    {
        private const int STRM_HEAD = 0x5354524D;
        private const int STRM_LENGTH = 0x00002760;
        private const int BLCK_HEAD = 0x424C434B;

        PCM16WAV wav;
        BeBinaryWriter writer;
        int sampleOffset;

        EncodeFormat format;

        private int[] last = new int[6];
        private int[] penultimate = new int[6];

        private int bytesPerFrame = 9;
        private int samplesPerFrame = 16;

        public uint loopStart = 0; 
        public uint loopEnd = 0;

        public encoder(PCM16WAV wavIn, EncodeFormat encodeFormat, Stream outputFile)
        {
            format = encodeFormat;
            wav = wavIn;
            writer = new BeBinaryWriter(outputFile);
        }

        public unsafe static byte[] PCM16ShortToByteBigEndian(short[] pcm)
        {
            var pcmB = new byte[pcm.Length*2];
            fixed (short* pcmD = pcm)
            {
                var pcmSh = (byte*)pcmD;
                //for (int i=0; i < pcm.Length;i++)
                for (int i = 0; i < pcmB.Length; i += 2)
                {
                    pcmB[i] = pcmSh[i + 1];
                    pcmB[i +1 ] = pcmSh[i]; 
                }
            }
            return pcmB;
        }



        public void encode()
        {
            writeHeader();
            switch (format)
            {
                case EncodeFormat.ADPCM4:
                    bytesPerFrame = 9;
                    samplesPerFrame = 16;
                    break;
                case EncodeFormat.PCM8:
                    bytesPerFrame = 1;
                    samplesPerFrame = 1;
                    break;
                case EncodeFormat.PCM16:
                    bytesPerFrame = 2;
                    samplesPerFrame = 1;
                    break;
            }


            var total_blocks = (((wav.sampleCount/ samplesPerFrame) * bytesPerFrame) + STRM_LENGTH - 1) / STRM_LENGTH;

            for (int i = 0; i < total_blocks; i++)
            {
                util.consoleProgress("Rendering block", i + 1 , total_blocks, true);
                writeBlock();
            }
            
            Console.WriteLine("\nDone.");
            var wl = writer.BaseStream.Position;
            //Console.WriteLine($"Offset long 0x{wl:X}");
            writer.BaseStream.Position = 4;
            writer.Write((int)(wl - 0x40 ));
            
            writer.Flush();
            writer.Close();
            //Console.WriteLine($"Indicated sample count {wav.sampleCount}\n\nSample count (based on file size) { ((samplesPerFrame * (((wl - 0x40) - (0x20 * total_blocks))) / bytesPerFrame) / wav.channels)}");
            //Console.ReadLine();

        }
        
        private void writeHeader()
        {
            var isLoop = false;
            var IloopStart = 0u;
            var IloopEnd = (uint)wav.sampleCount;

            if (loopEnd!=0)
            {
                isLoop = true;
                IloopStart = loopStart;
                IloopEnd = loopEnd;
            }else if (wav.sampler.loops != null)
            {
                isLoop = true;
                IloopStart = (uint)wav.sampler.loops[0].dwStart;
                IloopEnd = (uint)wav.sampler.loops[0].dwEnd;
            }



            writer.Write(STRM_HEAD);
            writer.Write((int)0x00000000); // Temporary length. 
            writer.Write((ushort)format);
            writer.Write((ushort)wav.bitsPerSample);
            writer.Write((ushort)wav.channels);
            writer.Write(isLoop ? (ushort)0xFFFF : (ushort)0x0000);
            writer.Write(wav.sampleRate);
            writer.Write(wav.sampleCount);
            writer.Write(IloopStart);
            writer.Write(IloopEnd);
            writer.Write(STRM_LENGTH);
            writer.Write(0);
            writer.Write(0x7F000000); // wat. 
            for (int i = 0; i < 0x14; i++)
                writer.Write((byte)0x00);
            writer.Flush();
        }


        private void writeBlock()
        {
            writer.Write(BLCK_HEAD);
            var remainingFrames =  ( (wav.sampleCount - sampleOffset) + samplesPerFrame - 1) / samplesPerFrame;
            var blckLen =  remainingFrames * bytesPerFrame  >= STRM_LENGTH ? STRM_LENGTH : remainingFrames * bytesPerFrame;
            while ((blckLen * 9) % 32 > 0)
                blckLen++;
            
            var samplesThisFrame = (blckLen / bytesPerFrame) * samplesPerFrame;
            //Console.WriteLine(samplesThisFrame);
            
            writer.Write(blckLen);
            for (int h = 0; h < 6; h++) {
                writer.Write((ushort)last[h]);
                writer.Write((ushort)penultimate[h]);
            }
            for (int channel = 0; channel < wav.channels; channel++)
            {
                var lastC = last[channel];
                var penultC = penultimate[channel];
                var sampleBuff = getPCMBufferChannel(wav, channel, sampleOffset, samplesThisFrame);               
                
                byte[] writeBuff = new byte[0];
                switch (format)
                {
                    case EncodeFormat.ADPCM4:
                        writeBuff = transform_pcm16_mono_adpcm(sampleBuff, samplesThisFrame, ref lastC, ref penultC);
                        break;
                    case EncodeFormat.PCM8:
                        writeBuff = transform_pcm16_pcm8(sampleBuff, samplesThisFrame, ref lastC, ref penultC);
                        break;
                    case EncodeFormat.PCM16:
                        writeBuff = PCM16ShortToByteBigEndian(sampleBuff);
                        break;
                }
                writer.Write(writeBuff);
                last[channel] = lastC;
                penultimate[channel] = penultC;
            }
            sampleOffset += samplesThisFrame;
            writer.Flush();
        }

        private static short[] getPCMBufferChannel(PCM16WAV wav, int channelNumber, int sampleOffset , int samples)
        {
            var ret = new short[samples];
            for (int i = 0; i < samples; i++)
                if (sampleOffset + i < wav.sampleCount)
                {
                    ret[i] = wav.buffer[wav.channels * (sampleOffset + i) + channelNumber];
                }
                else
                {
                    //Console.WriteLine("AUGH. sample buffer exceeded.");
                    ret[i] = 0;
                }
            return ret;            
        }

        public static byte[] transform_pcm16_mono_adpcm(short[] data, int sampleCount, ref int last, ref int penult)
        {
            //adpcm_data = new byte[((WaveData.sampleCount / 9) * 16)];


            int frameCount = (sampleCount + 16 - 1) / 16;

            var frameBufferSize = frameCount * 9; // and we know the amount of bytes that the buffer will take.
            var adjustedFrameBufferSize = frameBufferSize; //+ (frameBufferSize % 32); // pads buffer to 32 bytes. 
            byte[] adpcm_data = new byte[adjustedFrameBufferSize]; // 9 bytes per 16 samples 

            last = 0;
            penult = 0;
            int absolute = 0;
            int pcmLast = 0;
            //Console.WriteLine($"\n\n\n{WaveData.sampleCount} samples\n{frameCount} frames.\n{frameBufferSize} bytes\n{adjustedFrameBufferSize} padded bytes. ");
            var adp_f_pos = 0; // ADPCM position


            var wavFP = data;
            // transform one frame at a time
            for (int ix = 0; ix < frameCount; ix++)
            {
                short[] wavIn = new short[16];
                byte[] adpcmOut = new byte[9];
                for (int k = 0; k < 16; k++)
                {
                    if (((ix * 16) + k) >= sampleCount)
                        continue; // skip if we're out of samplebuffer, continue to build last frame
                    wavIn[k] = wavFP[(ix * 16) + k];
                }
                // build ADPCM frame
                bananapeel.Pcm16toAdpcm4HLE(wavIn, adpcmOut, ref last, ref penult); // convert PCM16 -> ADPCM4
                for (int k = 0; k < 9; k++)
                {
                    adpcm_data[adp_f_pos] = adpcmOut[k]; // dump into ADPCM buffer
                    adp_f_pos++; // increment ADPCM byte
                }
            }
            return adpcm_data;
        }




        public static byte[] transform_pcm16_pcm8(short[] data, int sampleCount, ref int last, ref int penult)
        {
            //adpcm_data = new byte[((WaveData.sampleCount / 9) * 16)];

            last = 0;
            penult = 0;

            int frameCount = sampleCount;
            // now that we have a properly calculated frame count, we know the amount of samples that realistically fit into that buffer. 
     
            byte[] pcm8Data = new byte[frameCount];

            // transform one frame at a time
            for (int ix = 0; ix < frameCount; ix++)
            {
                pcm8Data[ix] = (byte)((sbyte)(data[ix] >> 8));
            }
            return pcm8Data;
        }

    }
}
