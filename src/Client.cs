/*
 * Copyright 2012-2013 Viacheslav Soroka
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
using System.Collections;
using System.Collections.Generic;

using IGE;

namespace xmemcached {
	public class Client {
		private Socket Connection;
		private Thread Thread;
		private NetworkStream DataStream;
		private ByteQueue cmd = new ByteQueue();
		private int ib = 0;
		private char c = '\0';
		private int eol = 0;
		private string[] cmdTokens;
		private byte[] EOL = new byte[] { (byte)'\r', (byte)'\n' };
		public readonly int Id;
		public bool Connected { get { return Connection != null && Connection.Connected; } }
		public string Address { get { return (Connection == null || !Connection.Connected) ? "-" : String.Format("{0}", Connection.RemoteEndPoint.ToString()); } }
		private DateTime m_ConnectTime;
		public DateTime ConnectTime { get { return m_ConnectTime; } }
		
		private static int NextClientId = 0;
		
		public Client(Socket client) {
			Id = NextClientId++;
			if( NextClientId >= 2000000000 )
				NextClientId = 0;
			m_ConnectTime = DateTime.Now;
			Connection = client;
			DataStream = new NetworkStream(client);
			Thread = new Thread(ClientThread);
			Thread.Start();
		}
		
		public void Disconnect() {
			if( Connection != null && Connection.Connected ) {
				Connection.Shutdown(SocketShutdown.Both);
				Connection.Close();
			}
			Thread.Join();
		}
		
		private string ReadCommand() {
			eol = 0;
			do {
				if( Program.StopService )
					return null;
				ib = DataStream.ReadByte();
				if( ib < 0 || ib > 255 )
					return null;
				c = unchecked((char)((byte)ib));
				if( c == '\r' || c == '\n' ) {
					if( ++eol == 2 )
						break;
				}
				else {
					eol = 0;
					cmd.Enqueue(unchecked((byte)ib));
				}
			} while(true);
			return Encoding.UTF8.GetString(cmd.ToArray());
		}
		
		private void SendResponse(string resp) {
			byte[] respB = Encoding.ASCII.GetBytes(String.Format("{0}\r\n", resp));
			Log.WriteLine(Log.Level.Debug, "Response: {0}", resp);
			DataStream.Write(respB, 0, respB.Length);
		}
		
		private void ClientThread() {
			CacheItem item;
			string command;
			int i, bytes, exptime;
			uint flags;
			bool isCas, res, quit = false;
			int offs, read;
			
			try {
				while (!quit && !Program.StopService && Connection.Connected) {
					// read command from input
					command = ReadCommand();
					if( command == null )
						break;
					cmdTokens = command.Split(' ');
					Log.WriteLine(Log.Level.Debug, "Command: {0}", command);
					isCas = false;
					switch(cmdTokens[0]) {
						case "gets":
							isCas = true;
							goto case "get";
						case "get":
							if( cmdTokens.Length < 2 ) {
								SendResponse("CLIENT_ERROR get command must have at least one key specified");
								break;
							}
							for(i = cmdTokens.Length - 1; i >= 1; i-- ) {
								if( cmdTokens[i].Length == 0 )
									continue;
								item = Program.Get(cmdTokens[i]);
								if( item != null ) {
									if( isCas )
										SendResponse(String.Format("VALUE {0} {1} {2} {3}", cmdTokens[i], item.CustomBits, item.Data.Length, item.LastChangeId));
									else
										SendResponse(String.Format("VALUE {0} {1} {2}", cmdTokens[i], item.CustomBits, item.Data.Length));
									DataStream.Write(item.Data, 0, item.Data.Length);
									DataStream.Write(EOL, 0, 2);
								}
								else
									Log.WriteLine(Log.Level.Debug, "Not found: {0}", cmdTokens[i]);
							}
							SendResponse("END");
							break;
						
						case "set":
							if( cmdTokens.Length < 5 ) {
								SendResponse("CLIENT_ERROR set command must have at least 5 parameters");
								break;
							}
							if( !uint.TryParse(cmdTokens[2], out flags) ) {
								SendResponse("CLIENT_ERROR flags parameter must be numeric");
								break;
							}
							if( !int.TryParse(cmdTokens[3], out exptime) ) {
								SendResponse("CLIENT_ERROR exptime parameter must be numeric");
								break;
							}
							if( !int.TryParse(cmdTokens[4], out bytes) ) {
								SendResponse("CLIENT_ERROR bytes parameter must be numeric");
								break;
							}
							if( bytes > Program.Config.MaxStorage ) {
								for(i = bytes; i > 0; i--)
									DataStream.ReadByte();
								if( cmdTokens.Length < 6 )
									SendResponse("NOT_STORED");
								break;
							}
							byte[] data = new byte[bytes];
							offs = 0;
							read = 0;
							while( !Program.StopService && offs < bytes ) {
								read = DataStream.Read(data, offs, bytes - offs);
								if( read == 0 )
									break;
								offs += read;
							}
							if( offs < bytes || Program.StopService || (char)DataStream.ReadByte() != '\r' || (char)DataStream.ReadByte() != '\n' )
								break;
							DateTime exp;
							exp = (exptime == 0) ? DateTime.MaxValue : ((exptime <= 60*60*24*30) ? DateTime.Now : new DateTime(1970,1,1,0,0,0,0));
							exp = exp.AddSeconds(exptime).ToLocalTime();
							res = (Program.Set(cmdTokens[1], flags, data, exp, SetFlags.Create | SetFlags.Replace) == StoreResult.Stored);
							if( cmdTokens.Length < 6 ) {
								if( res )
									SendResponse("STORED");
								else
									SendResponse("NOT_STORED");
							}
							break;
							
						case "delete":
							if( cmdTokens.Length < 2 ) {
								SendResponse("CLIENT_ERROR delete command must have a key specified");
								break;
							}
							if( cmdTokens.Length < 4 || !int.TryParse(cmdTokens[3], out read) )
								read = -1;
							res = Program.Delete(cmdTokens[1], read);
							if( cmdTokens.Length < 3 )
								SendResponse(res ? "DELETED" : "NOT_FOUND");
							break;
							
						case "flush_all":
							if( cmdTokens.Length < 3 ) {
								Program.Flush(-1);
								SendResponse("OK");
							}
							else if( int.TryParse(cmdTokens[3], out read) )
								Program.Flush(read);
							break;
						
						case "quit":
							quit = true;
							break;
							
						case "ping": // a special command sent by cross server threads
							break;
							
						default:
							SendResponse("ERROR");
							break;
					}
				}
			}
			catch {
			}

			try {
				if( Connection != null ) {
					IPEndPoint ep = Connection.RemoteEndPoint as IPEndPoint;
					if( ep != null )
						Log.WriteLine(Log.Level.Debug, "Client disconnected at {0}:{1}", ep.Address, ep.Port);
					else
						Log.WriteLine(Log.Level.Debug, "Client disconnected");
					Connection.Shutdown(SocketShutdown.Both);
					Connection.Close();
				}
			}
			catch {
				Log.WriteLine(Log.Level.Debug, "Client disconnected");
			}
			Program.RemoveClient(this);
		}
	}
}
