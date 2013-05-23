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

namespace xmemcached {
	/// <summary>
	/// Description of Server.
	/// </summary>
	public class Server {
		protected TcpListener Listener = null;
		private Thread Thread;
		private volatile bool Quit = false;
		private IPEndPoint m_EndPoint;
		public IPEndPoint EndPoint { get { return m_EndPoint; } }
		public bool Running { get { return Listener != null; } }
		
		public Server(IPEndPoint ep) {
			m_EndPoint = ep;
			Thread = new Thread(ListenerThread);
			Thread.Start();
		}
		
		public void Stop() {
			Quit = true;
			
			if( Listener != null ) {
				Listener.Stop();
				Listener = null;
			}
			
			Thread.Join();
		}
		
		private void ListenerThread() {
			Socket client;
			try {
				while( !Quit && !Program.StopService ) {
					if( Listener == null ) {
						Listener = new TcpListener(m_EndPoint);
						Listener.Start();
						Program.Log("Listening at {0}:{1} ({2})", EndPoint.Address, EndPoint.Port, DateTime.Now);
					}
					
					try {
						client = Listener.AcceptSocket();
						if( client == null )
							Listener = null;
						else
							Program.AddClient(client);
					}
					catch {
						try {
							if( Listener != null )
								Listener.Stop();
						}
						catch {}						
						Listener = null;						
					}
				}
			}
			catch {
			}
			Program.Log("Listener stopped at {0}:{1} ({2})", EndPoint.Address, EndPoint.Port, DateTime.Now);
		}
	}
}
