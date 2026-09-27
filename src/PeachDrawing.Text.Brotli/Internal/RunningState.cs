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
	/// <summary>Enumeration of decoding state-machine.</summary>
	internal sealed class RunningState
	{
		internal const int Uninitialized = 0;

		internal const int BlockStart = 1;

		internal const int CompressedBlockStart = 2;

		internal const int MainLoop = 3;

		internal const int ReadMetadata = 4;

		internal const int CopyUncompressed = 5;

		internal const int InsertLoop = 6;

		internal const int CopyLoop = 7;

		internal const int CopyWrapBuffer = 8;

		internal const int Transform = 9;

		internal const int Finished = 10;

		internal const int Closed = 11;

		internal const int Write = 12;
	}
}
