# Version 1.0.22+ localization

Source files remain under `source/Localization/` and are embedded into `HarmonyIntegration.dll` by MSBuild.

Languages:

- `en`
- `cn`
- `jp`
- `ru`
- `ptbr`
- `kr`
- `fr`
- `es`

New keys in 1.0.22:

```text
IMHI_MODS_VERSION
IMHI_MODS_VERSION_FORMAT
IMHI_MODS_LOCAL
IMHI_MODS_WORKSHOP
```

The card metadata uses `IMHI_MODS_VERSION_FORMAT`, `IMHI_MODS_LOCAL`, and `IMHI_MODS_WORKSHOP` so word order/source labels can be translated independently per language.
