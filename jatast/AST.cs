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
        private const int BLCK_HEAD_SIZE = 0x20; 
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

        public int BytesPerFrame;
        public int SamplesPerFrame;

        public List<short[]> Channels = new();




        private int[] last = new int[BLCK_MAX_CHANNELS];
        private int[] penult = new int[BLCK_MAX_CHANNELS];
        private int sampleOffset = 0;

        private int[] loop_penult = new int[BLCK_MAX_CHANNELS];
        private int[] loop_last = new int[BLCK_MAX_CHANNELS];



        public static byte[] PCM16ShortToByteBigEndian(short[] pcm)
        {
            var pcmB = new byte[pcm.Length * 2];
            // For some reason this is faster than pinning in memory?
            for (int i = 0; i < pcmB.Length; i += 2)
            {
                var ci = pcm[i / 2];
                pcmB[i] = (byte)(ci >> 8);
                pcmB[i + 1] = (byte)(ci & 0xFF);
            }
            return pcmB;
        }

        private int getSampleOffset(int sample, byte channel = 1, int lastBlockSize = BLCK_SIZE)
        {
            var offset = STRM_HEAD_SIZE;
            var samplesPerBlock = (BLCK_SIZE / BytesPerFrame) * SamplesPerFrame;
            var blockNumber =  sample / samplesPerBlock;

            // Seek the size of every block that was skipped
            offset += BLCK_HEAD_SIZE * (blockNumber + 1) + (blockNumber * BLCK_SIZE * ChannelCount) + ((channel - 1) * lastBlockSize);

            // Finally, find the offset of the frame that this sample sits in.
            offset += ( sample - 1 - (blockNumber*samplesPerBlock)) / SamplesPerFrame * BytesPerFrame;
            return offset;                
        }

        private byte[] EncodeADPCM4Block(short[] samples, int sampleCount, ref int last, ref int penult, int channel)
        {

            int frameCount = (sampleCount + 16 - 1) / 16; // Roundup samples to 16 or else we'll truncate frames.
            int frameBufferSize = frameCount * 9; 
            byte[] adpcm_data = new byte[frameBufferSize]; 
            int adpcmBufferPosition = 0;

            // Previous frame's L / P 
            var lastLast = last;
            var lastPenult = penult;
            // transform one frame at a time
            for (int ix = 0; ix < frameCount; ix++)
            {


                short[] wavIn = new short[16];
                byte[] adpcmOut = new byte[9];

                // Extract samples 16 at a time, 1 frame = 16 samples / 9 bytes. 
                for (int k = 0; k < 16; k++)
                    wavIn[k] = samples[(ix * 16) + k];

                if (Loop && ((sampleOffset + (ix * 16)) == LoopStart))
                {
                    
                    loop_last[channel] = last;
                    loop_penult[channel] = penult;
                    Console.WriteLine($"\nstore loop predictor values N-1 {ix} ({loop_last[channel]}) N-2({loop_penult[channel]}) Chn:{channel} Smpl:{sampleOffset + (ix * 16)}");
                }


                lastLast = last;
                lastPenult = penult;

                bananapeel.PCM16TOADPCM4(wavIn, adpcmOut, ref last, ref penult); // convert PCM16 -> ADPCM4


                // Hack for looping 
              

                for (int k = 0; k < 9; k++)
                {
                    adpcm_data[adpcmBufferPosition] = adpcmOut[k]; // dump into ADPCM buffer
                    adpcmBufferPosition++;
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
                    //if (LoopEnd % 16 != 0) // Can't increase. 
                    //    Console.WriteLine($"ADPCM ENC WARN: End loop {LoopEnd} is not divisible by 16, corrected to {LoopEnd=LoopEnd - (LoopEnd % 16)} ");
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

            // Tell encoder to clamp boundary, nothing plays after the loop.
            if (Loop && (SampleCount > LoopEnd))
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

            Console.WriteLine($"Loop point sits at 0x{getSampleOffset(LoopStart,(byte)ChannelCount):X}");

            var total_blocks = (((SampleCount / SamplesPerFrame) * BytesPerFrame) + BLCK_SIZE - 1) / BLCK_SIZE;

            // sample history storage for blocks
            last = new int[BLCK_MAX_CHANNELS]; // reset
            penult = new int[BLCK_MAX_CHANNELS]; // reset

            sampleOffset = 0; // reset


            Console.WriteLine("Processing BLCK's");
            for (int i = 0; i < total_blocks; i++)
            {
                WriteBlock(wrt, i+1==total_blocks);
                wrt.Flush();
            }
            Console.WriteLine();

            // Save the size and flush it into 0x04 of the header.
            var size = (int)wrt.BaseStream.Position;
            wrt.BaseStream.Position = 4;
            wrt.Write(size - STRM_HEAD_SIZE);

            wrt.Flush();
            wrt.Close();

       
        }
   


        private int WriteBlock(BeBinaryWriter wrt, bool lastBlock = false)
        {

            var totalFramesLeft = ((SampleCount - sampleOffset) + SamplesPerFrame - 1) / SamplesPerFrame;
            var thisBlockLength = (totalFramesLeft * BytesPerFrame) >= BLCK_SIZE ? BLCK_SIZE : totalFramesLeft * BytesPerFrame;
            var samplesThisFrame = (thisBlockLength / BytesPerFrame) * SamplesPerFrame;
            var paddingSize = 32 - (thisBlockLength % 32);
            if (paddingSize == 32) // Was zero, we're already aligned.
                paddingSize = 0;

            Console.Write(".");

            if (paddingSize > 0)
                Console.WriteLine($"\nLAST BLCK Add size {paddingSize} bytes {paddingSize/BytesPerFrame} frames {(paddingSize / BytesPerFrame) * SamplesPerFrame} samples");
     

            wrt.Write(BLCK_HEAD);
            wrt.Write(thisBlockLength + paddingSize);


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
                       writeBuff = EncodeADPCM4Block(samples, samplesThisFrame, ref last_Current, ref penultimate_Current,i);
                        break;
                    case EncodeFormat.PCM16:
                        writeBuff = PCM16ShortToByteBigEndian(samples);
                        break;
                }
                wrt.Write(writeBuff);
            
                last[i] = (short)last_Current;
                penult[i] = (short)penultimate_Current;

                if (paddingSize > 0)
                    wrt.Write(new byte[paddingSize]);
            }

            var oldPos = wrt.BaseStream.Position;
            wrt.BaseStream.Position = leadoutSampleSavePosition;

            // Now that the block has been rendered, push the predictor values into the file.
            if (lastBlock && Loop) 
                for (int i = 0; i < BLCK_MAX_CHANNELS; i++)
                {
                    wrt.Write((short)loop_last[i]);
                    wrt.Write((short)loop_penult[i]);
                }
            else
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
