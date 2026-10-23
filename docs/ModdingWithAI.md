# Making Idol Manager mods with an AI coding assistant

This guide sets up the workspace I use to make my mods with an AI coding assistant, such as Claude Code,
Codex, Cursor or GitHub Copilot. Follow the steps in order and paste each message into the assistant.
Send the next message only after the assistant has finished the previous one.

Every mod starts as a copy of [IM-FastForward](https://github.com/ui3TD/IM-FastForward), a small mod
made to be a template. It comes with a build that puts the mod in the game, unit tests, in-game tests
and a script that publishes to the Steam Workshop.

## 1. Install these yourself

1. Idol Manager from Steam, on Windows.
2. [IM-HarmonyIntegration](https://github.com/ui3TD/IM-HarmonyIntegration#install-instructions-windows):
   follow its install instructions, then start the game once to check that it works.
3. An AI coding assistant that can run commands and edit files on your computer.

Then make an empty folder, for example `Documents\IM-Modding`, and start the assistant in it.

## 2. Set up the workspace

**The template**

```
I want to make Harmony mods for Idol Manager. I have already installed IM-HarmonyIntegration, which
loads them in the game: don't download, build or reinstall it, BepInEx or Harmony. Build against the
copies in my game folder.

Clone https://github.com/ui3TD/IM-FastForward and https://github.com/ui3TD/IM-InGameTests into this
folder, and follow IM-FastForward's README until the mod builds and its unit tests and in-game tests
pass. Install whatever its README needs.
```

The in-game tests open the game, run the tests and close it by themselves. Keep Steam running.
This also puts FastForward in your game; turn it off in the Mods menu if you don't want it.

**Game code**

```
Decompile the game's code into a folder outside the mod repos, so you can read how the game works
when I ask for a mod. Never commit or publish the decompiled code or the game's DLLs.
```

**Notes for later sessions**

```
Write an instructions file in this folder that you read at the start of every session (for example
CLAUDE.md or AGENTS.md). Say what this workspace is, where the decompiled game code is, that each new
mod starts as a copy of IM-FastForward in its own folder, and how to build and test a mod. Add these
rules: after changing a mod, build it and run its unit and in-game tests; never commit or publish the
game's DLLs or decompiled code; never push to GitHub or publish to Steam unless I ask; show me a plan
before any big change.
```

## 3. Make a mod

Describe what you want in plain words, for example:

```
Make a new mod called Gentle Shows where idols lose half as much stamina from theater shows. Start
from a copy of IM-FastForward in a new folder with a new git repository, following its "Make your own
mod" section. Find how the game does it
first, and replace FastForward's code and tests with the new mod's.
```

To try it, start the game, open the Mods menu, turn the mod on, and load a save.

## 4. Publish it on the Steam Workshop

You need a free [GitHub](https://github.com/) account and the Steam account that owns the game.

The first time:

```
Publish Gentle Shows to the Steam Workshop with its tools/publish_workshop.py, as a new item. It only
publishes code that is on GitHub, so first put the mod on GitHub, keeping my email address private in
commits. Let me type my own passwords.
```

For an update:

```
Publish the update of Gentle Shows to the Steam Workshop.
```

A new mod is uploaded as unlisted. On its Workshop page, add the tags, add
[Harmony Checker](https://steamcommunity.com/sharedfiles/filedetails/?id=3102983863) under
Add/Remove Required Items, and then make it public. Harmony Checker tells players on the main menu
whether IM-HarmonyIntegration is installed.
