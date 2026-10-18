Grible Station Server

Start: Start-Server.bat
Settings: grible-server.toml
Database: local PostgreSQL in ..\Admin (started automatically).

Keep bin\Content.Server\Content.Client.zip with the server.
It is the client download package for the SS14 launcher, not a client launcher.
The server can distribute it without depending on the separate Client folder.

When updating game code or resources, rebuild the server and the client download package together.

Lobby, saved characters and automatic map selection are configured in grible-server.toml:

[game]
lobbyenabled = true
map = ""
map_pool = "SunriseMapPool"
map_rotation = true

[ic]
random_characters = false

An empty map value clears the forced development map. Rotation uses maps eligible for the current player count.
Players use the character selected and saved in lobby customization.