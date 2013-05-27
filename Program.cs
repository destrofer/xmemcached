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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using Mono.Unix;
using Mono.Unix.Native;

using System.ComponentModel;
using System.Configuration.Install;
using System.ServiceProcess;

using System.IO;

namespace xmemcached {
	class ServiceApp {
		public static void Main(string[] args) {
			if( args.Length > 0 && args[0].Equals("-c") ) {
				Log.LogToConsole = true;
				Program service = new Program();
				service.ExecuteAsConsoleApp();
			}
			else {
				Log.LogToConsole = false;
				ServiceBase[] servicesToRun;
				servicesToRun = new ServiceBase[] { new Program() };
				ServiceBase.Run( servicesToRun );
			}
		}
	}

	[RunInstaller(true)]
	public class CustomServiceInstaller : System.Configuration.Install.Installer {
		public CustomServiceInstaller() {
			ServiceProcessInstaller process = new ServiceProcessInstaller();

            process.Account = ServiceAccount.LocalSystem;

            ServiceInstaller xmemcachedService = new ServiceInstaller();

            xmemcachedService.StartType = ServiceStartMode.Automatic;
            xmemcachedService.ServiceName = "xmemcached";
            xmemcachedService.DisplayName = "xmemcached";
            
            Installers.Add( process );
            Installers.Add( xmemcachedService );
		}
	}
	
	public class Program : System.ServiceProcess.ServiceBase {
		public static volatile bool IsLinux;
		
		private static object ConsoleSync = new object();
		public static Config Config = null;
		
		public static List<Server> Servers = new List<Server>();
		public static Dictionary<int, Client> Clients = new Dictionary<int, Client>();
		private static CSClient NextServer = null;
		
		public static Dictionary<string, CacheItem> Storage = new Dictionary<string, CacheItem>();
		public static Dictionary<string, Tag> Tags = new Dictionary<string, Tag>();
		public static object WriteLock = new object();
		public static ulong StoredSize = 0;

		public static volatile bool StopService = false;
		private Thread ServiceThread = null;
		private static UnixSignal[] TermSig = null;
		public static volatile bool RunningInConsole = false;
		
		static Program() {
			IsLinux = Config.IsLinux;
		}
		
		public Program() {
			ServiceName = "xmemcached";
		}
		
		protected override void OnStart(string[] args) {
			Start();
			base.OnStart(args);
		}
		
		protected override void OnStop() {
			StopService = true;
			ServiceThread.Join(29000);
			base.OnStop();
		}
		
		protected void Start() {
			StopService = false;
			ServiceThread = new Thread(ServiceThreadFunc);
			ServiceThread.Start();
		}
		
		public void ExecuteAsConsoleApp() {
			RunningInConsole = true;
			Start();
			ServiceThread.Join();
		}
		
		
		public void ServiceThreadFunc() {
			Config = new Config();

			Log.MaxLevel = Log.Level.Info;
			Log.SyslogServiceName = ServiceName;
			if( Config.LogPath.ToLower().Equals("syslog") ) {
				Log.LogToFile = false;
				Log.LogToSyslog = true;
			}
			else {
				Log.LogToFile = true;
				Log.LogToSyslog = false;
				Log.LogFilePath = Config.LogPath;
			}
			
			Log.WriteLine(Log.Level.Important, "Starting server {0}", Config.ServerId);
			
			/*
 			foreach(IPAddress addr in Config.AllowedAddr.Keys)
				Console.WriteLine("Allowed {0}", addr);
			
			if( Config.NextServerAddr == null )
				Console.WriteLine("No next server in chain loop (possibly stand alone server)");
			else
				Console.WriteLine("Next server in chain loop {0}:{1}", Config.NextServerAddr.Address,  Config.NextServerAddr.Port);
			
			Console.WriteLine("Max storage {0}", Config.MaxStorage);
			*/

			foreach(IPEndPoint ep in Config.BindAddr)
				Servers.Add(new Server(ep));
			
			if( Config.NextServerAddr != null )
				NextServer = new CSClient();

			Thread.Sleep(1000);
			
			if( !RunningInConsole && IsLinux ) {
				try {
					TermSig = new UnixSignal[] {
						new UnixSignal(Signum.SIGABRT),
						new UnixSignal(Signum.SIGHUP),
						new UnixSignal(Signum.SIGINT),
						new UnixSignal(Signum.SIGQUIT),
						new UnixSignal(Signum.SIGTERM),
						new UnixSignal(Signum.SIGUSR1),
						
						//new UnixSignal(Signum.SIGKILL),
						//new UnixSignal(Signum.SIGSTOP),
					};
				}
				catch(Exception ex) {
					StopService = true;
					TermSig = null;
					Log.WriteLine(Log.Level.Debug, "Exception: {0}", ex);
				}
			}
			
			if( TermSig == null ) {
				while( !StopService ) {
					System.Threading.Thread.Sleep(1000);
					if( RunningInConsole && Console.KeyAvailable ) {
						Console.ReadKey(true);
						StopService = true;
					}
				}
			}
			else {
				int sig;
				while( !StopService ) {
					sig = UnixSignal.WaitAny(TermSig, -1);
					if( sig >= 0 && sig < TermSig.Length ) {
						if( sig >= 0 && sig <= 4 ) {
							StopService = true;
							break;
						}
						else {
							Log.WriteLine(Log.Level.Important, "Start of state log ({0})", DateTime.Now);
							Log.WriteLine(Log.Level.Important, "GC.GetTotalMemory: {0}", GC.GetTotalMemory(true));
							lock(WriteLock) {
								Log.WriteLine(Log.Level.Important, "Tags ({0}):", Tags.Count);
								foreach(KeyValuePair<string, Tag> pair in Tags) {
									Log.WriteLine(Log.Level.Important, "{0}: {1}", pair.Key, pair.Value.Items.Count);
								}
								
								Log.WriteLine(Log.Level.Important, "Keys ({0}):", Storage.Count);
								foreach(KeyValuePair<string, CacheItem> pair in Storage) {
									Log.WriteLine(Log.Level.Important, "{0}: {1}", pair.Key, pair.Value.Data.Length);
								}
								
								Log.WriteLine(Log.Level.Important, "Servers ({0}):", Servers.Count);
								foreach( Server server in Servers ) {
									Log.WriteLine(Log.Level.Important, "{0}:{1} {2}", server.EndPoint.Address, server.EndPoint.Port, server.Running ? "Running" : "Stopped");
								}
								
								Log.WriteLine(Log.Level.Important, "Clients ({0}):", Clients.Count);
								foreach( KeyValuePair<int, Client> pair in Clients ) {
									Log.WriteLine(Log.Level.Important, "{0} {1} {2}", pair.Key, pair.Value.Address, pair.Value.ConnectTime);
								}
							}
							Log.WriteLine(Log.Level.Important, "End of state log");
						}
					}
				}
			}
			
			Log.WriteLine(Log.Level.Important, "Stopping service");
			Log.WriteLine(Log.Level.Debug, "Listeners...");
			
			foreach( Server server in Servers )
				server.Stop();

			Log.WriteLine(Log.Level.Debug, "Clients...");
			
			Client[] clients;
			lock(Clients) {
				clients = new Client[Clients.Count];
				Clients.Values.CopyTo(clients, 0);
			}
			
			foreach( Client client in clients )
				client.Disconnect();

			Log.WriteLine(Log.Level.Debug, "Next server connection...");
			
			if( NextServer != null )
				NextServer.Stop();
			
			Log.WriteLine(Log.Level.Important, "Service stopped");
		}
		
		public static void AddClient(Socket client) {
			try {
				IPEndPoint ep = client.RemoteEndPoint as IPEndPoint;
				if( ep == null || !Config.AllowedAddr.ContainsKey(ep.Address) ) {
					client.Close();
					return;
				}
				Log.WriteLine(Log.Level.Debug, "Client connected from {0}:{1}", ep.Address, ep.Port);
				lock(Clients) {
					Client newClient = new Client(client);
					Clients.Add(newClient.Id, newClient);
				}
			}
			catch {
			}
		}
		
		public static void RemoveClient(Client client) {
			lock(Clients) {
				if( Clients.ContainsKey(client.Id) )
					Clients.Remove(client.Id);
			}
		}
		
		#region Cache functions
		
		public static CacheItem Get(string id) {
			CacheItem item;
			int pos = id.IndexOf(':');
			if( pos == 0 ) return null;
			if( pos >= 0 ) id = id.Substring(0, pos);
			if( Storage.TryGetValue(id, out item) ) {
				if( item.Expire <= DateTime.Now ) {
					Log.WriteLine(Log.Level.Debug, "Expired: {0}", id);
					Delete(id, -1);
					return null;
				}
				item.LastUse = DateTime.Now;
			}
			return item;
		}
		
		public static StoreResult Set(string id, uint customBits, byte[] data, DateTime expire, SetFlags flags) {
			string[] spl = id.Split(':');
			string[] tags;
			string itemId = spl[0];
			if( itemId.Length == 0 || data.Length > Config.MaxStorage )
				return StoreResult.NotStored;
			
			if( spl.Length > 1 ) {
				tags = new string[spl.Length - 1];
				Array.Copy(spl, 1, tags, 0, tags.Length);
			}
			else
				tags = null;
			spl = null; // not needed anymore so free memory as quick as possible
			
			lock(WriteLock) {
				if( Storage.ContainsKey(itemId) ) {
					if( (flags & SetFlags.Replace) != SetFlags.Replace )
						return StoreResult.Exists;
					Delete(itemId, -1);
				}
				else if( (flags & SetFlags.Create) != SetFlags.Create )
					return StoreResult.NotFound;
				if( StoredSize + (ulong)data.Length > (ulong)Config.MaxStorage ) {
					bool clean = true;
					while( StoredSize + (ulong)data.Length > (ulong)Config.MaxStorage ) {
						if( clean ) {
							Log.WriteLine(Log.Level.Debug, "Cleaning up expired items");
							// clean up all expired entries
							List<string> toRemove = new List<string>();
							DateTime now = DateTime.Now;
							foreach( KeyValuePair<string, CacheItem> cpair in Storage )
								if( cpair.Value.Expire <= now )
									toRemove.Add(cpair.Key);
							foreach( string remId in toRemove )
								Delete(remId, -1);
							clean = false;
						}
						else {
							Log.WriteLine(Log.Level.Important, "Cleaning up expired items did not give us enough memory. Cleaning up old items.");
							// still not enough memory .. cleanup oldest used entries
							IOrderedEnumerable<KeyValuePair<string, CacheItem>> sorted = Storage.OrderBy(p => p.Value.LastUse);
							long toFree = (long)data.Length - (Config.MaxStorage - (long)StoredSize);
							foreach(KeyValuePair<string, CacheItem> cpair in sorted) {
								toFree -= cpair.Value.Data.Length;
								Delete(cpair.Key, -1);
								if( toFree <= 0 )
									break;
							}
						}
						// too much ... need to clean up a bit
					}
				}
				CacheItem item = new CacheItem(tags, customBits, data, expire, 0);
				Storage.Add(itemId, item);
				StoredSize += (ulong)data.Length;
				if( tags != null ) {
					Tag tag;
					foreach( string tagId in tags ) {
						if( Tags.ContainsKey(tagId) )
							tag = Tags[tagId];
						else {
							tag = new Tag();
							Tags.Add(tagId, tag);
						}
						tag.Items.Add(itemId);
					}
				}
			}
			return StoreResult.Stored;
		}
		
		public static bool Delete(string id, int originServerId) {
			Tag tmpTag, tmpTag2;
			CacheItem tmpItem;
			int pos;
			if( originServerId == Config.ServerId || id.Length < 1 )
				return false;
			
			if( NextServer != null && !NextServer.IsFlushing )
				NextServer.AddToDeleteQueue(id, (originServerId == -1) ? (int)Config.ServerId : originServerId);

			if( (pos = id.IndexOf(':')) >= 0 ) {
				// removing by tag
				id = id.Substring(pos + 1);
				lock(WriteLock) {
					if( !Tags.ContainsKey(id) )
						return false;
					tmpTag = Tags[id];
					Tags.Remove(id);
					foreach(string stId in tmpTag.Items)
						if( Storage.ContainsKey(stId) ) {
							// remove item
							tmpItem = Storage[stId];
							Storage.Remove(stId);
							StoredSize -= (ulong)tmpItem.Data.Length;
							if( tmpItem.Tags != null ) {
								foreach (string tagId in tmpItem.Tags)
									if( Tags.ContainsKey(tagId) ) {
										tmpTag2 = Tags[tagId];
										tmpTag2.Items.Remove(stId);
										if( tmpTag2.Items.Count == 0 )
											Tags.Remove(tagId);
									}
							}
						}
				}
			}
			else {
				lock(WriteLock) {
					if( !Storage.ContainsKey(id) )
						return false;
					// remove item
					tmpItem = Storage[id];
					Storage.Remove(id);
					StoredSize -= (ulong)tmpItem.Data.Length;
					if( tmpItem.Tags != null ) {
						foreach(string tagId in tmpItem.Tags)
							if( Tags.ContainsKey(tagId) ) {
								tmpTag2 = Tags[tagId];
								tmpTag2.Items.Remove(id);
								if( tmpTag2.Items.Count == 0 )
									Tags.Remove(tagId);
							}
					}
				}
			}
			return true;
		}
		
		public static void Flush(int originServerId) {
			if( originServerId == Config.ServerId )
				return;
			if( NextServer != null )
				NextServer.Flush(originServerId);
			lock(WriteLock) {
				Storage.Clear();
				Tags.Clear();
			}
		}

		#endregion Cache functions
	}
	
	[Flags]
	public enum SetFlags {
		None = 0x00,
		Create = 0x01,
		Replace = 0x02,
		
	}
	
	public enum StoreResult {
		Stored,
		NotStored,
		Exists,
		NotFound
	}
}