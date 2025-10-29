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
                case "pcm8":
                    encFmt = EncodeFormat.PCM8;
                    break;
                case "adpcm4":
                    encFmt = EncodeFormat.ADPCM4;
                    break;
                case "def":
                    Console.WriteLine("Encoding using ADPCM4");
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
            var waveInput = File.OpenRead(inFile);
            var waveReader = new BinaryReader(waveInput);
            var astOutputFile = File.Open(outFile, FileMode.Create, FileAccess.ReadWrite);
            var astOutputWriter = new BeBinaryWriter(astOutputFile);
            var waveInputObject = PCM16WAV.readStream(waveReader);

            if (waveInputObject.bitsPerSample != 16)
                cmdarg.assert("WAV must be signed PCM 16 bit");

            var astEncoder = new AST();

            if (waveInputObject.sampleRate > 32000)
            {
                var w = Console.ForegroundColor;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"Warning: The Gamecube and Wii only play at 32000hz. Your wav is {waveInputObject.sampleRate}, which means that you are wasting space. Consider changing your samplerate on your WAV file to 32000hz or less.");
                Console.ForegroundColor = w;
            }


            astEncoder.ChannelCount = waveInputObject.channels;
            astEncoder.BitsPerSample = waveInputObject.bitsPerSample;
            astEncoder.BytesPerFrame = waveInputObject.byteRate;
            astEncoder.SampleCount = waveInputObject.sampleCount;
            astEncoder.SampleRate = waveInputObject.sampleRate;
            astEncoder.format = encFmt;



            for (int i = 0; i < waveInputObject.channels; i++)
                astEncoder.Channels.Add(util.getPCMBufferChannel(waveInputObject, i, 0, astEncoder.SampleCount));

            if (waveInputObject.sampler.loops != null)
            {
                astEncoder.Loop = true;
                astEncoder.LoopStart = (int)waveInputObject.sampler.loops[0].dwStart;
                astEncoder.LoopEnd = (int)waveInputObject.sampler.loops[0].dwEnd;
            }

            if (loopArg != "none")
            {
                var loopData = loopArg.Split(',');
                cmdarg.assert(loopData.Length >= 2, "Bad loop format, format is -loop start,end. Units are in samples, not seconds or otherwise.");
                var loopStartParsed = 0u;
                var loopEndParsed = 0u;
                cmdarg.assert(UInt32.TryParse(loopData[0], out loopStartParsed), $"Cannot parse '{loopData[0]}' as an integer.");
                cmdarg.assert(UInt32.TryParse(loopData[1], out loopEndParsed), $"Cannot parse '{loopData[1]}' as an integer.");
                cmdarg.assert(loopStartParsed < astEncoder.SampleCount, "Loop start is more samples than in the file.");
                cmdarg.assert(loopEndParsed <= astEncoder.SampleCount, "Loop end is more samples than in the file.");
                cmdarg.assert(loopStartParsed < loopEndParsed, "Loop start is greater than loop end.");
                astEncoder.Loop = true;
                astEncoder.LoopStart = (int)loopStartParsed;
                astEncoder.LoopEnd = (int)loopEndParsed;
            }
            astEncoder.WriteToStream(astOutputWriter);
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
                Console.WriteLine($"That wasn't supposed to happen.\n\nA short description of the error: '{E.Message}'\n\nThings you can try:\n1. Checking your WAV file, verify that it is in the correct format\n2. Make sure the folder you're saving to is writable.\n3. If all else fails, submitting an issue request on github.");
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
            Console.WriteLine("\t -loop <startSample,EndSample> - Samples are in samples, not seconds or otherwise.");
            Console.WriteLine("\t -encode-fromat <format> - Sets the encoding format for the AST, options are:");
            Console.WriteLine("\t\t * adpcm4 [9 bytes/16 samples] (default)  -- The default codec for the gamecube. Space efficient, but lossy quality. ");
            Console.WriteLine("\t\t * pcm16 [32 bytes/16 samples] (not recommended) -- Optional high quality codec. This will use a lot of I/O bandwidth and will increase load times if you're streaming while loading. Only use this if you know what you're doing.");
            Console.WriteLine("\n\n");
            Console.WriteLine("My game takes a long time to load while music is playing:");
            Console.WriteLine("\t*Lower your samplerate, and make sure you're using adpcm4. The gamecube and wii do not load the entire song at once, so it will constantly be using the disk to play music.");

            Console.WriteLine("Note: if your WAV file has loop points, they will be automatically imported. ");
        }
    }
}
