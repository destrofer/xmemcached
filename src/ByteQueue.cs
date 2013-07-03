/*
 * Copyright 2012 Viacheslav Soroka
 * Author: Viacheslav Soroka
 * 
 * This file is part of IGE (https://github.com/destrofer/IGE/).
 * 
 * IGE is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * IGE is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with IGE.  If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections;
using System.Collections.Generic;

namespace IGE {
	public class ByteQueue {
		public const int DefaultChunkSize = 256;
		
		private int ChunkSize = DefaultChunkSize;
		
		private ByteChunk ReadChunk = null;
		private int ReadPointer = 0;
		
		private ByteChunk WriteChunk = null;
		private int WritePointer = 0;
		
		// private List<ByteChunk> Chunks = new List<ByteChunk>();
		
		private int m_Length = 0;
		
		public int Length { get { return m_Length; } }
		
		public ByteQueue() {
		}
		
		public ByteQueue(int chunkSize) : this() {
			if( chunkSize < 1 )
				throw new Exception("Chunk size cannot be smaller than 1");
			ChunkSize = chunkSize;
		}
		
		public void Enqueue(byte b) {
			if( WriteChunk == null )
				AddChunk();
			if( WritePointer >= ChunkSize ) {
				AddChunk();
				WritePointer = 0;
				WriteChunk = WriteChunk.Next;
			}
			m_Length++;
			WriteChunk.Bytes[WritePointer++] = b;
		}
		
		public void Enqueue(byte[] bytes, int offset, int length) {
			if( WriteChunk == null )
				AddChunk();
			if( WritePointer >= ChunkSize ) {
				AddChunk();
				WritePointer = 0;
				WriteChunk = WriteChunk.Next;
			}
			int size;
			// m_Length += length; // increasing length with each chunk copying is slower, but in case of OutOfMemoryException the queue length will not be corupt
			while( length > 0 ) {
				size = ChunkSize - WritePointer;
				if( length <= size ) {
					size = length;
					Array.Copy(bytes, offset, WriteChunk.Bytes, WritePointer, size);
					m_Length += size;
					length = 0;
					WritePointer += size;
				}
				else {
					Array.Copy(bytes, offset, WriteChunk.Bytes, WritePointer, size);
					m_Length += size;
					WritePointer = ChunkSize; // just in case of an OutOfMemoryException
					AddChunk();
					WriteChunk = WriteChunk.Next;
					WritePointer = 0;
					length -= size;
					offset += size;
				}
			}
		}
		
		public void Enqueue(byte[] bytes) {
			Enqueue(bytes, 0, bytes.Length);
		}
		
		public byte Dequeue() {
			if( ReadPointer >= ChunkSize ) {
				ReadChunk = ReadChunk.Next;
				ReadPointer = 0;
				if( ReadChunk == null ) {
					WriteChunk = null;
					WritePointer = 0;
				}
			}
			if( m_Length == 0 )
				throw new Exception("ByteQueue is empty");
			m_Length--;
			return ReadChunk.Bytes[ReadPointer++];
		}
		
		public void Dequeue(byte[] bytes, int offset, int length) {
			if( ReadPointer >= ChunkSize ) {
				ReadChunk = ReadChunk.Next;
				ReadPointer = 0;
				if( ReadChunk == null ) {
					WriteChunk = null;
					WritePointer = 0;
				}
			}
			if( m_Length < length )
				throw new Exception("ByteQueue is smaller than requested length");
			int size;
			while( length > 0 ) {
				size = ChunkSize - ReadPointer;
				if( length <= size ) {
					size = length;
					Array.Copy(ReadChunk.Bytes, ReadPointer, bytes, offset, size);
					length = 0;
					ReadPointer += size;
				}
				else {
					Array.Copy(ReadChunk.Bytes, ReadPointer, bytes, offset, size);
					ReadChunk = ReadChunk.Next;
					ReadPointer = 0;
					length -= size;
					offset += size;
				}
				m_Length -= size;
			}
		}
		
		public byte[] Dequeue(int length) {
			if( m_Length < length )
				throw new Exception("ByteQueue is smaller than requested length");
			byte[] array = new byte[length];
			Dequeue(array, 0, length);
			return array;
		}
		
		public byte[] ToArray() {
			return Dequeue(m_Length);
		}
		
		public int IndexOf(byte searchByte) {
			ByteChunk currentChunk = ReadChunk;
			int currentPointer = ReadPointer, pos = 0;
			for(int i = m_Length; i > 0; i--, pos++ ) {
				if( currentPointer >= ChunkSize ) {
					currentChunk = currentChunk.Next;
					currentPointer = 0;
				}
				if( currentChunk.Bytes[currentPointer++] == searchByte )
					return pos;
			}
			return -1;
		}
		
		private void AddChunk() {
			ByteChunk newChunk = new ByteChunk(ChunkSize);
			if( WriteChunk != null )
				WriteChunk.Next = newChunk;
			else
				ReadChunk = WriteChunk = newChunk;
		}
		
		// This is not needed in xmemcached
		// public ByteQueueStream GetStream() {
		//	return new ByteQueueStream(this);
		// }
		
		internal class ByteChunk {
			internal byte[] Bytes;
			internal ByteChunk Next = null;
			
			public ByteChunk(int size) {
				Bytes = new byte[size];
			}
		}
	}	
}