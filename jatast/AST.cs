using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;

namespace jatast
{

    public enum EncodeFormat
    {
        ADPCM4 = 0,
        PCM16 = 1,
        PCM8 = 2
    }


    internal class AST
    {
        private const int STRM_HEAD = 0x5354524D;
        private const int STRM_HEAD_SIZE = 0x40;
        private const int BLCK_HEAD = 0x424C434B;
        private const int BLCK_SIZE = 0x2760;
        private const int BLCK_MAX_CHANNELS = 6;

        public EncodeFormat format;
        public int LoopStart;
        public int LoopEnd;
        public int SampleRate;
        public int SampleCount;
        public bool Loop;

        public short BitsPerSample;
        public short ChannelCount;

        public List<short[]> Channels = new();

        public int BytesPerFrame;
        public int SamplesPerFrame;


        public unsafe static byte[] PCM16ShortToByteBigEndian(short[] pcm)
        {
            var pcmB = new byte[pcm.Length * 2];
            fixed (short* pcmD = pcm)
            {
                var pcmSh = (byte*)pcmD;
                //for (int i=0; i < pcm.Length;i++)
                for (int i = 0; i < pcmB.Length; i += 2)
                {
                    pcmB[i] = pcmSh[i + 1];
                    pcmB[i + 1] = pcmSh[i];
                }
            }
            return pcmB;
        }



        public static byte[] transform_pcm16_mono_adpcm(short[] data, int sampleCount, ref int last, ref int penult)
        {
            //adpcm_data = new byte[((WaveData.sampleCount / 9) * 16)];


            int frameCount = (sampleCount + 16 - 1) / 16;

            var frameBufferSize = frameCount * 9; // and we know the amount of bytes that the buffer will take.
            var adjustedFrameBufferSize = frameBufferSize; //+ (frameBufferSize % 32); // pads buffer to 32 bytes. 
            byte[] adpcm_data = new byte[adjustedFrameBufferSize]; // 9 bytes per 16 samples 

            //if (sampleCount / 16 != frameCount)
                //Console.WriteLine($"Frame rounded! {sampleCount / 16} {frameCount}");
            //Console.WriteLine($"\n\n\n{WaveData.sampleCount} samples\n{frameCount} frames.\n{frameBufferSize} bytes\n{adjustedFrameBufferSize} padded bytes. ");
            var adp_f_pos = 0; // ADPCM position


            var wavFP = data;
            // transform one frame at a time
            for (int ix = 0; ix < frameCount; ix++)
            {
                short[] wavIn = new short[16];
                byte[] adpcmOut = new byte[9];
                for (int k = 0; k < 16; k++)
                    wavIn[k] = wavFP[(ix * 16) + k];

                // build ADPCM frame
                bananapeel.Pcm16toAdpcm4(wavIn, adpcmOut, ref last, ref penult); // convert PCM16 -> ADPCM4
                for (int k = 0; k < 9; k++)
                {
                    adpcm_data[adp_f_pos] = adpcmOut[k]; // dump into ADPCM buffer
                    adp_f_pos++; // increment ADPCM byte
                }
            }
            return adpcm_data;
        }


        public void WriteToStream(BeBinaryWriter wrt)
        {

            switch (format)
            {
                case EncodeFormat.ADPCM4:
                    BytesPerFrame = 9;
                    SamplesPerFrame = 16;
                    break;
                case EncodeFormat.PCM8:
                    BytesPerFrame = 1;
                    SamplesPerFrame = 1;
                    break;
                case EncodeFormat.PCM16:
                    BytesPerFrame = 2;
                    SamplesPerFrame = 1;
                    break;
            }

            wrt.Write(STRM_HEAD);
            wrt.Write(0x00); // Temporary Length
            wrt.Write((ushort)format);
            wrt.Write((ushort)BitsPerSample);
            wrt.Write((ushort)ChannelCount);
            wrt.Write(Loop ? (ushort)0xFFFF : (ushort)0x0000);
            wrt.Write(SampleRate);
            wrt.Write(SampleCount);
            wrt.Write(LoopStart);
            wrt.Write(LoopEnd);
            wrt.Write(BLCK_SIZE);
            wrt.Write(0);
            wrt.Write(0x7F000000);
            wrt.Write(new byte[0x14]);

            var total_blocks = (((SampleCount / SamplesPerFrame) * BytesPerFrame) + BLCK_SIZE - 1) / BLCK_SIZE;

            last = new int[BLCK_MAX_CHANNELS]; // reset
            penult = new int[BLCK_MAX_CHANNELS]; // reset
            sampleOffset = 0; // reset

            for (int i = 0; i < total_blocks; i++)
            {
                WriteBlock(wrt);
                wrt.Flush();
            }
            var size = (int)wrt.BaseStream.Position;
            wrt.BaseStream.Position = 4;
            wrt.Write(size - STRM_HEAD_SIZE);
            wrt.Flush();
            wrt.Close();

        }
   

        private int[] last = new int[BLCK_MAX_CHANNELS];
        private int[] penult = new int[BLCK_MAX_CHANNELS];
        private int sampleOffset = 0; 

        private void WriteBlock(BeBinaryWriter wrt)
        {

            var totalFramesLeft = ((SampleCount - sampleOffset) + SamplesPerFrame - 1) / SamplesPerFrame;
            var thisBlockLength = (totalFramesLeft * BytesPerFrame) >= BLCK_SIZE ? BLCK_SIZE : totalFramesLeft * BytesPerFrame;
            var samplesThisFrame = (thisBlockLength / BytesPerFrame) * SamplesPerFrame;
           // thisBlockLength = thisBlockLength + (32 - (thisBlockLength % 32)); // Pad size to 32


            wrt.Write(BLCK_HEAD);
            wrt.Write(thisBlockLength);
            for (int i = 0; i < BLCK_MAX_CHANNELS; i++)
            {
                wrt.Write((short)last[i]);
                wrt.Write((short)penult[i]);                 
            }



            for (int i=0; i < Channels.Count; i++)
            {
                //Console.WriteLine($"{Channels.Count} {ChannelCount} {i}");
                var samples = sliceSampleArray(Channels[i], sampleOffset, samplesThisFrame);

                int last_Current = last[i];
                int penultimate_Current = penult[i];

                byte[] writeBuff = new byte[0];
                switch (format)
                {
                    case EncodeFormat.ADPCM4:
                       writeBuff = transform_pcm16_mono_adpcm(samples, samplesThisFrame, ref last_Current, ref penultimate_Current);
                        break;
                    case EncodeFormat.PCM8:
                      //  writeBuff = transform_pcm16_pcm8(sampleBuff, samplesThisFrame, ref lastC, ref penultC);
                        break;
                    case EncodeFormat.PCM16:
                        writeBuff = PCM16ShortToByteBigEndian(samples);
                        break;
                }
                wrt.Write(writeBuff);
            
                last[i] = (short)last_Current;
                penult[i] = (short)penultimate_Current;
            }

            sampleOffset += samplesThisFrame;
        }

        private short[] sliceSampleArray(short[] samples, int start, int sampleCount)
        {
            //Console.WriteLine(samples.Length);
            //Console.WriteLine(start);
            //Console.WriteLine(sampleCount);
                
            var ret = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                if (i < samples.Length)
                    ret[i] = samples[start + (i)];
                else
                    ret[i] = 0;
            return ret;
        }

    }
}
