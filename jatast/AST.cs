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

            int frameCount = (sampleCount + 16 - 1) / 16;

            var frameBufferSize = frameCount * 9; // and we know the amount of bytes that the buffer will take.
            var adjustedFrameBufferSize = frameBufferSize; //+ (frameBufferSize % 32); // pads buffer to 32 bytes. 

            byte[] adpcm_data = new byte[adjustedFrameBufferSize]; // 9 bytes per 16 samples 

            var adp_f_pos = 0; 


            var wavFP = data;
            // transform one frame at a time
            for (int ix = 0; ix < frameCount; ix++)
            {
                short[] wavIn = new short[16];
                byte[] adpcmOut = new byte[9];
                for (int k = 0; k < 16; k++)
                    wavIn[k] = wavFP[(ix * 16) + k];

                // build ADPCM frame
                bananapeel.PCM162ADPCM4LLE(wavIn, adpcmOut, ref last, ref penult); // convert PCM16 -> ADPCM4

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
                    if (LoopStart % 16 != 0)
                        Console.WriteLine($"ADPCM ENC WARN: Start loop {LoopStart} is not divisible by 16, corrected to { LoopStart += (16 - (LoopStart % 16))} ");
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


            if (Loop && SampleCount < LoopEnd)
                SampleCount = LoopEnd;
                

            wrt.Write(STRM_HEAD);
            wrt.Write(0x00); // Temporary Length
            wrt.Write((ushort)format);
            wrt.Write((ushort)BitsPerSample);
            wrt.Write((ushort)ChannelCount);
            wrt.Write(Loop ? (ushort)0xFFFF : (ushort)0x0000);
            wrt.Write(SampleRate);
            wrt.Write(SampleCount);
            wrt.Write(LoopStart);
            wrt.Write(Loop ? LoopEnd : SampleCount); // ty zyphro <3 
            wrt.Write(BLCK_SIZE);
            wrt.Write(0);
            wrt.Write(0x7F000000);
            wrt.Write(new byte[0x14]);

            var total_blocks = (((SampleCount / SamplesPerFrame) * BytesPerFrame) + BLCK_SIZE - 1) / BLCK_SIZE;

            last = new int[BLCK_MAX_CHANNELS]; // reset
            penult = new int[BLCK_MAX_CHANNELS]; // reset
            sampleOffset = 0; // reset


            var sampleCorrection = 0;
            for (int i = 0; i < total_blocks; i++)
            {
                sampleCorrection += WriteBlock(wrt);
                wrt.Flush();
            }


            var size = (int)wrt.BaseStream.Position;


           // util.padTo(wrt, 0x20); // ow my ass 
            wrt.BaseStream.Position = 4;
            wrt.Write(size - STRM_HEAD_SIZE);

            if (sampleCorrection > 0)
            {
                wrt.BaseStream.Position = 0x14;
                wrt.Write(SampleCount + sampleCorrection);
            }


            wrt.Flush();
            wrt.Close();

        }
   

        private int[] last = new int[BLCK_MAX_CHANNELS];
        private int[] penult = new int[BLCK_MAX_CHANNELS];
        private int sampleOffset = 0; 

        private int WriteBlock(BeBinaryWriter wrt)
        {

            var totalFramesLeft = ((SampleCount - sampleOffset) + SamplesPerFrame - 1) / SamplesPerFrame;
            var thisBlockLength = (totalFramesLeft * BytesPerFrame) >= BLCK_SIZE ? BLCK_SIZE : totalFramesLeft * BytesPerFrame;
            var samplesThisFrame = (thisBlockLength / BytesPerFrame) * SamplesPerFrame;

            var addSize = thisBlockLength;
            while (((addSize % 32) + (addSize % BytesPerFrame)) != 0)
                addSize++;

            var extraBytes = addSize - thisBlockLength;
            if (extraBytes > 0 )
                Console.WriteLine($"BLCK Add size {addSize - thisBlockLength} bytes {extraBytes/BytesPerFrame} frames {(extraBytes / BytesPerFrame) * SamplesPerFrame} samples");
       

            wrt.Write(BLCK_HEAD);
            wrt.Write(thisBlockLength + extraBytes);


            var leadoutSampleSavePosition = wrt.BaseStream.Position;
            wrt.Write(new byte[4 * BLCK_MAX_CHANNELS]); 


            for (int i=0; i < Channels.Count; i++)
            {

                var samples = sliceSampleArray(Channels[i], sampleOffset, samplesThisFrame);

                int last_Current = last[i];
                int penultimate_Current = penult[i];

                byte[] writeBuff = new byte[0];
                switch (format)
                {
                    case EncodeFormat.ADPCM4:
                       writeBuff = transform_pcm16_mono_adpcm(samples, samplesThisFrame, ref last_Current, ref penultimate_Current);
                        break;
                    case EncodeFormat.PCM16:
                        writeBuff = PCM16ShortToByteBigEndian(samples);
                        break;
                }
                wrt.Write(writeBuff);
            
                last[i] = (short)last_Current;
                penult[i] = (short)penultimate_Current;

                if (extraBytes > 0)
                    wrt.Write(new byte[extraBytes]);

            }

            var oldPos = wrt.BaseStream.Position;

            wrt.BaseStream.Position = leadoutSampleSavePosition;
            for (int i = 0; i < BLCK_MAX_CHANNELS; i++)
            {
                wrt.Write((short)(last[i]));
                wrt.Write((short)(penult[i]));
            }
            wrt.BaseStream.Position = oldPos;

            sampleOffset += samplesThisFrame;

            return 0;
        }

        private short[] sliceSampleArray(short[] samples, int start, int sampleCount)
        {

            var ret = new short[sampleCount];
            for (int i = 0; i < sampleCount; i++)
                if ( (i +start) < samples.Length )
                    ret[i] = samples[start + (i)];
                else
                    ret[i] = 0;
            return ret;
        }

    }
}
