using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using Be.IO;

namespace jatast
{
    class jatast_entrypoint
    {
        public static StringBuilder DebugOutput = new StringBuilder();

        static void Main(string[] args)
        {
            Console.WriteLine("JATAST -- JAudio Toolkit AST creator");
#if DEBUG
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Console.WriteLine("!JATAST build in debug mode, do not push into release!");
            Console.WriteLine("!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!!");
            Console.ForegroundColor = ConsoleColor.Gray;
            
            args = new string[]
            {
                "celeste_loop.wav",
                @"cac.ast",
      
            };
            
#endif
            var taskTimer = new System.Diagnostics.Stopwatch();
            cmdarg.cmdargs = args;

            EncodeFormat encFmt = EncodeFormat.ADPCM4;
            var encodingArg = cmdarg.findDynamicStringArgument("-encode-format", "def");
            var loopArg = cmdarg.findDynamicStringArgument("-loop", "none");

            var inFile = cmdarg.assertArg(0, "Input File");
            if (inFile == "help")
            {
                showHelp();
                return;
            }

            var oft = Path.GetDirectoryName(inFile) + "/" + Path.GetFileNameWithoutExtension(inFile) + ".ast";
            var outFile = cmdarg.tryArg(1, $"Output file, assuming {oft}");
            if (outFile == null)
                outFile = oft;

            switch (encodingArg)
            {
                case "pcm16":
                    encFmt = EncodeFormat.PCM16;
                    break;
                case "adpcm4":
                    Console.WriteLine("Encoding in ADPCM4 -- Report any bugs to the github page!");
                    encFmt = EncodeFormat.ADPCM4;
                    break;

                case "def":
                    Console.WriteLine("No encoding format specified, using ADPCM4");
                    break;
                default:
                    Console.WriteLine($"Invalid encoding format '{encodingArg}' available formats are:\npcm16, adpcm4");
                    break;
            }

            cmdarg.assert(File.Exists(inFile), $"Cannot locate file '{inFile}'");
#if RELEASE
            try
            {
#endif
            taskTimer.Start();
            var wI = File.OpenRead(inFile);
            var wIR = new BinaryReader(wI);
            var wO = File.Open(outFile, FileMode.Create, FileAccess.ReadWrite);
            var wrt = new BeBinaryWriter(wO);
            var wav = PCM16WAV.readStream(wIR);

            if (wav.bitsPerSample != 16)
                cmdarg.assert("WAV must be signed PCM 16 bit");

            var enc = new AST();

            if (wav.sampleRate > 32000)
            {
                var w = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("DSP Samplerate is only 32khz. Consider lowering your samplerate.");
                Console.ForegroundColor = w;
            }


            enc.ChannelCount = wav.channels;
            enc.BitsPerSample = wav.bitsPerSample;
            enc.BytesPerFrame = wav.byteRate;
            enc.SampleCount = wav.sampleCount;
            enc.SampleRate = wav.sampleRate;
            enc.format = encFmt;



            for (int i = 0; i < wav.channels; i++)
                enc.Channels.Add(util.getPCMBufferChannel(wav, i, 0, enc.SampleCount));

            if (wav.sampler.loops != null)
            {
                enc.Loop = true;
                enc.LoopStart = (int)wav.sampler.loops[0].dwStart;
                enc.LoopEnd = (int)wav.sampler.loops[0].dwEnd;
            }

            if (loopArg != "none")
            {
                var loopData = loopArg.Split(',');
                cmdarg.assert(loopData.Length >= 2, "Bad loop format, format is -loop start,end");
                var lS = 0u;
                var lE = 0u;
                cmdarg.assert(UInt32.TryParse(loopData[0], out lS), $"Cannot parse '{loopData[0]}' as an integer.");
                cmdarg.assert(UInt32.TryParse(loopData[1], out lE), $"Cannot parse '{loopData[1]}' as an integer.");
                cmdarg.assert(lS < enc.SampleCount, "Loop start is more samples than in the file.");
                cmdarg.assert(lE <= enc.SampleCount, "Loop end is more samples than in the file.");
                cmdarg.assert(lS < lE, "Loop start is greater than loop end.");
                enc.Loop = true;
                enc.LoopStart = (int)lS;
                enc.LoopEnd = (int)lE;
            }
            enc.WriteToStream(wrt);
            taskTimer.Stop();

#if RELEASE
            }
            catch (Exception E)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Yikes!");
                Console.WriteLine(E.ToString());
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine();
                Console.WriteLine($"That wasn't supposed to happen.\n\nA short description of the error: '{E.Message}'\n\nThings you can try:\n1. Checking your WAV file\n2. Check to make sure I can read and write the file.\n3. Crying a lot.\n\nIf you've tried all of these things, please put your tears and the above red text in a jar, and send it to the developer.");
            }
#else
            Console.WriteLine($"Task complete in {taskTimer.Elapsed.TotalSeconds}s");
#endif

        }

        public static void showHelp()
        {
            Console.WriteLine("by Xayrga");
            Console.WriteLine("Syntax:");
            Console.WriteLine();
            Console.WriteLine("jatast.exe <input file> <output file> [-encode-format (pcm16,adpcm4) = adpcm4] [-loop startSample,endSample] [-bananapeel.gain = 1.0]");

            Console.WriteLine("Note: if your WAV file has loop points, they will be automatically imported.");
        }
    }
}
