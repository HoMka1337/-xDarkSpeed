Grible Station

Server: Server\Start-Server.bat
Client: Client\Start-Client.bat

Start the server first, then open the client and connect to localhost.
Server settings: Server\grible-server.toml
Server saved data: Server\data

The client contains its own binaries and resources. The server uses the local PostgreSQL instance in Admin; keep Server and Admin together.
Requires the .NET 10 runtime installed on the target computer.
These are separate runnable copies. Source changes require rebuilding and updating these copies.
Admin panel: Admin\Configure-OAuth.bat, then Admin\Start-Admin.bat.
Local website: https://localhost:27689
