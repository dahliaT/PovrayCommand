# PovrayCommand
TestClient addon module for creating PovRay input files for rendering map tiles for OpenSimulator

This is an extension command for TestClient.exe. To use it, first add the project to the
libopenmetaverse solution and build it. It should put a dll into libopenmetaverse/bin. Go to
that folder and invoke TestClient.exe, logging in your bot to the region you want to make
a map tile for. Once logged in, type:

load PovrayCommand

This should load the dll. To create the povray input file type:

dpovray

This will create a file "sim.pov" in the current diretory. This file can be used as input to Povray to create
your map tile.

Note: the current versions of PovRay are licensed as AGPL. If this is not acceptable, consider using an older version.


