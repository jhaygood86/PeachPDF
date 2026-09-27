/* Copyright 2015 Google Inc. All Rights Reserved.

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
	/// <summary>Transformations on dictionary words.</summary>
	internal sealed class Transform
	{
		private readonly byte[] prefix;

		private readonly int type;

		private readonly byte[] suffix;

		internal Transform(string prefix, int type, string suffix)
		{
			this.prefix = ReadUniBytes(prefix);
			this.type = type;
			this.suffix = ReadUniBytes(suffix);
		}

		internal static byte[] ReadUniBytes(string uniBytes)
		{
			byte[] result = new byte[uniBytes.Length];
			for (int i = 0; i < result.Length; ++i)
			{
				result[i] = unchecked((byte)uniBytes[i]);
			}
			return result;
		}

		internal static readonly PeachDrawing.Text.Brotli.Internal.Transform[] Transforms = new PeachDrawing.Text.Brotli.Internal.Transform[] { new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, 
			PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst1, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " the "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity
			, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform("s ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " of "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseFirst, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " and "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst2, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast1, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(", ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity
			, ", "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " in "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, " to "), new PeachDrawing.Text.Brotli.Internal.Transform("e ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "\""), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, 
			PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "."), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "\">"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "\n"), new 
			PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast3, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "]"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, " for "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst3, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast2, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " a "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " that "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst
			, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(".", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, ", "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst4, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " with "), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "'"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " from "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity
			, " by "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst5, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst6, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform
			(" the ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast4, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, ". The "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " on "), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " as "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " is "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast7
			, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast1, "ing "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "\n\t"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty
			, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ":"), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "ed "), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst9, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitFirst7, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.OmitLast6, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "("), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ", "), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast8, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " at "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, "ly "), new PeachDrawing.Text.Brotli.Internal.Transform(" the ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " of "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast5, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(
			string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.OmitLast9, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ", "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst
			, "\""), new PeachDrawing.Text.Brotli.Internal.Transform(".", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "("), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseFirst, "\">"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "."), new PeachDrawing.Text.Brotli.Internal.Transform(".com/", 
			PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(" the ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " of the "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst
			, "'"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ". This "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ","), new PeachDrawing.Text.Brotli.Internal.Transform(".", PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "("), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "."), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, " not "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "er "
			), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, " "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "al "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseAll, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "='"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "\""), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "("), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, 
			"ful "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "ive "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.Identity, "less "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "'"), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "est "), new PeachDrawing.Text.Brotli.Internal.Transform
			(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "."), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "\">"), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "='"
			), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ","), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, "ize "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseAll, "."), new PeachDrawing.Text.Brotli.Internal.Transform("\u00c2\u00a0", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, string.Empty), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity, ","), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty
			, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.Identity
			, "ous "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, ", "), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "='"), new PeachDrawing.Text.Brotli.Internal.Transform(" ", 
			PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, ","), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, ", "), new PeachDrawing.Text.Brotli.Internal.Transform
			(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, ","), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "("), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.
			UppercaseAll, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "."), new PeachDrawing.Text.Brotli.Internal.Transform(string.Empty, PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "='"), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseAll, ". "), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst, "=\""), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll, "='"), new PeachDrawing.Text.Brotli.Internal.Transform(" ", PeachDrawing.Text.Brotli.Internal.WordTransformType
			.UppercaseFirst, "='") };

		internal static int TransformDictionaryWord(byte[] dst, int dstOffset, byte[] word, int wordOffset, int len, PeachDrawing.Text.Brotli.Internal.Transform transform)
		{
			int offset = dstOffset;
			// Copy prefix.
			byte[] @string = transform.prefix;
			int tmp = @string.Length;
			int i = 0;
			// In most cases tmp < 10 -> no benefits from System.arrayCopy
			while (i < tmp)
			{
				dst[offset++] = @string[i++];
			}
			// Copy trimmed word.
			int op = transform.type;
			tmp = PeachDrawing.Text.Brotli.Internal.WordTransformType.GetOmitFirst(op);
			if (tmp > len)
			{
				tmp = len;
			}
			wordOffset += tmp;
			len -= tmp;
			len -= PeachDrawing.Text.Brotli.Internal.WordTransformType.GetOmitLast(op);
			i = len;
			while (i > 0)
			{
				dst[offset++] = word[wordOffset++];
				i--;
			}
			if (op == PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseAll || op == PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst)
			{
				int uppercaseOffset = offset - len;
				if (op == PeachDrawing.Text.Brotli.Internal.WordTransformType.UppercaseFirst)
				{
					len = 1;
				}
				while (len > 0)
				{
					tmp = dst[uppercaseOffset] & unchecked((int)(0xFF));
					if (tmp < unchecked((int)(0xc0)))
					{
						if (tmp >= 'a' && tmp <= 'z')
						{
							dst[uppercaseOffset] ^= unchecked((byte)32);
						}
						uppercaseOffset += 1;
						len -= 1;
					}
					else if (tmp < unchecked((int)(0xe0)))
					{
						dst[uppercaseOffset + 1] ^= unchecked((byte)32);
						uppercaseOffset += 2;
						len -= 2;
					}
					else
					{
						dst[uppercaseOffset + 2] ^= unchecked((byte)5);
						uppercaseOffset += 3;
						len -= 3;
					}
				}
			}
			// Copy suffix.
			@string = transform.suffix;
			tmp = @string.Length;
			i = 0;
			while (i < tmp)
			{
				dst[offset++] = @string[i++];
			}
			return offset - dstOffset;
		}
	}
}
