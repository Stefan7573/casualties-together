# Casualties: Together 5.5.3 + fixes

Unofficial build of the [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together) co-op mod for [Casualties: Unknown](https://store.steampowered.com/app/4576510) 1.0.2.
It is the **official 5.5.3, unchanged**, plus **CT Fixes**, a small plugin with two sync fixes that are open upstream as pull requests and still not merged.

**[Download CasualtiesTogether-Setup.exe](https://github.com/Stefan7573/casualties-together/releases/latest)**

## What is fixed

| Bug (still in official 5.5.3) | Cause | Fix | Upstream PR |
|---|---|---|---|
| Only the host can repair a prosthesis. A client places the patches, then the repair rolls back. | The host accepts `prosthesis_patch` only if it knows the kit's patch count, and that is recorded only on the machine that opened the minigame, so a client's patches were always dropped. | The host looks up the patch count from the item itself when it validates the request. | [#60](https://github.com/creaturefeaturelarry/casualties-together/pull/60) |
| On high or jittery ping, using items and objects (food, bandages, doors, drill pod, scrap eater) rolls back or goes through twice. | The client ignores host snapshots for a guessed window (0.3 s + RTT). When the host's answer comes later, the stale snapshot wins. | The host acks every handled use request; the client keeps its predicted state until the ack (timeout 1.5 s + 2x RTT, max 7.5 s). | [#61](https://github.com/creaturefeaturelarry/casualties-together/pull/61) |

Both fixes were checked in a real co-op session (host and client on different machines, client on 300-600 ms artificial lag) on 5.5.2.
In 5.5.3 the code of both bugs is byte-for-byte the same as in 5.5.2 (compared by decompiling the official DLL).

## Compatible with the official build

CT Fixes does not change `CasualtiesMP.dll`, so the mod version stays 5.5.3:
- players with this build and players with plain 5.5.3 can play together;
- the prosthesis fix only needs the plugin on the **host**;
- the ping fix needs it on the **host and the client**; with a host without it the client behaves exactly like 5.5.3.

## Install

1. Download `CasualtiesTogether-Setup.exe` from [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Run it. The game folder is found through Steam; if not, pick it with Browse.
3. Press **Install** and start the game from Steam.

It adds BepInEx 5.4.23.5 if missing, installs official 5.5.3 into `BepInEx\plugins\Together`, CT Fixes into `BepInEx\plugins\CTFixes` and a Russian translation (`RU-r-full.json`).
Everything it replaces is copied to `BepInEx\CT-test-backup`; **Restore** puts the last backup back.
Windows may show a SmartScreen warning: the exe is not signed.

Manual install: put `CTFixes.dll` from the release into `BepInEx\plugins\CTFixes\` next to the official 5.5.3.

## Установка

1. Скачай `CasualtiesTogether-Setup.exe` из [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Запусти. Папка игры находится через Steam, если нет, укажи её кнопкой «Обзор...».
3. Нажми «Установить» и запускай игру через Steam. Вернуть как было: кнопка «Восстановить».

С игроками на обычной 5.5.3 играть можно. Фикс протезов работает, если плагин стоит у хоста; фикс пинга работает, если он стоит и у хоста, и у клиента.

## Source

- `ct-fixes/` - the plugin (Harmony patches on top of the official `CasualtiesMP.dll`).
- `installer/` - the setup exe (put the payload files next to it, see `CTTestInstaller.csproj`).
- `fix/prosthesis-repair-clients`, `fix/use-ack-high-ping` - the same fixes as source changes, as sent upstream (they apply cleanly to current `main`).
- `fixes-5.5.2` - the previous build: full 5.5.2 source with both fixes.

The upstream source on `main` has not been synced to 5.5.3 yet, which is why this build ships the official 5.5.3 binary plus a plugin instead of a rebuilt mod.

## Credits

The mod is made by [Creature](https://github.com/creaturefeaturelarry) and [Krokosha666](https://github.com/Krokosha666) and their contributors. This fork only adds the fixes above. License: LGPL-2.1, same as upstream.
