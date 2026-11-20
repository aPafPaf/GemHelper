# GemHelper

ExileApi (Path of Exile HUD) plugin: shows gems in your equipped item sockets, tracks gem experience,
and automates **Unequip** (drop all gems of an item into the inventory) and **Swap** (replace a socketed
gem with the best non-`MAX` gem from the inventory). Both actions are started manually from the table.

## Features

- Socket table grouped by item: slot, color, link group, gem, level, quality, required level, exp (`%`/`MAX`).
- `Unequip` button per item, `Swap` button per socket.
- Inventory panel overlay: item/socket frames, link lines, gem text, gold highlight for max-level gems.
- Reads gem experience with runtime calibration against the regular gem XP table.
- Works with the second weapon set (`Weapon2`/`Offhand2`): presses the weapon swap key (`X`), unequips,
  then switches the set back.

## Install

1. Copy the `GemHelper` folder to `<HUD root>\Plugins\Source\`.
2. Restart the HUD (it compiles the plugin into `Plugins\Temp\GemHelper`).
3. Enable **GemHelper** in the plugin list — `Enable` is off by default.


Requires ExileApi **330.5** and .NET SDK 10.

## Usage

1. Open the character/inventory panel in game.
2. `Swap` — right-clicks the socket, picks the highest-level non-`MAX` gem from the inventory, inserts it.
3. `Unequip` — right-clicks each socket and drops the gems into free inventory cells.

## Settings worth knowing

| Setting | Default | Purpose |
|---|---|---|
| `Enable` | off | Master switch |
| `ShowDebugSnapshot` | on | Socket table window |
| `ShowOverlay` | off | Inventory panel overlay |
| `SwapTiming.HideOverlayDuringOperations` | on | Hide the HUD overlay so clicks reach the game |
| `SwapTiming.StepDelayMs` | 10 | Pause between operation steps |
| `SwapTiming.TimeoutMs` | 2000 | Operation timeout (includes waiting for focus) |
| `SwapTiming.CompletionTimeoutMs` | 400 | Max wait for the cursor to become empty |
| `SwapTiming.MouseMoveDurationMs` | 50 | Cursor travel time (`0` = instant) |
| `SwapTiming.WeaponSwapKey` | `X` | In-game weapon set swap key |
| `OverlayTextOffsetX` / `Y` | 0 | Move the overlay text block |

`TextOffsets` adjusts text position per slot; `OverlayDebugSlots` labels detected slots.

## Notes

- The plugin clicks through `ExileCore.Input`, so the game window must be focused and the inventory open;
  the HUD overlay is hidden during operations and restored afterwards.

<div align="center">

```
    █████╗ ███╗   ███╗███╗   ██╗██╗   ██╗ █████╗ ███╗   ███╗
   ██╔══██╗████╗ ████║████╗  ██║╚██╗ ██╔╝██╔══██╗████╗ ████║
   ███████║██╔████╔██║██╔██╗ ██║ ╚████╔╝ ███████║██╔████╔██║
   ██╔══██║██║╚██╔╝██║██║╚██╗██║  ╚██╔╝  ██╔══██║██║╚██╔╝██║
   ██║  ██║██║ ╚═╝ ██║██║ ╚████║   ██║   ██║  ██║██║ ╚═╝ ██║
   ╚═╝  ╚═╝╚═╝     ╚═╝╚═╝  ╚═══╝   ╚═╝   ╚═╝  ╚═╝╚═╝     ╚═╝
```

</div>
