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
        static void Main(string[] args)
        {
       
            cmdarg.cmdargs = args; 
            //Console.WriteLine(generateDeviceToken());
            //Console.WriteLine(Guid.NewGuid().ToString());
            Console.WriteLine("JATAST -- JAudio Toolkit AST creator");
            EncodeFormat encFmt = EncodeFormat.PCM16;
            var encodingArg = cmdarg.findDynamicStringArgument("-encode-format", "def");
            var loopArg = cmdarg.findDynamicStringArgument("-loop", "none");

            var inFile = cmdarg.assertArg(0, "Input File");
            if (inFile=="help")
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
                    Console.WriteLine("WARNING: ADPCM4 has artifacts or clicks in it. It's still experimental.");
                    encFmt = EncodeFormat.ADPCM4;
                    break;
                case "pcm8":
                    Console.WriteLine("WARNING: PCM8 may not be supported in most games.");
                    encFmt = EncodeFormat.PCM8;
                    break;
                case "def":
                    Console.WriteLine("No encoding format specified, using PCM16");
                    break;
                default:
                    Console.WriteLine($"Invalid encoding format '{encodingArg}' available formats are:\npcm16, adpcm4");
                    break;
            }

            cmdarg.assert(!File.Exists(inFile), $"Cannot locate file '{inFile}'");
            
            try
            {

                

                var wI = File.OpenRead(inFile);
                var wIR = new BinaryReader(wI);
                var wO = File.OpenWrite(outFile);
                var wav = PCM16WAV.readStream(wIR);
                var enc = new encoder(wav, encFmt, wO);
                if (loopArg != "none")
                {
                    var loopData = loopArg.Split(',');
                    cmdarg.assert(loopData.Length < 2, "Bad loop format, format is -loop start,end");
                    var lS = 0u;
                    var lE = 0u;
                    cmdarg.assert(!UInt32.TryParse(loopData[0], out lS), $"Cannot parse '{loopData[0]}' as an integer.");
                    cmdarg.assert(!UInt32.TryParse(loopData[1], out lE), $"Cannot parse '{loopData[1]}' as an integer.");
                    cmdarg.assert(lS > lE, "Loop start is greater than loop end.");
                    enc.loopStart = lS;
                    enc.loopEnd = lE;
                }
                enc.encode();

            } catch (Exception E)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Yikes!");
                Console.WriteLine(E.ToString());
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine();
                Console.WriteLine($"That wasn't supposed to happen.\n\nA short description of the error: '{E.Message}'\n\nThings you can try:\n1. Checking your WAV file\n2. Check to make sure I can read and write the file.\n3. Crying a lot.\n\nIf you've tried all of these things, please put your tears and the above red text in a jar, and send it to the developer.");
            }

  
        }

        public static void showHelp()
        {
            Console.WriteLine("by Xayrga");
            Console.WriteLine("Syntax:");
            Console.WriteLine();
            Console.WriteLine("jatast.exe <input file> <output file> [--encode-format (pcm16,adpcm4)] [--loop startSample,endSample]");
            Console.WriteLine("Note: if your WAV file has loop points, they will be automatically imported.");
        }
    }
}
