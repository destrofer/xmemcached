/*
 * Copyright 2012 Viacheslav Soroka
 * Author: Viacheslav Soroka
 * 
 * This file is part of xmemcached.
 * 
 * xmemcached is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 * 
 * xmemcached is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU Lesser General Public License for more details.
 * 
 * You should have received a copy of the GNU Lesser General Public License
 * along with xmemcached.  If not, see <http://www.gnu.org/licenses/>.
 */
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Collections.Generic;

namespace xmemcached {
	public class CSClient {
		private static Queue<DeleteEntity> DeleteQueue = new Queue<DeleteEntity>();
		private Thread Thread;
		private volatile bool Quit = false;
		private TcpClient Connection = null;
		public volatile int FlushSenderId = -1;
		public object FlushLock = new object();
		
		public CSClient() {
			Thread = new Thread(CrossServerClientThread);
			Thread.Start();
		}
		
		public void Stop() {
			Quit = true;
			Thread.Join();
			DeleteQueue.Clear();
		}
		
		public void AddToDeleteQueue(string id, int originServerId) {
			if( DeleteQueue.Count >= Program.Config.MaxDeleteQueueLength )
				DeleteQueue.Dequeue();
			DeleteQueue.Enqueue(new DeleteEntity { Id = id, OriginServerId = originServerId });
		}
		
		private void CrossServerClientThread() {
			DeleteEntity del;
			NetworkStream stream = null;
			byte[] cmd;
			DateTime nextPing = DateTime.Now.AddSeconds(10);
			int flushId;
			
			while( !Quit && !Program.StopService ) {
				if( Connection == null || !Connection.Connected ) {
					stream = null;
					try {
						Connection = new TcpClient();
						Connection.Connect(Program.Config.NextServerAddr);
					}
					catch {
						// Program.Log("Failed to connect to next server in chain loop");
						Thread.Sleep(Program.Config.ReconnectDelay);
					}
					if( Connection.Connected ) {
						Program.Log("Connection to next server in chain loop established");
						stream = Connection.GetStream();
					}
					Thread.Sleep(50);
					continue;
				}
				try {
					if( nextPing < DateTime.Now ) {
						nextPing = DateTime.Now.AddSeconds(10);
						cmd = Encoding.ASCII.GetBytes("ping\r\n");
						stream.Write(cmd, 0, cmd.Length);
					}
					while( stream.CanWrite && DeleteQueue.Count > 0 ) {
						del = DeleteQueue.Dequeue();
						cmd = Encoding.ASCII.GetBytes(String.Format("delete {0} noreply {1}\r\n", del.Id, del.OriginServerId));
						stream.Write(cmd, 0, cmd.Length);
					}
					flushId = -1;
					lock(FlushLock) {
						if( FlushSenderId >= 0 ) {
							flushId = FlushSenderId;
							FlushSenderId = -1;
						}
					}
					if( flushId >= 0 ) {
						cmd = Encoding.ASCII.GetBytes(String.Format("flush_all 0 noreply {0}\r\n", flushId));
						stream.Write(cmd, 0, cmd.Length);
					}
					
					Thread.Sleep(50);
					if( !Connection.Connected )
						Program.Log("Connection to next server in chain loop lost");
				}
				catch {
					Program.Log("Connection to next server in chain loop lost");
					Connection = null;
				}
			}
			if( Connection != null && Connection.Connected ) {
				Connection.Close();
				Connection = null;
			}
		}
		
		public void Flush(int originServerId) {
			lock(FlushLock) {
				int myServerId = (int)Program.Config.ServerId;				
				if( FlushSenderId != myServerId )
					FlushSenderId = (originServerId >= 0) ? originServerId : myServerId;
				DeleteQueue.Clear();
			}
		}
		
		public bool IsFlushing { get { return FlushSenderId >= 0; } }
		
		internal struct DeleteEntity {
			internal string Id;
			internal int OriginServerId;
		}
	}
}
