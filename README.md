# Casualties: Together 5.5.2 + fixes

Unofficial build of the [Casualties: Together](https://github.com/creaturefeaturelarry/casualties-together) co-op mod for [Casualties: Unknown](https://store.steampowered.com/app/4576510) 1.0.2.
It is the official 5.5.2 plus two sync fixes that are open as pull requests upstream and not merged yet.

**[Download CasualtiesTogether-Setup.exe](https://github.com/Stefan7573/casualties-together/releases/latest)**

## What is fixed

| Bug in 5.5.2 | Cause | Fix | Upstream PR |
|---|---|---|---|
| Only the host can repair a prosthesis. For clients the repair minigame finishes and nothing happens. | The number of holes to patch was known only on the host, so the client's request was dropped. | The client sends the hole count with the request; the host validates it (1..64) and applies the repair. | [#60](https://github.com/creaturefeaturelarry/casualties-together/pull/60) |
| On high ping, using items and objects (bandages, drill pod, containers, scrap eater) rolls back or does nothing. | The client ignored host state for a fixed time based on RTT. With a slow connection the old state arrived after that window and overwrote the action. | The host acknowledges every handled use request. The client keeps its predicted state until the ack arrives (timeout 1.5 s + 2x RTT, max 7.5 s). | [#61](https://github.com/creaturefeaturelarry/casualties-together/pull/61) |

Both fixes were checked in a real co-op session, host and client on different machines.

## Install

1. Download `CasualtiesTogether-Setup.exe` from [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Run it. The game folder is found through Steam; if not, pick it with the Browse button.
3. Press Install and start the game from Steam.

The installer adds BepInEx 5.4.23.5 if it is missing and copies the old mod files to `BepInEx\CT-test-backup`.
Windows may show a SmartScreen warning, the exe is not signed.

## Установка

1. Скачай `CasualtiesTogether-Setup.exe` из [Releases](https://github.com/Stefan7573/casualties-together/releases/latest).
2. Запусти. Папка игры находится через Steam, если нет, укажи её кнопкой «Обзор...».
3. Нажми «Установить» и запускай игру через Steam.

## Notes

- Every player in the lobby needs this same build. It does not connect to the official 5.5.2.
- The mod's auto-updater is turned off, otherwise it would replace this build with the official one.
- The installer also adds a Russian translation (`RU-r-full.json`), selectable in the game's language settings.
- To go back, install the official release from the [upstream repository](https://github.com/creaturefeaturelarry/casualties-together/releases).

## Source

- `fixes` (this branch): official 5.5.2 with both fixes, plus the installer source in `installer/`.
- `fix/prosthesis-repair-clients`, `fix/use-ack-high-ping`: the two fixes separately, as sent upstream.

Building: run `scripts/prepare_workspace.py`, then open `CasualtiesTogether.sln` in Visual Studio 2022.

## Credits

The mod is made by [Creature](https://github.com/creaturefeaturelarry) and [Krokosha666](https://github.com/Krokosha666) and their contributors. This fork only adds the fixes above. License: LGPL-2.1, same as upstream.
