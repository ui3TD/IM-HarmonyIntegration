# Making Idol Manager mods with an AI coding assistant

Copy each message below into an AI coding assistant. It does the work. Windows only.

## Before you start

- Install Idol Manager.
- Install an AI coding assistant, such as Claude Code, Codex, Cursor or GitHub Copilot.
- Make a GitHub account.
- Make an empty folder, and open the assistant in it.

## Set up (once)

### Step 1: Install the tools

```
I want to make Harmony mods for Idol Manager. Install what I need: Git, the GitHub CLI, the .NET SDK,
Python, ilspycmd, UnityPy and SteamCMD.

Mods are loaded by IM-HarmonyIntegration (https://github.com/ui3TD/IM-HarmonyIntegration). If it isn't
in my game folder, install it as its README says: extract the latest release zip into the game folder.
Don't build it from source or download BepInEx or Harmony: the zip has them. Build my mods against the
copies in my game folder.
```

### Step 2: Install and test the template

```
Clone https://github.com/ui3TD/IM-FastForward and https://github.com/ui3TD/IM-InGameTests into this
folder, and follow IM-FastForward's README until the mod builds and its unit and in-game tests pass.
```

Keep Steam open. The game opens and closes by itself while the assistant tests.

### Step 3: Let the assistant read the game's code and assets

```
Make a private folder for the game's files, outside this folder and outside OneDrive or any other
cloud sync. Move the game DLLs there and set IDOL_MANAGER_GAME_DLLS to them, then check that
IM-FastForward still builds. Decompile the game's code into it with ilspycmd. Extract the game's
images and text into it with UnityPy, with an index file you can search: export atlas sprites from
the atlas render data and skip the low-quality _LQ atlases. Never commit or publish anything from
that folder.
```

### Step 4: Log in to GitHub and Steam

```
Log me in to the GitHub CLI and to SteamCMD, so you can push my mods and publish them to the Steam
Workshop. Give me the commands and let me type my own passwords. Set Git to commit with my GitHub
noreply email address, so my real email stays private.
```

### Step 5: Give the assistant its rules

```
Write an instructions file in this folder that you read at the start of every session (for example
CLAUDE.md or AGENTS.md). Say what this workspace is, where the decompiled code and extracted assets
are, that each new mod starts as a copy of IM-FastForward, how to build and test a mod, and that
SteamCMD can download Workshop mods to check other mods' code. Rules: after changing a mod, build it
and run its tests; never commit or publish the game's files; never push to GitHub or publish to Steam
unless I ask; show me a plan before any big change.
```

## Make a mod

### Step 6: Describe your mod

Replace the example with your own idea.

```
Make a new mod called Gentle Shows where idols lose half as much stamina from theater shows. Start
from a copy of IM-FastForward in a new folder with a new git repository, following its "Make your own
mod" section. Find how the game does it first, and replace FastForward's code and tests.
```

### Step 7: Try it in the game

1. Start the game.
2. Turn the mod on in the Mods menu.
3. Load a save.

## Publish on the Steam Workshop

### Step 8: Upload it

```
Put Gentle Shows on GitHub, and publish it to the Steam Workshop as a new item with its
tools/publish_workshop.py.
```

### Step 9: Make it public

On your mod's Workshop page:

1. Add tags.
2. Under Add/Remove Required Items, add [Harmony Checker](https://steamcommunity.com/sharedfiles/filedetails/?id=3102983863).
3. Make it public.

### Updating a mod

```
Publish the update of Gentle Shows to the Steam Workshop.
```
