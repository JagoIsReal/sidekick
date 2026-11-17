# Sidekick

A compact Dalamud toolkit for Final Fantasy XIV, with chat and roleplay utilities.

## Features

- **Chat mentions:** Color messages that mention your character's first or last name, or your saved keywords. Automatic name detection ignores your own messages. Keywords can apply to one character or all characters. Works with the native chat and Chat 2.
- **Focus:** Keep nearby or selected players readable while dimming other messages. Choose the channels, range in yalms, participants, and opacity.
- **Poses:** Switch directly to a numbered `/cpose` for your current posture.
- **Glamourer rules:** Apply saved outfits based on weather, online status, Eorzea day or night, flying, swimming, diving, mounts, or locations. Restore the previous appearance when a rule ends, while preserving manual changes.
- **Penumbra rules:** Enable, disable, or toggle mods using the same conditions, with a target collection and configurable Redraw. Optional early application attempts to prepare outdoor housing mods during loading; already loaded resources cannot be refreshed this way.

Both rule modules support Account-wide rules and character profiles identified by full name and home world. Character rules take priority over Account rules.

Select multiple weathers or online statuses in a rule. Deleting rules and resetting settings require **Ctrl + click**.

English and French interfaces are available in **Settings**. Chat appearance changes apply to new messages.

## Commands

| Command | Action |
| --- | --- |
| `/sidekick` or `/sk` | Open Sidekick. |
| `/sk focus` | Toggle Focus. |
| `/sk focus on` | Enable Focus. |
| `/sk focus off` | Disable Focus. |
| `/sk cpose N` | Select pose N, starting at 1. |

The **Commands** tab contains the full command reference. `/sidekick` accepts the same arguments as `/sk`.

## Installation

Requires Dalamud. Add this URL in **Dalamud Settings → Experimental → Custom Plugin Repositories**, then install Sidekick from `/xlplugins`:

```text
https://github.com/JagoIsReal/sidekick/releases/latest/download/repo.json
```

Chat 2 is optional. Glamourer and Penumbra are required only for their respective rule modules.
