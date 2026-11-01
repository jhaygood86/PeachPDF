/* Copyright 2017 Google Inc. All Rights Reserved.

Distributed under MIT license.
See file LICENSE for detail or copy at https://opensource.org/licenses/MIT
*/

/* Ported to PeachDrawing.Text.Brotli (C#) from google/brotli's own C# decoder port at
 * csharp/org/brotli/dec/, retrieved 2026-09-27 from the master branch
 * (https://github.com/google/brotli/tree/master/csharp/org/brotli/dec). Namespace renamed
 * from Org.Brotli.Dec to PeachDrawing.Text.Brotli.Internal and #nullable disabled; the
 * decoding logic is unmodified except for two bug fixes in BrotliInputStream.cs (see PORTING-NOTES.md).
 */
#nullable disable
namespace PeachDrawing.Text.Brotli.Internal
{
	/// <summary>Byte-to-int conversion magic.</summary>
	internal sealed class IntReader
	{
		private byte[] byteBuffer;

		private int[] intBuffer;

		internal static void Init(PeachDrawing.Text.Brotli.Internal.IntReader ir, byte[] byteBuffer, int[] intBuffer)
		{
			ir.byteBuffer = byteBuffer;
			ir.intBuffer = intBuffer;
		}

		/// <summary>Translates bytes to ints.</summary>
		/// <remarks>
		/// Translates bytes to ints.
		/// NB: intLen == 4 * byteSize!
		/// NB: intLen should be less or equal to intBuffer length.
		/// </remarks>
		internal static void Convert(PeachDrawing.Text.Brotli.Internal.IntReader ir, int intLen)
		{
			for (int i = 0; i < intLen; ++i)
			{
				ir.intBuffer[i] = ((ir.byteBuffer[i * 4] & unchecked((int)(0xFF)))) | ((ir.byteBuffer[(i * 4) + 1] & unchecked((int)(0xFF))) << 8) | ((ir.byteBuffer[(i * 4) + 2] & unchecked((int)(0xFF))) << 16) | ((ir.byteBuffer[(i * 4) + 3] & unchecked((int
					)(0xFF))) << 24);
			}
		}
	}
}
