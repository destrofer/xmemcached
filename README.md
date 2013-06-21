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

Currently compilation is only available in windows (no makefile scripts yet) using
\#develop IDE (http://www.icsharpcode.net/). To compile xmemcached you need to create
your own solution and add this project to it. Then just press F9 or use
menu command Build->Build xmemcached. After that binaries will be available
in `bin/Debug` or `bin/Release` directory.

Initially project was created using mono version 2.10.9. As such references in the project
point to `C:\Program Files\Mono-2.10.9\lib\mono\4.0\*`. If you have a different version
of mono installed then you will most probably have to remove and readd references in the IDE
or, while IDE is closed, manually, using notepad, edit `xmemcached.csproj` and replace all
`C:\Program Files\Mono-2.10.9\lib\mono\4.0\` with the path to wherever your mono installation
currently is.

Installation
============

Windows
-------

To install the service on windows copy `xmemcached.exe` and `Mono.Posix.dll` from compilation
output directory to your desired location (for example `C:\Services\xmemcached\`).
Then run this command from the console:
	
	c:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /LogToConsole=true C:\Services\xmemcached\xmemcached.exe
	
Note that path to the executable must be full when running that command.
	
After that xmemcached will be installed in your system as a service, but it will not be running.
You must create a configuration file before starting the service.

Linux
-----

Copy compiled `xmemcached.exe` to `/bin/xmemcached` (don't forget to remove extension) and create
a shell script file `/etc/init.d/xmemcached` with following contents:
	
	#!/bin/bash
	### BEGIN INIT INFO
	# Provides:            xmemcached
	# Required-Start:      $syslog
	# Required-Stop:       $syslog
	# Should-Start:        $local_fs
	# Should-Stop:         $local_fs
	# Default-Start:       2 3 4 5
	# Default-Stop:                0 1 6
	# Short-Description:   Start xmemcached daemon
	# Description:         Start xmemcached daemon
	### END INIT INFO 
	
	SERVICE_PATH="/bin"
	SERVICE_NAME="xmemcached"
	export MONO_OPTIONS=--runtime=v4.0.30319
	export LD_LIBRARY_PATH=.:/usr/local/mono/2.8/lib/:$LD_LIBRARY_PATH
	
	case "$1" in
		start)
			echo "Starting xmemcached"
			mono-service2 -d:$SERVICE_PATH -l:/var/lock/$SERVICE_NAME.lock -m:$SERVICE_NAME $SERVICE_PATH/$SERVICE_NAME
			;;
			
		debug)
			echo "Starting xmemcached"
			mono-service2 -d:$SERVICE_PATH -l:/var/lock/$SERVICE_NAME.lock -m:$SERVICE_NAME --debug $SERVICE_PATH/$SERVICE_NAME
			;;
			
		stop)
			echo "Stopping xmemcached"
			kill `cat /var/lock/$SERVICE_NAME.lock`
			rm /var/lock/$SERVICE_NAME.lock
			;;
		
		restart)
			echo "Stopping xmemcached"
			kill `cat /var/lock/$SERVICE_NAME.lock`
			rm /var/lock/$SERVICE_NAME.lock
			
			echo "Starting xmemcached"
			mono-service2 -d:$SERVICE_PATH -l:/var/lock/$SERVICE_NAME.lock -m:$SERVICE_NAME $SERVICE_PATH/$SERVICE_NAME
			;;
		
		force-restart)
			echo "Forcing xmemcached to stop"
			kill -s SIGKILL `cat /var/lock/$SERVICE_NAME.lock`
			rm /var/lock/$SERVICE_NAME.lock
			
			echo "Starting xmemcached"
			mono-service2 -d:$SERVICE_PATH -l:/var/lock/$SERVICE_NAME.lock -m:$SERVICE_NAME $SERVICE_PATH/$SERVICE_NAME
			;;
		
		status)
			echo "Writing xmemcached status to the log file"
			kill -s SIGUSR1 `cat /var/lock/$SERVICE_NAME.lock`
			;;
		
		*)
			echo "Usage: /etc/init.d/xmemcached {start|stop|restart|force-restart|status}"
			exit 1
			;;
	esac
	
	exit 0

While editing you might want to change `LD_LIBRARY_PATH` variable to match it to your current mono version.
After script was created execute following commands:

	chmod +x /bin/xmemcached
	chmod +x /etc/init.d/xmemcached
	mkdir /etc/xmemcached
	update-rc.d xmemcached defaults 20 80

Before starting the service you must create a configuration file (see next section).

This installation instruction applies to Debian and Ubuntu linux distributions and may differ
for other distributions.

Configuring
===========

The sample contents of a configuration file `xmemcached.conf`:

	# Change server_id only if there is more than one server in the network.
	# It must be unique and not negative 32 bit integer (0 - 2000000000).
	server_id = 0
	
	# Comma or whitespace separated list of addresses to bind tcp listeners to.
	# Address entry may be {*|localhost|ip}[:port]
	# Samples:
	#    bind_addr = *
	#    bind_addr = localhost 192.168.1.1
	#    bind_addr = localhost:11211; 192.168.1.1:11211
	bind_addr = localhost; 192.168.1.90
	
	# Comma or whitespace separated list of allowed client ip addresses
	allowed_addr = localhost; 192.168.1.14; 192.168.1.90
	
	# Ip and port of next server in the loop chain of xmemcached servers.
	# Uncomment next_server_addr ONLY when there is more than one xmemcached server!
	# For normal functionality all servers must be linked in a chain loop.
	# It means that next_server_addr setting in the last server must be ip and port
	# of the first server.
	# This is needed for "flush_all" and "delete" commands to execute on all
	# xmemcached servers.
	#next_server_addr = 127.0.0.1:11212
	
	max_storage = 16M
	
	log=/var/log/xmemcached.log 

Currently service loads the config file only from `C:\xmemcached.conf` in windows
or `/etc/xmemcached/xmemcached.conf` in linux.
To change the path to your config file you should modify `Config.cs`

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

Assuming you have installed the service to `C:\Services\xmemcached\` you have
to run following commands to uninstall the service:

	net stop xmemcached
	c:\Windows\Microsoft.NET\Framework\v4.0.30319\InstallUtil.exe /Uninstall /LogToConsole=true C:\Services\xmemcached\xmemcached.exe

Linux
-----

Execute these commands to uninstall the service:

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
