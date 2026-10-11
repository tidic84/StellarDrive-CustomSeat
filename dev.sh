#!/usr/bin/env bash
# Dev loop: build with DEV, deploy to the game's Mods folder, start the game on a copy of a test world and
# wait for the scripted test (src/Dev/DevTest.cs) to finish. KS_PHASE=2 reopens the world saved by the
# previous run instead of a fresh copy (checks that the seat kept its keys).
set -e
export DOTNET_ROOT="$HOME/.dotnet" PATH="$HOME/.dotnet:$PATH"
GAME="/c/Program Files (x86)/Steam/steamapps/common/StellarDrive Demo"
LOG="$GAME/MelonLoader/Latest.log"
TIMEOUT="${1:-150}"
cd "$(dirname "$0")"

taskkill //IM StellarDrive.exe //F > /dev/null 2>&1 || true
dotnet build src/KeySeat.csproj -c Release -p:DefineConstants=DEV -nologo -v q 2>&1 | grep -E "error|rror CS" && exit 1
sleep 2
cp src/bin/Release/KeySeat.dll "$GAME/Mods/"
W="/c/Users/tidic/AppData/LocalLow/CuriousOwlGames/StellarDrive/WorldSaves"
if [ "$KS_PHASE" != "2" ]; then rm -rf "$W/KS_Test" && mkdir -p "$W/KS_Test" && cp "$W/WB_Dump/world.json" "$W/WB_Dump/world.jpg" "$W/KS_Test/"; fi
rm -f "$LOG"
(cd "$GAME" && ./StellarDrive.exe -screen-fullscreen 0 -screen-width 1280 -screen-height 720 > /dev/null 2>&1 &)
for i in $(seq 1 "$TIMEOUT"); do
  if [ -f "$LOG" ] && grep -qE "test done|\[KeySeat\].*(Exception|failed)" "$LOG"; then break; fi
  sleep 1
done
grep -E "\[KeySeat\]|Exception|ERROR" "$LOG" | tail -40
