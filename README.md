# IM-HarmonyIntegration 1.2.0

IM-HarmonyIntegration is a BepInEx bootstrap plugin that enables Harmony-based mods in Idol Manager.

## 1.2.0 design goal

Version 1.2.0 deliberately makes HarmonyIntegration small and infrastructure-only. The plugin is difficult to update for players because it lives outside Idol Manager's normal Steam Workshop mod-delivery path. Features that can live in a regular Harmony mod should therefore not live in this plugin.

HarmonyIntegration 1.2.0 is responsible only for:

- detecting enabled Idol Manager mods that declare `HarmonyID` in `info.json`;
- resolving `<HarmonyID>.dll` in the declaring mod's directory;
- loading the selected patch assembly;
- applying patches through `Harmony.CreateAndPatchAll`;
- unpatching through `Harmony.UnpatchID` when the effective mod is disabled;
- synchronizing Harmony state after `Mods.LoadMods()` and `staticVars._settings.SwitchModStatus()`;
- resolving duplicate enabled copies that share a HarmonyID, preferring LocalLow over Workshop and preserving the previous later-entry precedence within a source tier;
- logging missing DLLs, duplicate selections, load failures, and unload failures.

It intentionally does **not** alter any mod-management UI.

## Removed from the BepInEx plugin in 1.2.0

The following ExSlam-fork functionality is intentionally removed from HarmonyIntegration and deferred to a separate Workshop-deliverable Harmony mod:

- replacement of `Mods_Popup.Render`;
- installed-mod pagination and search;
- source filtering;
- custom card layout and metadata;
- thumbnail scheduling and cache management;
- custom Mod_Button tooltip behavior;
- browser localization resources;
- any future redesign of the upload-new-mod or update-uploaded-mod menus.

See `UI_EXTRACTION_PLAN.md` for the future mod's scope and migration requirements.

## Harmony mod format

A Harmony-enabled Idol Manager mod declares a `HarmonyID` in `info.json` and includes a DLL named exactly:

```text
<HarmonyID>.dll
```

The DLL must contain Harmony-compatible patches. HarmonyIntegration owns patching and unpatching, so the mod should not create its own BepInEx bootstrap or independently patch itself on startup.

## Source layout

```text
IM-HarmonyIntegration-1.2.0-source/
├── README.md
├── CHANGELOG.md
├── UI_EXTRACTION_PLAN.md
└── source/
    ├── HarmonyIntegration.csproj
    └── Plugin.cs
```

## Build note

This package contains source only. It has not been compiled as part of this refactor pass.

The project targets .NET Framework 4.6 and expects Idol Manager's `Assembly-CSharp.dll` and `Assembly-CSharp-firstpass.dll` under `source/dll/`, matching the existing ExSlam project layout.
