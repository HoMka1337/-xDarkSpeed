@echo off
cd /d "%~dp0"
echo Stop the game server and admin panel before stopping the database.
"%~dp0PostgreSQL\pgsql\bin\pg_ctl.exe" stop -D "%~dp0private\pgdata" -m fast -w
pause
