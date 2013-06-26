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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Net;
using System.Net.Sockets;

namespace xmemcached {
	public class Config {
		public const string ConfigFilePathWin = @"C:\xmemcached.conf";
		public const string ConfigFilePathLinux = @"/etc/xmemcached/xmemcached.conf";
		
		public Dictionary<IPAddress, bool> AllowedAddr = new Dictionary<IPAddress, bool>();
		public List<IPEndPoint> BindAddr = new List<IPEndPoint>();
		public uint ServerId = 0;
		public IPEndPoint NextServerAddr = null;
		public long MaxStorage = 16 * 1024 * 1024;
		public int MaxDeleteQueueLength = 128;
		public int ReconnectDelay = 15000;
		public string LogPath = null;
		public char TagCharacter = ':';
		
		public static bool IsLinux {
		    get {
		        PlatformID p = Environment.OSVersion.Platform;
		        return (p == PlatformID.Unix) || (p == PlatformID.MacOSX) || ((int)p == 128);
		    }
		}
		
		public Config() {
			string line;
			string[] data;
			string[] addressList;
			string[] ipport;
			char[] optionSplitChars = new char[] { '=' };
			char[] addrSplitChars = new char[] { ':' };
			int lineNumber = 0, port;
			string addr;
			IPAddress ipaddr;
			IPEndPoint endpoint;
			
			using(StreamReader r = new StreamReader(new FileStream(IsLinux ? ConfigFilePathLinux : ConfigFilePathWin, FileMode.Open), Encoding.UTF8)) {
				while( !r.EndOfStream ) {
					lineNumber++;
					line = r.ReadLine().Trim();
					if( line.Length == 0 || line[0] == '#' )
						continue;
					data = line.Split(optionSplitChars, 2);
					if( data.Length < 2 )
						throw new Exception(String.Format("Misformatted configuration option on line {0}", lineNumber));
					data[0] = data[0].Trim();
					if( data[0].Length == 0 )
						throw new Exception(String.Format("Misformatted configuration option on line {0}", lineNumber));
					data[1] = data[1].Trim();
					if( data[1].Length == 0 )
						continue; // empty option is considered to have a default value
					switch (data[0]) {
						case "log":
							LogPath = data[1].Equals("") ? null : data[1];
							break;
							
						case "bind_addr":
							addressList = data[1].Split(',',';',' ','\t');
							
							foreach (string xaddr in addressList) {
								addr = xaddr.Trim();
								if( addr.Length == 0 )
									continue;
								ipport = addr.Split(addrSplitChars, 2);
								if( ipport.Length < 2 || !int.TryParse(ipport[1], out port) )
									port = 11211; // default memcached port is 11211
								if( ipport[0].Equals("*") ) {
									BindAddr.Clear();
									BindAddr.Add(new IPEndPoint(IPAddress.Any, port));
									break;
								}
								else if( ipport[0].Equals("localhost") )
									ipaddr = IPAddress.Loopback;
								else if( !IPAddress.TryParse(ipport[0], out ipaddr) )
									throw new Exception(String.Format("Invalid IP address on line {0}", lineNumber));									
								endpoint = new IPEndPoint(ipaddr, port);
								if( !BindAddr.Contains(endpoint) )
									BindAddr.Add(endpoint);
							}
							break;
							
						case "allowed_addr":
							addressList = data[1].Split(',',';',' ','\t');
							
							foreach (string xaddr in addressList) {
								addr = xaddr.Trim();
								if( addr.Length == 0 )
									continue;
								if( addr.Equals("*") ) {
									AllowedAddr.Clear();
									AllowedAddr.Add(IPAddress.Any, true);
									break;
								}
								else if( addr.Equals("localhost") )
									ipaddr = IPAddress.Loopback;
								else if( !IPAddress.TryParse(addr, out ipaddr) )
									throw new Exception(String.Format("Invalid IP address on line {0}", lineNumber));
								if( !AllowedAddr.ContainsKey(ipaddr) )
									AllowedAddr.Add(ipaddr, true);
							}
							break;
						
						case "next_server_addr":
							ipport = data[1].Split(addrSplitChars, 2);
							if( ipport.Length < 2 || !int.TryParse(ipport[1], out port) )
								port = 11211; // default memcached port is 11211
							if( ipport[0].Equals("localhost") )
								NextServerAddr = new IPEndPoint(IPAddress.Loopback, port);
							else if( IPAddress.TryParse(ipport[0], out ipaddr) )
								NextServerAddr = new IPEndPoint(ipaddr, port);
							else
								throw new Exception(String.Format("Invalid IP address on line {0}", lineNumber));
							break;
						
						case "max_storage":
							if( !long.TryParse(data[1], out MaxStorage) ) {
								char sizeMod = data[1][data[1].Length - 1];
								data[1] = data[1].Substring(0, data[1].Length - 1);
								if( !long.TryParse(data[1], out MaxStorage) )
									throw new Exception(String.Format("Invalid size value on line {0}", lineNumber));
								if( sizeMod == 'k' || sizeMod == 'K' )
									MaxStorage *= 1024;
								else if( sizeMod == 'm' || sizeMod == 'M' )
									MaxStorage *= 1024 * 1024;
								else if( sizeMod == 'g' || sizeMod == 'G' )
									MaxStorage *= 1024 * 1024 * 1024;
								else
									throw new Exception(String.Format("Invalid size modifier on line {0}. Only 'K', 'M' and 'G' modifiers supported.", lineNumber));
							}
							break;

						case "server_id":
							if( !uint.TryParse(data[1], out ServerId) )
								throw new Exception(String.Format("'server_id' configuration option must be a 32 bit integer on line {0}", lineNumber));
							break;
							
						case "tag_character":
							if( data[1].Length > 1 )
								Log.WriteLine(Log.Level.Warning, "'tag_character' configuration option has a value of multiple characters on line {0}. Only First character will be used.", lineNumber);
							TagCharacter = data[1][0];
							break;
					}
				}
			}
			
			// fill in default values
			if( BindAddr.Count == 0 )
				BindAddr.Add(new IPEndPoint(IPAddress.Loopback, 11211));
			
			if( AllowedAddr.Count == 0 ) {
				foreach (IPEndPoint ep in BindAddr) {
					if( ep.Address == IPAddress.Any ) {
						// TODO: need to detect which ip addresses the current machine has and add them to that list
						AllowedAddr.Add(IPAddress.Loopback, true);
					}
					else
						AllowedAddr.Add(ep.Address, true);
				}
			}
			
			if( LogPath == null || LogPath.Equals("") )
				LogPath = "syslog";
		}
	}
}
