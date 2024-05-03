using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;



namespace jatast
{
	public static partial class bananapeel
	{
		public static float EncoderGain = 1f;
		static short[,] sAdpcmCoefficents = new short[16, 2] {
			{ 0, 0, }, { 2048, 0, }, { 0, 2048, }, { 1024, 1024, },
			{ 4096, -2048, }, { 3584, -1536, }, { 3072, -1024, }, { 4608, -2560, },
			{ 4200, -2248, }, { 4800, -2300, }, { 5120, -3072, }, { 2048, -2048, },
			{ 1024, -1024, }, { -1024, 1024, }, { -1024, 0, }, { -2048, 0, },
		};

		private static void message(string message, params object[] data)
        {
			var w = Console.ForegroundColor;
			Console.ForegroundColor = ConsoleColor.Yellow;
			Console.Write("bananapeel.adpcm# ");
			Console.ForegroundColor = w;
			Console.WriteLine(message, data);
        }

		public static int decodeADPCMSample(int nib, int coefIndex, int scale, int last, int penult)
		{
			return (nib << scale) + ((sAdpcmCoefficents[coefIndex, 0] * last) + (sAdpcmCoefficents[coefIndex, 1] * penult) >> 11);
		}

		public static bool isInvalidSigned16(int number)
		{
			return number > 32767 || number < -32768;
		}


		public static int PCM16TOADPCM4(short[] pcm16, byte[] adpcm4, ref int last, ref int penult, int force_coefficient = -1)
		{
			var pcmDivisor = 1;
		retrySolveFrame:
			var bestCoefficientIndex = -1;
			var bestScale = -1;
			var current_error = 0;
			var best_error = Int32.MaxValue;
			var total_error = 0;
			var forceCoefOn = (force_coefficient > -1);

			for (int coefIndex = 0; coefIndex < 16; coefIndex++)
			{
				if (forceCoefOn)
					coefIndex = force_coefficient;

				for (int scale = 0; scale < 16; scale++)
				{		
					current_error = 0;
					byte num_ok_frames = 0;
					var copyLast = last;
					var copyPenult = penult;
					for (int sampleIndex = 0; sampleIndex < pcm16.Length; sampleIndex++)
					{
						var pcmSample = pcm16[sampleIndex] / pcmDivisor;
						var differential = (pcmSample - copyLast);
						var diff2 = (int)Math.Round((float)differential / (1 << scale)); // Calculate floating point component						

						if (diff2 > 7 || diff2 < -8)
							break; // this scale is too fat.

						var sampleDecoded = decodeADPCMSample(diff2, coefIndex, scale, copyLast, copyPenult);
						if (isInvalidSigned16(sampleDecoded))
							break; // scale clipped

						var error = Math.Abs(pcmSample - sampleDecoded);
						current_error += error;
						num_ok_frames++;
						copyPenult = copyLast;
						copyLast = sampleDecoded;
					}

					if (num_ok_frames == pcm16.Length && current_error < best_error)
					{
						bestCoefficientIndex = coefIndex;
						bestScale = scale;
						best_error = current_error;
					}
				}

				if (forceCoefOn)
					break; // exit loop, we already have the best coef C:
			}

			if (bestCoefficientIndex < 0 || bestScale < 0)
			{
				pcmDivisor++;
				if (pcmDivisor > 10)
					throw new Exception("Failed to solve for frame coefficient d=10");
				message($"failed to solve coefficient\ntrying again d={pcmDivisor}");
				goto retrySolveFrame;
			}


			var nibbles = new int[16];
			if (force_coefficient > -1)
				bestCoefficientIndex = force_coefficient;

			for (int i = 0; i < pcm16.Length; i++)
			{
				var differential = ((pcm16[i] / pcmDivisor) - last);
				var differentialSampleScaled = (int)Math.Round((float)differential / (1 << bestScale));
				nibbles[i] = differentialSampleScaled;
				var sampleDecoded = decodeADPCMSample(differentialSampleScaled, bestCoefficientIndex, bestScale, last, penult);
				penult = last;
				last = sampleDecoded;
				total_error += Math.Abs(pcm16[i] - sampleDecoded);
			}

			adpcm4[0] = (byte)((bestScale << 4) | bestCoefficientIndex);
			for (var i = 0; i < 8; ++i)
				adpcm4[1 + i] = (byte)(((nibbles[i * 2] << 4) & 0xF0) | (nibbles[i * 2 + 1] & 0xF));
			return total_error;
		}
	}
}