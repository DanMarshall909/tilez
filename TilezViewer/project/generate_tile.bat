@echo off
REM Generate a tile with Tilez
REM Usage: generate_tile.bat <x> <y> <seed>

cd /d "C:\code\tilez"
dotnet run --project Tilez/Tilez.csproj -- render --image-backend openai --ai-base-url http://localhost:1234 --ai-model flux.1-schnell --x %1 --y %2 --zoom 3.0 --seed %3
