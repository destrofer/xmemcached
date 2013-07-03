Introduction
============

Xmemcached is a fast memory caching service very similar to well known memcached
daemon (http://github.com/memcached/memcached). It works using the same protocol. Xmemcached
does not have all the functionality of the memcached, however it has enough to fully work with
PHP memcache/memcached extensions, and it has these additional features:

* it supports tagging
* it may have a server chain

Tagging
=======

Tagging in our case means the ability to have groups of keys. For example you can
send command to the service to add an item with the key `contact.2344:contacts`
and as a result the item will have the key `contact.2344` and added to the group `contacts`.
This does not sound like much at first sight, however when you have a very big website or
application with lots of different types of cacheable data, you would hate to clear the
whole cache when you could just remove a portion of it. In our case you could remove 
the items by:

* using the item key `contact.2344` just normally, like you would do with memcached
* using the tag name `:contacts`, which would remove all keys that are in the `contacts` group
* flushing the whole cache

By default to separate tag name from the key colon symbol (":") is used, but it may be changed
by writing a different character in tag_character configuration option.

Server chain
============

The server chain thingy is that deletion and flushing commands are delegated to the next server in
the chain. This helps with the problem when PHP memcache/memcached extensions tend to execute
the deletion commands only on one server of the given list as a result leaving the outdated data
on other servers. To use the deletion command delegation you must assign each server a unique id
and setting the addresses of the next server in the chain. It can be done by changing `server_id`
and `next_server_addr` parameters in server configs. To make a proper chain each server must have
ip and optionally port of the next server in the chain set. The last server in the chain MUST point
to the first server as the next server or otherwise chaining will not work properly.

For example we have 3 servers running xmemcached with these ips:

* 192.168.1.2
* 192.168.1.3
* 192.168.1.4

Then in their configuration files we would set parameters:

for the first server

	server_id = 0
	next_server_addr = 192.168.1.3:11211

for the second server

	server_id = 1
	next_server_addr = 192.168.1.4:11211

for the third and last server

	server_id = 2
	next_server_addr = 192.168.1.2:11211

This way deletion and flushing commands would make a whole circle in the chain and stop at the
server that issued the command.

Prerequisites
=============

* mono 2.8.* or later (http://www.mono-project.com/)
* Microsoft .NET Framework v4.0.30319 (on windows platform)

Compiling
=========

There are two coices how to compile the service: using GNU make or via IDE

To compile xmemcached using GNU make, all you have to do is execute command `make` in the
console (assuming you have PATH variable set in windows).

To compile xmemcached in IDE you need to create your own solution and add this project to it.
Then just use build command. After that binaries will be available in `bin` directory.

Initially project was created using mono version 2.10.9 in windows. As such references in the
project file and makefile point to `C:\Program Files\Mono-2.10.9\lib\mono\4.0\*`. If you have
a different version of mono installed then you will most probably have to remove and readd
references in the IDE or, while IDE is closed, manually, using notepad, edit `xmemcached.csproj`
and replace all `C:\Program Files\Mono-2.10.9\lib\mono\4.0\` with the path to wherever your mono
installation currently is. Similarily, if you use makefile to compile, you have to edit it manually.

Installation
============

Windows
-------

You can install service using GNU make by executing command `make install`, but don't forget to edit `conf/xmemcached.conf` beforehand.

To install the service on windows manually you have to copy `bin/xmemcached.exe`, `Mono.Posix.dll` and
`conf/xmemcached.conf` to your desired location (for example `C:\Services\`).
Then execute following commands from the console:
	
	c:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /LogToConsole=true C:\Services\xmemcached.exe
	net start xmemcached
	
Note that path to the executable must be full when running that command.

Before installing you might want to edit configuration file `conf/xmemcached.conf` and
change `LD_LIBRARY_PATH` variable in `scripts/xmemcached` to match it your current mono version.

Either if you install it using GNU make or manually you have to execute commands as an administrator.

Before installing you might want to edit configuration file `conf/xmemcached.conf`.
The service will start up after installation so configure it correctly before installing.

Linux
-----

You can install service using GNU make by executing command `make install`, but don't forget to edit `conf/xmemcached.conf` beforehand.

To install service on linux manually you have to execute commands:

	mkdir /etc/xmemcached
	cp bin/xmemcached.exe /bin/xmemcached.exe
	cp conf/xmemcached.conf /etc/xmemcached/xmemcached.conf
	cp scripts/xmemcached /etc/init.d/xmemcached
	chmod +x /bin/xmemcached.exe
	chmod +x /etc/init.d/xmemcached
	update-rc.d xmemcached defaults 20 80
	/etc/init.d/xmemcached start

Before installing you might want to edit configuration file `conf/xmemcached.conf` and
change `LD_LIBRARY_PATH` variable in `scripts/xmemcached` to match it your current mono version.
The service will start up after installation so configure it correctly before installing.

This installation instruction applies to Debian and Ubuntu linux distributions and may differ
for other distributions.

Configuring
===========

The sample contents of a configuration file are located in `conf/xmemcached.conf` that contains
configuration that listens and accepts connections on localhost only.

By default service loads the config file from:
* `./xmemcached.conf` in windows,
* `/etc/xmemcached/xmemcached.conf` in linux.

Running and stopping
====================

Windows
-------

You can start and stop the service via control panel or by running
`net start xmemcached` / `net stop xmemcached` commands.

Linux
-----

You must execute command `/etc/init.d/xmemcached start` to start the service or
`/etc/init.d/xmemcached stop` to stop the service. The script also supports
commands `debug` (starts xmemcached so that it outputs it's log to the console),
`restart` (restarts the service), `force-restart` (kills previous service and
starts it again) and `status` (dumps current status infromation to the log).

Uninstalling
============

Windows
-------

You can uninstall the service using GNU make by executing the command `make uninstall`.

To manually uninstall the service you have to run following commands as an administrator (assuming you have installed
the service to `C:\Services\`):

	net stop xmemcached
	c:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /Uninstall /LogToConsole=true C:\Services\xmemcached.exe

Either if you uninstall it using GNU make or manually you have to execute commands as an administrator.
	
Linux
-----

You can uninstall the service using GNU make by executing the command `make uninstall`.

Execute following commands to manually uninstall the service:

	/etc/init.d/xmemcached stop
	rm /etx/init.d/xmemcached
	update-rc.d xmemcached remove

Issues
======

There is a known issue when attacker can have a direct connection to the service
while the service is a part of a chain. Attacker may issue a flushing command that
would go in an infinite loop in the server chain as such rending the service useless.
To prevent that you should configure the service to accept only local connections and
connections from the previous server in the server chain.

License
=======

Copyright 2012-2013 Viacheslav Soroka

xmemcached is free software: you can redistribute it and/or modify
it under the terms of the GNU Lesser General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

xmemcached is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU Lesser General Public License for more details.

You should have received a copy of the GNU Lesser General Public License
along with xmemcached.  If not, see <http://www.gnu.org/licenses/>.
