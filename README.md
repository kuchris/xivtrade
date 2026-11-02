# xivtrade

A small Dalamud plugin for sending and receiving gil.

- **Send:** select a player, enter the total, and press **Send gil**. Totals over 1,000,000 gil are split across consecutive trades. The recipient stays pinned even if you change targets.
- **Receive:** press **Start receiving** to automatically accept pure gil trades from any player. Keep receiving until you press **Stop** or your wallet is full. The sender initiates each trade and locks their offer first.
- **Stop:** stops automation and cancels the open trade. Completed trades remain in the session totals.

Trades containing items, unexpected outgoing gil in receive mode, changed offers, or a different recipient stop automation. Progress advances only after the trade window closes and the exact expected wallet change is observed. Canceled or unverified trades do not count as completed.

## Commands

| Command | Action |
| --- | --- |
| `/xivtrade` | Open the window |
| `/xivtrade send 2500000` | Send 2,500,000 gil to the selected player |
| `/xivtrade receive` | Receive gil from any player until stopped |
| `/xivtrade stop` | Stop automation |

`/giltrade` remains an alias, including `/giltrade <amount>`.

## Development install

Requires Dalamud API 15. Disable Gillionaire before loading xivtrade: both register `/giltrade`.

Build the plugin, add the absolute path to `xivtrade/bin/x64/Release/xivtrade.dll` under `/xlsettings` → **Experimental** → **Dev Plugin Locations**, then enable **xivtrade** under `/xlplugins` → **Dev Tools**.

## Build and verify

Requires Windows, .NET 10, and the matching Dalamud development assemblies at `%APPDATA%/XIVLauncher/addon/Hooks/dev` (or set `DALAMUD_HOME`). There are no runtime dependencies beyond Dalamud.

```powershell
./build.ps1
dotnet run --project tests/xivtrade.Checks.csproj --configuration Release
```

The build script restores locked dependencies and produces `artifacts/xivtrade-1.0.0.0.zip` with a SHA256 checksum. The ZIP contains the DLL, manifest, dependency metadata, license, and attribution notice.

Offline checks cover session behavior. Native trade requests, agent events, and ownership of confirmation dialogs still need verification inside the matching live game build. Only confirmation dialogs linked to the Trade addon are eligible for automatic acceptance; an unrecognized dialog pauses the trade until it is closed or the session times out.

## Project history

xivtrade remakes [Gillionaire](https://github.com/voidstar0/Gillionaire), originally by voidstar0 and later maintained by kuchris. The trade loop and UI have been rewritten, and ECommons is no longer required. See [NOTICE.md](NOTICE.md) for attribution. The project retains the earlier declared AGPL-3.0-or-later license.
