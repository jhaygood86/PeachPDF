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
	/// <summary>
	/// <see cref="System.IO.Stream"/>
	/// decorator that decompresses brotli data.
	/// <p> Not thread-safe.
	/// </summary>
	internal class BrotliInputStream : System.IO.Stream
	{
		internal const int DefaultInternalBufferSize = 16384;

		/// <summary>Internal buffer used for efficient byte-by-byte reading.</summary>
		private byte[] buffer;

		/// <summary>Number of decoded but still unused bytes in internal buffer.</summary>
		private int remainingBufferBytes;

		/// <summary>Next unused byte offset.</summary>
		private int bufferOffset;

		/// <summary>Decoder state.</summary>
		private readonly PeachDrawing.Text.Brotli.Internal.State state = new PeachDrawing.Text.Brotli.Internal.State();

		/// <summary>
		/// Creates a
		/// <see cref="System.IO.Stream"/>
		/// wrapper that decompresses brotli data.
		/// <p> For byte-by-byte reading (
		/// <see cref="ReadByte()"/>
		/// ) internal buffer with
		/// <see cref="DefaultInternalBufferSize"/>
		/// size is allocated and used.
		/// <p> Will block the thread until first kilobyte of data of source is available.
		/// </summary>
		/// <param name="source">underlying data source</param>
		/// <exception cref="System.IO.IOException">in case of corrupted data or source stream problems</exception>
		public BrotliInputStream(System.IO.Stream source)
			: this(source, DefaultInternalBufferSize, null)
		{
		}

		/// <summary>
		/// Creates a
		/// <see cref="System.IO.Stream"/>
		/// wrapper that decompresses brotli data.
		/// <p> For byte-by-byte reading (
		/// <see cref="ReadByte()"/>
		/// ) internal buffer of specified size is
		/// allocated and used.
		/// <p> Will block the thread until first kilobyte of data of source is available.
		/// </summary>
		/// <param name="source">compressed data source</param>
		/// <param name="byteReadBufferSize">
		/// size of internal buffer used in case of
		/// byte-by-byte reading
		/// </param>
		/// <exception cref="System.IO.IOException">in case of corrupted data or source stream problems</exception>
		public BrotliInputStream(System.IO.Stream source, int byteReadBufferSize)
			: this(source, byteReadBufferSize, null)
		{
		}

		/// <summary>
		/// Creates a
		/// <see cref="System.IO.Stream"/>
		/// wrapper that decompresses brotli data.
		/// <p> For byte-by-byte reading (
		/// <see cref="ReadByte()"/>
		/// ) internal buffer of specified size is
		/// allocated and used.
		/// <p> Will block the thread until first kilobyte of data of source is available.
		/// </summary>
		/// <param name="source">compressed data source</param>
		/// <param name="byteReadBufferSize">
		/// size of internal buffer used in case of
		/// byte-by-byte reading
		/// </param>
		/// <param name="customDictionary">
		/// custom dictionary data;
		/// <see langword="null"/>
		/// if not used
		/// </param>
		/// <exception cref="System.IO.IOException">in case of corrupted data or source stream problems</exception>
		public BrotliInputStream(System.IO.Stream source, int byteReadBufferSize, byte[] customDictionary)
		{
			if (byteReadBufferSize <= 0)
			{
				throw new System.ArgumentException("Bad buffer size:" + byteReadBufferSize);
			}
			else if (source == null)
			{
				throw new System.ArgumentException("source is null");
			}
			this.buffer = new byte[byteReadBufferSize];
			this.remainingBufferBytes = 0;
			this.bufferOffset = 0;
			try
			{
				PeachDrawing.Text.Brotli.Internal.State.SetInput(state, source);
			}
			catch (PeachDrawing.Text.Brotli.Internal.BrotliRuntimeException ex)
			{
				throw new System.IO.IOException("Brotli decoder initialization failed", ex);
			}
			if (customDictionary != null)
			{
				PeachDrawing.Text.Brotli.Internal.Decode.SetCustomDictionary(state, customDictionary);
			}
		}

		/// <summary><inheritDoc/></summary>
		/// <exception cref="System.IO.IOException"/>
		public override void Close()
		{
			PeachDrawing.Text.Brotli.Internal.State.Close(state);
		}

		/// <summary><inheritDoc/></summary>
		/// <exception cref="System.IO.IOException"/>
		public override int ReadByte()
		{
			if (bufferOffset >= remainingBufferBytes)
			{
				remainingBufferBytes = Read(buffer, 0, buffer.Length);
				bufferOffset = 0;
				// PeachPDF fix: Read() below returns 0 at end-of-stream (the .NET Stream.Read contract), not -1 (the
				// java.io.InputStream.read() contract this file was transliterated from) - see PORTING-NOTES.md.
				if (remainingBufferBytes == 0)
				{
					return -1;
				}
			}
			return buffer[bufferOffset++] & unchecked((int)(0xFF));
		}

		/// <summary><inheritDoc/></summary>
		/// <exception cref="System.IO.IOException"/>
		public override int Read(byte[] destBuffer, int destOffset, int destLen)
		{
			if (destOffset < 0)
			{
				throw new System.ArgumentException("Bad offset: " + destOffset);
			}
			else if (destLen < 0)
			{
				throw new System.ArgumentException("Bad length: " + destLen);
			}
			else if (destOffset + destLen > destBuffer.Length)
			{
				throw new System.ArgumentException("Buffer overflow: " + (destOffset + destLen) + " > " + destBuffer.Length);
			}
			else if (destLen == 0)
			{
				return 0;
			}
			int copyLen = System.Math.Max(remainingBufferBytes - bufferOffset, 0);
			if (copyLen != 0)
			{
				copyLen = System.Math.Min(copyLen, destLen);
				System.Array.Copy(buffer, bufferOffset, destBuffer, destOffset, copyLen);
				bufferOffset += copyLen;
				destOffset += copyLen;
				destLen -= copyLen;
				if (destLen == 0)
				{
					return copyLen;
				}
			}
			try
			{
				state.output = destBuffer;
				state.outputOffset = destOffset;
				state.outputLength = destLen;
				state.outputUsed = 0;
				PeachDrawing.Text.Brotli.Internal.Decode.Decompress(state);
				if (state.outputUsed == 0)
				{
					// PeachPDF fix: this was `return -1;` in the upstream port, which is the java.io.InputStream.read()
					// end-of-stream convention. System.IO.Stream.Read must return 0 at end-of-stream - a negative return
					// value is not a valid outcome for it at all, and broke every idiomatic .NET caller (Stream.CopyTo,
					// StreamReader, ...), which is how this was actually found: a round-trip test decoding real data
					// through Stream.CopyTo threw ArgumentOutOfRangeException from MemoryStream.Write, not a fabricated
					// value. See PORTING-NOTES.md. Returning bare 0 here (as the very first version of this fix did)
					// is itself wrong whenever copyLen > 0: this method already copied copyLen bytes from the internal
					// read-ahead buffer into destBuffer above, so 0 would falsely claim end-of-stream and silently
					// discard those bytes for any caller that trusts the return value (e.g. one mixing ReadByte() and
					// Read() calls). Returning copyLen is correct in both cases - it degrades to the true "0" only when
					// copyLen itself is 0. See PORTING-NOTES.md.
					return copyLen;
				}
				return state.outputUsed + copyLen;
			}
			catch (PeachDrawing.Text.Brotli.Internal.BrotliRuntimeException ex)
			{
				throw new System.IO.IOException("Brotli stream decoding failed", ex);
			}
		}
		// <{[INJECTED CODE]}>
		public override bool CanRead {
			get {return true;}
		}

		public override bool CanSeek {
			get {return false;}
		}
		public override long Length {
			get {throw new System.NotSupportedException();}
		}
		public override long Position {
			get {throw new System.NotSupportedException();}
			set {throw new System.NotSupportedException();}
		}
		public override long Seek(long offset, System.IO.SeekOrigin origin) {
			throw new System.NotSupportedException();
		}
		public override void SetLength(long value){
			throw new System.NotSupportedException();
		}

		public override bool CanWrite{get{return false;}}
		public override System.IAsyncResult BeginWrite(byte[] buffer, int offset,
				int count, System.AsyncCallback callback, object state) {
			throw new System.NotSupportedException();
		}
		public override void Write(byte[] buffer, int offset, int count) {
			throw new System.NotSupportedException();
		}

		public override void Flush() {}
	}
}
