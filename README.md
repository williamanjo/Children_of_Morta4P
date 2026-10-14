# Morta4P

BepInEx plugin for Children of Morta that raises online co-op (Steam) from 2 to 4 players.

Status: **v0.1.0, experimental**. The lobby and character-select patches load, but no session with 3 or more players has been tested yet.

## What v0.1 does

| Patch | Purpose |
|---|---|
| `SteamInterface.CreateLobby` | sets the Steam lobby member limit to `MaxPlayers` (the shipped prefab already uses 4) |
| `LobbyManager.CheckLobbyIsFull` | accepts up to `MaxPlayers - 1` clients (original: 1) |
| `LobbyManager.CheckIsJoiningPossible` | transpiler that replaces the inlined `GetCurrentPlayersCount() < 2` |
| `CharacterSelect.GetPlayerNumber` / `GetIsPlayerLocal` | a client is the player number the host assigned, not always P2 |
| `CharacterSelect.CanInvite` | host can keep inviting while there is room |
| `CharacterSelect.CheckAllPlayersReady` | waits for every player to pick a character, not only P1 and P2 |
| `CharacterSelect.GetCharacterPanel` | P3/P4 use hidden panels (the UI has only 2); on a client the local player uses the second panel |

The camera needs no patch. Online, each PC already follows only its own local player (`LevelCameraManager` uses `PlayerManager.GetLocalPlayers()`).

## Install (every player)

1. Install BepInEx 5.4.23.x win x64 in the game folder (`G:\Steam\steamapps\common\ChildrenOfMorta`).
2. Copy `Morta4P.dll` to `BepInEx\plugins\Morta4P\`.
3. Start the game through Steam. The plugin lines appear in `BepInEx\LogOutput.log`.
4. Every player must use the same `MaxPlayers` value in `BepInEx\config\com.william.morta4p.cfg`.

## Build

```bash
dotnet build src/Morta4P/Morta4P.csproj -c Release
```

`GamePath` defaults to `G:\Steam\steamapps\common\ChildrenOfMorta` (override with `-p:GamePath=...`). After the build, the DLL is copied into the game's `BepInEx\plugins\Morta4P`.

## Testing a 3-4 player session

1. The host opens the home screen and invites friends through Steam, one at a time.
2. Note where it breaks: whether the join is rejected, whether character select works, whether the run starts, how the HUD looks.
3. Send `BepInEx\LogOutput.log` from both the host and the client that failed.

## v0.2: P3/P4 panels and partner HUDs

- **Character select:** `character_panels_` is extended to 4 entries with clones of the P1/P2 panels. With 3 or more players in the lobby, each column is split: P1 above P3 on the left, P2 above P4 on the right, all scaled by `CharacterSelect.PanelScale` (default 0.5). With 2 players the original layout is untouched.
- **Button markers:** each `CharacterButton` gets P3/P4 markers. The originals are images, not text, so P3 and P4 are tinted copies of the P2 marker (green and orange).
- **Egg / cure-fatigue widgets:** these only exist for P1/P2, so they are skipped for P3/P4.
- **Partner HUD:** `NetworkHUDManager` used a single `OnlinePlayerHUD` for the one remote player. Now each remote player gets its own HUD (clones of the original), stacked in `HUD.StackDirection` (Auto: down when the HUD is in the upper half of the screen).
- **Debug:** `F9` toggles a solo preview. In character select it shows the 4-panel layout; in a run it fills the partner HUD slots with your own character. `F10` reloads the config so the layout can be tuned live.

## Known gaps / next steps

- The P3/P4 markers on the character buttons are recolored P2 markers, not real "P3"/"P4" labels.
- The lobby status text (`MiniLobby`/`OnlinePlayerHUD.SetStatus`) has no sender information, so every remote status lands on the first partner HUD.
- `MiniLobby` (home screen widget) shows only the first remote player.
- `CharacterSelect` input handling still hard-codes P2 for clients in one spot (cancel button).
- In-run systems (revive, scaling, cutscenes) have not been checked for 4 players.
