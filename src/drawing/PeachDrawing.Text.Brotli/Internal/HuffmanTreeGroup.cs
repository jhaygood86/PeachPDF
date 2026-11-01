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
	/// <summary>Contains a collection of huffman trees with the same alphabet size.</summary>
	internal sealed class HuffmanTreeGroup
	{
		/// <summary>The maximal alphabet size in this group.</summary>
		private int alphabetSize;

		/// <summary>Storage for Huffman lookup tables.</summary>
		internal int[] codes;

		/// <summary>
		/// Offsets of distinct lookup tables in
		/// <see cref="codes"/>
		/// storage.
		/// </summary>
		internal int[] trees;

		/// <summary>Initializes the Huffman tree group.</summary>
		/// <param name="group">POJO to be initialised</param>
		/// <param name="alphabetSize">the maximal alphabet size in this group</param>
		/// <param name="n">number of Huffman codes</param>
		internal static void Init(PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup group, int alphabetSize, int n)
		{
			group.alphabetSize = alphabetSize;
			group.codes = new int[n * PeachDrawing.Text.Brotli.Internal.Huffman.HuffmanMaxTableSize];
			group.trees = new int[n];
		}

		/// <summary>Decodes Huffman trees from input stream and constructs lookup tables.</summary>
		/// <param name="group">target POJO</param>
		/// <param name="br">data source</param>
		internal static void Decode(PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup group, PeachDrawing.Text.Brotli.Internal.BitReader br)
		{
			int next = 0;
			int n = group.trees.Length;
			for (int i = 0; i < n; i++)
			{
				group.trees[i] = next;
				PeachDrawing.Text.Brotli.Internal.Decode.ReadHuffmanCode(group.alphabetSize, group.codes, next, br);
				next += PeachDrawing.Text.Brotli.Internal.Huffman.HuffmanMaxTableSize;
			}
		}
	}
}
