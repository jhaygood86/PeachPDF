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
	internal sealed class State
	{
		internal int runningState = PeachDrawing.Text.Brotli.Internal.RunningState.Uninitialized;

		internal int nextRunningState;

		internal readonly PeachDrawing.Text.Brotli.Internal.BitReader br = new PeachDrawing.Text.Brotli.Internal.BitReader();

		internal byte[] ringBuffer;

		internal readonly int[] blockTypeTrees = new int[3 * PeachDrawing.Text.Brotli.Internal.Huffman.HuffmanMaxTableSize];

		internal readonly int[] blockLenTrees = new int[3 * PeachDrawing.Text.Brotli.Internal.Huffman.HuffmanMaxTableSize];

		internal int metaBlockLength;

		internal bool inputEnd;

		internal bool isUncompressed;

		internal bool isMetadata;

		internal readonly PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup hGroup0 = new PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup();

		internal readonly PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup hGroup1 = new PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup();

		internal readonly PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup hGroup2 = new PeachDrawing.Text.Brotli.Internal.HuffmanTreeGroup();

		internal readonly int[] blockLength = new int[3];

		internal readonly int[] numBlockTypes = new int[3];

		internal readonly int[] blockTypeRb = new int[6];

		internal readonly int[] distRb = new int[] { 16, 15, 11, 4 };

		internal int pos = 0;

		internal int maxDistance = 0;

		internal int distRbIdx = 0;

		internal bool trivialLiteralContext = false;

		internal int literalTreeIndex = 0;

		internal int literalTree;

		internal int j;

		internal int insertLength;

		internal byte[] contextModes;

		internal byte[] contextMap;

		internal int contextMapSlice;

		internal int distContextMapSlice;

		internal int contextLookupOffset1;

		internal int contextLookupOffset2;

		internal int treeCommandOffset;

		internal int distanceCode;

		internal byte[] distContextMap;

		internal int numDirectDistanceCodes;

		internal int distancePostfixMask;

		internal int distancePostfixBits;

		internal int distance;

		internal int copyLength;

		internal int copyDst;

		internal int maxBackwardDistance;

		internal int maxRingBufferSize;

		internal int ringBufferSize = 0;

		internal long expectedTotalSize = 0;

		internal byte[] customDictionary = new byte[0];

		internal int bytesToIgnore = 0;

		internal int outputOffset;

		internal int outputLength;

		internal int outputUsed;

		internal int bytesWritten;

		internal int bytesToWrite;

		internal byte[] output;

		// Current meta-block header information.
		// TODO: Update to current spec.
		private static int DecodeWindowBits(PeachDrawing.Text.Brotli.Internal.BitReader br)
		{
			if (PeachDrawing.Text.Brotli.Internal.BitReader.ReadBits(br, 1) == 0)
			{
				return 16;
			}
			int n = PeachDrawing.Text.Brotli.Internal.BitReader.ReadBits(br, 3);
			if (n != 0)
			{
				return 17 + n;
			}
			n = PeachDrawing.Text.Brotli.Internal.BitReader.ReadBits(br, 3);
			if (n != 0)
			{
				return 8 + n;
			}
			return 17;
		}

		/// <summary>Associate input with decoder state.</summary>
		/// <param name="state">uninitialized state without associated input</param>
		/// <param name="input">compressed data source</param>
		internal static void SetInput(PeachDrawing.Text.Brotli.Internal.State state, System.IO.Stream input)
		{
			if (state.runningState != PeachDrawing.Text.Brotli.Internal.RunningState.Uninitialized)
			{
				throw new System.InvalidOperationException("State MUST be uninitialized");
			}
			PeachDrawing.Text.Brotli.Internal.BitReader.Init(state.br, input);
			int windowBits = DecodeWindowBits(state.br);
			if (windowBits == 9)
			{
				/* Reserved case for future expansion. */
				throw new PeachDrawing.Text.Brotli.Internal.BrotliRuntimeException("Invalid 'windowBits' code");
			}
			state.maxRingBufferSize = 1 << windowBits;
			state.maxBackwardDistance = state.maxRingBufferSize - 16;
			state.runningState = PeachDrawing.Text.Brotli.Internal.RunningState.BlockStart;
		}

		/// <exception cref="System.IO.IOException"/>
		internal static void Close(PeachDrawing.Text.Brotli.Internal.State state)
		{
			if (state.runningState == PeachDrawing.Text.Brotli.Internal.RunningState.Uninitialized)
			{
				throw new System.InvalidOperationException("State MUST be initialized");
			}
			if (state.runningState == PeachDrawing.Text.Brotli.Internal.RunningState.Closed)
			{
				return;
			}
			state.runningState = PeachDrawing.Text.Brotli.Internal.RunningState.Closed;
			PeachDrawing.Text.Brotli.Internal.BitReader.Close(state.br);
		}
	}
}
