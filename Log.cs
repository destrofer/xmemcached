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
using System.Text;
using System.IO;
using System.Runtime.InteropServices;

using Mono.Posix;
using Mono.Unix;
using Mono.Unix.Native;

namespace xmemcached {
	/// <summary>
	/// </summary>
	public static class Log {
		private static object ConsoleLock = new object();
		private static StreamWriter LogFile = null;
		private static bool IsLinux;
		private static bool SyslogReady = false;
		private static string m_SyslogServiceName = null;
		
		public static bool LogToSyslog = true;
		public static bool LogToFile = false;
		public static bool LogToConsole = false;
		public static Level MaxLevel = Level.Info;
		public static string LogFilePath = null;
		public static bool AutoFlush = true;
		
		public static string SyslogServiceName {
			get { return m_SyslogServiceName; }
			set {
				if( value != null && !value.Equals("") ) {
					m_SyslogServiceName = value;
					SyslogReady = false;
				}
			}
		}
		
		static Log() {
			PlatformID p = Environment.OSVersion.Platform;
		    IsLinux = (p == PlatformID.Unix) || (p == PlatformID.MacOSX) || ((int)p == 128);
			Console.OutputEncoding = Encoding.UTF8;
			SyslogServiceName = AppDomain.CurrentDomain.FriendlyName;
			LogFilePath = String.Format("{0}.log", SyslogServiceName);
			AppDomain.CurrentDomain.DomainUnload += LogClose;
		}

		private static void LogClose(object sender, EventArgs args) {
			if( LogFile != null )
				LogFile.Close();
		}
		
		public static void Flush() {
			if( LogFile != null )
				LogFile.Flush();
		}
		
		public static void WriteLine(Level level, string fmt, params object[] parm) {
			if( (int)level > (int)MaxLevel )
				return;
			lock(ConsoleLock) {
				if( LogToFile ) {
					try {
						if( LogFile == null )
								LogFile = new StreamWriter(new FileStream(LogFilePath, System.IO.FileMode.Append));
						LogFile.WriteLine("[{0}] {1}", DateTime.Now, String.Format(fmt, parm));
					}
					catch {
						LogToFile = false;
						if( !LogToSyslog && !LogToConsole )
							LogToSyslog = true; // we must log somewhere, right?
						WriteLine(Level.Info, "Logging to file disabled due to errors while opening or writing to the log file.");
					}
				}
				
				if( LogToSyslog ) {
					if( IsLinux ) {
						if( !SyslogReady ) {
							Mono.Unix.Native.Syscall.openlog(Marshal.StringToHGlobalAuto(SyslogServiceName), SyslogOptions.LOG_PERROR | SyslogOptions.LOG_PID, SyslogFacility.LOG_DAEMON);
							SyslogReady = true;
						}
						SyslogLevel slevel = SyslogLevel.LOG_DEBUG;
						switch(level) {
							case Level.Important: slevel = SyslogLevel.LOG_INFO; break;
							case Level.Error: slevel = SyslogLevel.LOG_ERR; break;
							case Level.Warning: slevel = SyslogLevel.LOG_WARNING; break;
							case Level.Info: slevel = SyslogLevel.LOG_INFO; break;
							case Level.Debug: slevel = SyslogLevel.LOG_DEBUG; break;
						}
						
						Mono.Unix.Native.Syscall.syslog(SyslogFacility.LOG_DAEMON, slevel, String.Format(fmt, parm));
					}
					else {
						System.Diagnostics.EventLog.WriteEntry(SyslogServiceName, String.Format(fmt, parm));
					}
				}
				
				if( LogToConsole && (!LogToSyslog || !IsLinux) )
					Console.WriteLine(fmt, parm);
			}
			
			if( AutoFlush )
				Flush();
		}
		
		public enum Level : byte {
			Important,
			Error,
			Warning,
			Info,
			Debug
		}
	}
}
