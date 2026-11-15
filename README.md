# Sidekick

A compact Dalamud toolkit for Final Fantasy XIV, with chat and roleplay utilities.

## Features

- **Chat mentions:** Color messages that mention your character's first or last name, or your saved keywords. Automatic name detection ignores your own messages. Keywords can apply to one character or all characters. Works with the native chat and Chat 2.
- **Focus:** Keep nearby or selected players readable while dimming other messages. Choose the channels, range in yalms, participants, and opacity.
- **Poses:** Switch directly to a numbered `/cpose` for your current posture.
- **Penumbra rules:** Enable, disable, or toggle mods based on your location, with a target collection and configurable Redraw.

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

Requires Dalamud. After the first release is published, add this URL in **Dalamud Settings → Experimental → Custom Plugin Repositories**, then install Sidekick from `/xlplugins`:

```text
https://github.com/JagoIsReal/sidekick/releases/latest/download/repo.json
```

Chat 2 is optional. Penumbra is only required for location rules.
