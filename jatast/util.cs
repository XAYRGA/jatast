using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Be.IO;

namespace jatast
{
    public static class util
    {
        public static void consoleProgress(string txt, int progress, int max, bool show_progress = false)
        {
            var flt_total = (float)progress / max;
            Console.CursorLeft = 0;
            //Console.WriteLine(flt_total);
            Console.Write($"{txt} [");
            for (float i = 0; i < 32; i++)
                if (flt_total > (i / 32f))
                    Console.Write("#");
                else
                    Console.Write(" ");
            Console.Write("]");
            if (show_progress)
                Console.Write($" ({progress}/{max})");           
        }
        public static int padTo(BeBinaryWriter bw, int padding)
        {
            int del = 0; 
            while (bw.BaseStream.Position % 32 != 0)
            {
                bw.BaseStream.WriteByte(0x00);
                bw.BaseStream.Flush();
                del++;
            }
            return del;
        }

        public static int padToInt(int Addr, int padding)
        {
            var delta = (int)(Addr % padding);
            return (padding - delta);        
        }

        public static short[] getPCMBufferChannel(PCM16WAV wav, int channelNumber, int sampleOffset, int samples)
        {
            var ret = new short[samples];
            for (int i = 0; i < samples; i++)
                if (sampleOffset + i < wav.sampleCount)
                    ret[i] = wav.buffer[wav.channels * (sampleOffset + i) + channelNumber];
                else
                    ret[i] = 0;
            return ret;
        }

    }
}
