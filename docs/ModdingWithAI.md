# Making Idol Manager mods with an AI coding assistant

This guide sets up the workspace I use to make my mods with an AI coding assistant, such as Claude Code,
Codex, Cursor or GitHub Copilot. Follow the steps in order and paste each message into the assistant.
Send the next message only after the assistant has finished the previous one.

## 1. Install these yourself

1. Idol Manager from Steam, on Windows.
2. [IM-HarmonyIntegration](https://github.com/ui3TD/IM-HarmonyIntegration#install-instructions-windows):
   follow its install instructions, then start the game once to check that it works.
3. An AI coding assistant that can run commands and edit files on your computer.

Then make an empty folder, for example `Documents\IM-Modding`, and start the assistant in it.

## 2. Set up the workspace

**Mod library**

```
I want to make Harmony mods for Idol Manager. I have already installed IM-HarmonyIntegration, which
loads them in the game: don't download, build or reinstall it, BepInEx or Harmony. Build against the
copies in my game folder. https://github.com/ui3TD/IM-HarmonyIntegration explains how a mod must be
written.

In this folder, make me an empty mod library that works like https://github.com/ui3TD/Tel-Mod-Library:
building a mod puts it in the game, and each mod has unit tests. Don't copy any of Tel's mods. Then
make a tiny test mod that writes one line to the game's log, build it, and tell me how to check it in
the game.
```

**Game code**

```
Decompile the game's code into a folder outside the mod library, so you can read how the game works
when I ask for a mod. Never commit or publish the decompiled code or the game's DLLs.
```

**In-game tests**

```
Clone https://github.com/ui3TD/IM-InGameTests into this folder and follow its README. Set it up to run
in-game tests for my mods, the way tests/InGameTests.TelMods does in Tel-Mod-Library. Add an in-game
test for the test mod and run it.
```

Keep Steam running. The game opens, runs the tests and closes by itself.

**Notes for later sessions**

```
Write an instructions file in the mod library that you read at the start of every session (for
example CLAUDE.md or AGENTS.md). Say what this workspace is, where the decompiled game code is, and
how to build and test a mod. Add these rules: after changing a mod, build it and run its unit and
in-game tests; never commit or publish the game's DLLs or decompiled code; never push to GitHub or
publish to Steam unless I ask; show me a plan before any big change.
```

## 3. Make a mod

Describe what you want in plain words, for example:

```
Make a mod called Gentle Shows where idols lose half as much stamina from theater shows. Find how
the game does it first, and add unit and in-game tests.
```

To try it, start the game, open the Mods menu, turn the mod on, and load a save.

When you no longer need the test mod from step 2, ask the assistant to delete it.

## 4. Publish it on the Steam Workshop

You need a free [GitHub](https://github.com/) account and the Steam account that owns the game.

```
Set me up to publish my mods to the Steam Workshop with a script like tools/publish_workshop.py in
Tel-Mod-Library. It only publishes code that is on GitHub, so help me put my mod library on GitHub,
keeping my email address private in commits. Let me type my own passwords. Tell me what each mod needs
before it can be published.
```

Then, for each mod:

```
Publish <mod name> to the Steam Workshop.
```

The script uploads a new mod as unlisted. On its Workshop page, add the tags, add
[Harmony Checker](https://steamcommunity.com/sharedfiles/filedetails/?id=3102983863) under
Add/Remove Required Items, and then make it public. Harmony Checker tells players on the main menu
whether IM-HarmonyIntegration is installed.

For a small hand-written example mod, see [IM-FastForward](https://github.com/ui3TD/IM-FastForward).
