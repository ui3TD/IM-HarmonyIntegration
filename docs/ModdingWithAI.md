# Making Idol Manager mods with an AI coding assistant

Copy each message below into an AI coding assistant. It does the work. Windows only.

## Before you start

- Install Idol Manager.
- Install an AI coding assistant, such as Claude Code, Codex, Cursor or GitHub Copilot.
- Make an empty folder, and open the assistant in it.

## Set up (once)

### Step 1: Install the tools

```
I want to make Harmony mods for Idol Manager. Install what I need: Git, the .NET SDK, Python,
ilspycmd, UnityPy and SteamCMD.

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

### Step 4: Log in to Steam

```
Log me in to SteamCMD, so you can publish my mods to the Steam Workshop. Give me the command and let
me type my own password.
```

### Step 5: Give the assistant its rules

```
Write an instructions file in this folder that you read at the start of every session (for example
CLAUDE.md or AGENTS.md).

Describe:
- what this workspace is
- where the decompiled code and extracted assets are
- that a new mod can start as a copy of IM-FastForward or of one of my own mods
- how to build and test a mod
- that SteamCMD can download Workshop mods to check other mods' code

Rules:
- After changing a mod, build it and run its tests.
- Commit changes to the mod's git repository, but only after their tests pass.
- Never commit or publish the game's files.
- Each mod's README says what the mod is for. If a request seems to go beyond that, say so before
  starting, and let me decide whether it belongs in this mod, another mod or a new one.
```

Add your own rules to the end of the message if you like, for example "never publish to Steam unless I
ask" or "show me a plan before any big change".

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

### Step 8: Make a thumbnail

Your mod still has FastForward's picture. Replace it with your own:

- File: `source\assets\thumb.png`
- Size: 512 × 512 PNG, under 1 MB

### Step 9: Upload it

```
Publish Gentle Shows to the Steam Workshop as a new item with its tools/publish_workshop.py.
```

### Step 10: Make it public

On your mod's Workshop page:

1. Add [Harmony Checker](https://steamcommunity.com/sharedfiles/filedetails/?id=3102983863) as a Required Item.
2. Optional: add tags and screenshots.
3. Make it public.

### Updating a mod

```
Publish the update of Gentle Shows to the Steam Workshop.
```

## Tips

Ask for a plan before a bigger change, and add what the plan should cover:

- Tests, so big changes, or many small ones over time, don't break what already works:

  ```
  Before you write code, list the situations and edge cases this has to handle and plan a test for
  each. Test every patch in the mod, not only this change. Use unit tests where you can; plan an
  in-game test only for what needs the running game, such as layout or clicks, because each game
  start is slow.
  ```

- Architecture, so the code stays easy to change as the mod grows:

  ```
  Before you write code, describe how it fits together: which classes hold what, which game methods
  you patch, how actions flow and what gets saved. Use the game's own data where you can instead of
  adding to save files. Sketch the UI only if a screen is new or its layout changes.
  ```

- Compatibility, so your mod doesn't break or get broken by other mods that change the same things:

  ```
  For each patch, plan what happens if another mod already fixes the same thing, patches the same
  method, or changes the values it reads. Change as little as possible: adjust the game's values
  rather than replacing or skipping its methods, don't rely on other mods' names, and if something
  your patch expects isn't there, log it and carry on. Also check the mods I have installed for
  conflicts.
  ```

As a mod grows, check that it still sticks to what it's for:

```
Review this mod's scope. List features that don't serve what the README says the mod is for,
features that overlap with my other mods, and anything half-finished. Don't change anything; I'll
decide what to keep, move or remove.
```
