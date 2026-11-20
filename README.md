# Casualties: Together 5.5.4 + fixes

Unofficial build of the [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together) co-op mod for [Casualties: Unknown](https://store.steampowered.com/app/4576510) 1.0.2.
It is the **official 5.5.4, unchanged**, plus **CT Fixes**, a small plugin with a few multiplayer fixes. They are open upstream as pull requests and are not merged yet.

**[Download CasualtiesTogether-Setup.exe](https://github.com/Stefan7573/casualties-together/releases/latest)**

## What is fixed

| Bug (still in official 5.5.4) | Cause | Fix | Upstream PR |
|---|---|---|---|
| Only the host can repair a prosthesis. A client places the patches, then the repair rolls back. | The host accepts `prosthesis_patch` only if `ProsthesisRepair_RememberPatchCount.PatchCount` has the kit's patch count. When a client repairs, the host usually has no entry, so the patch is dropped and the next health sync puts the damage back. | The host looks up the patch count from the item itself when it validates the request. | [#60](https://github.com/creaturefeaturelarry/casualties-together/pull/60) |
| A client's prosthesis repair could still use the wrong patch count. | Sync ids are reused, so `PatchCount` can keep an entry from another item that had the same id before. | The host always looks the count up again for the item being repaired. | [#70](https://github.com/creaturefeaturelarry/casualties-together/pull/70) |
| The "don't scatter" rule counts the wrong players. With teams on, enemies standing next to you count as your group; players who just spawned count too. It could also throw a NullReferenceException. | The required group size is taken from a filtered list (your team, past the 20 s grace, loaded in), but nearby players were counted from everyone alive. | Nearby players are counted from the same filtered list; players without a body are skipped. | [#69](https://github.com/creaturefeaturelarry/casualties-together/pull/69) |

The high-ping fix from the 5.5.3 build ([#61](https://github.com/creaturefeaturelarry/casualties-together/pull/61), use ack) is now part of the official mod, so CT Fixes 1.1.0+ no longer includes it.

The prosthesis fix was checked in a real co-op session (host and client on different machines) on 5.5.2.
In 5.5.4 the host-side check is the same as in 5.5.2 and 5.5.3 (compared against the source and the official DLL).
The #69 and #70 fixes (CT Fixes 1.2.0) build and install cleanly but have not been tested in a game session yet.

## Compatible with the official build

CT Fixes does not change `CasualtiesMP.dll`, so the mod version stays 5.5.4:
- players with this build and players with plain 5.5.4 can play together;
- the prosthesis fixes only need the plugin on the **host**;
- the scatter rule is checked on each player's own machine, so that fix works for whoever has the plugin.

## Install

1. Download `CasualtiesTogether-Setup.exe` from [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Run it. The game folder is found through Steam; if not, pick it with Browse.
3. Press **Install** and start the game from Steam.

It adds BepInEx 5.4.23.5 if missing, installs official 5.5.4 into `BepInEx\plugins\Together`, CT Fixes into `BepInEx\plugins\CTFixes` and a Russian translation (`RU-r-full.json`).
Everything it replaces is copied to `BepInEx\CT-test-backup`; **Restore** puts the last backup back.
Windows may show a SmartScreen warning: the exe is not signed.

Manual install: put `CTFixes.dll` from the release into `BepInEx\plugins\CTFixes\` next to the official 5.5.4.
Do not use CT Fixes 1.0.0 (from the 5.5.3 build) with 5.5.4: its ping fix clashes with the built-in one.

## Установка

1. Скачай `CasualtiesTogether-Setup.exe` из [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Запусти. Папка игры находится через Steam, если нет, укажи её кнопкой «Обзор...».
3. Нажми «Установить» и запускай игру через Steam. Вернуть как было: кнопка «Восстановить».

С игроками на обычной 5.5.4 играть можно. Фикс протезов работает, если плагин стоит у хоста, фикс правила «не разбегаться» работает у того, у кого стоит плагин.

## Source

- `ct-fixes/` - the plugin (Harmony patches on top of the official `CasualtiesMP.dll`).
- `installer/` - the setup exe (put the payload files next to it, see `CTTestInstaller.csproj`).
- `fix/prosthesis-repair-clients`, `fix/patchcount-stale`, `fix/scatter-count` - the same fixes as source changes, as sent upstream.
- `fixes-5.5.3`, `fixes-5.5.2` - the previous builds.

This branch is based on upstream `main` at 5.5.4.

## Credits

The mod is made by [Creature](https://github.com/creaturefeaturelarry) and [Krokosha666](https://github.com/Krokosha666) and their contributors. This fork only adds the fixes above. License: LGPL-2.1, same as upstream.
