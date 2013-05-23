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
using System.Collections.Generic;

namespace xmemcached
{
	/// <summary>
	/// Description of CacheItem.
	/// </summary>
	public class CacheItem {
		public string[] Tags;
		public byte[] Data;
		public DateTime Expire;
		public ulong LastChangeId;
		public uint CustomBits;
		public DateTime LastUse;
		
		public CacheItem(string[] tags, uint customBits, byte[] data, DateTime expire, ulong lcid) {
			Tags = tags;
			CustomBits = customBits;
			Data = data;
			Expire = expire;
			LastChangeId = lcid;
			LastUse = DateTime.Now;
		}
	}
}
