# Falcon BMS Launcher (Rebuild)

## Summary

This is a full rebuild of the Falcon BMS Launcher using **.NET 4.8 (WPF)**.


The goal of this project is to modernize the Launcher codebase and redesign the UI/UX so Falcon BMS control configuration is easier for new users to understand. This Launcher also moves toward a unified controls model built around a cleaner in-memory binding system and JSON-based storage.

This new Launcher serves as a bridge between the current Falcon BMS file system and possible future enhancements. Falcon BMS 4.38 still depends on several existing file formats, and some third-party tools still read those files directly. To avoid breaking those workflows, this Launcher continues to generate the required legacy files, but those files are treated as compatibility outputs rather than the primary editing model.

On startup, the Launcher builds its working model from current BMS input files and saved Launcher JSON files:

- The `BMS - Full*.key` files are used to build the current keyboard/control catalog. This means the Launcher starts from the current list of controls provided by the installed BMS version, rather than only loading an older saved copy of the user's bindings.

- Saved JSON files are then applied on top of that current catalog to restore user-managed keyboard and device bindings. This allows new controls added by BMS to appear in the Launcher without requiring the user to delete their existing bindings or rebuild their control setup from scratch.

- For devices, the Launcher discovers DirectInput hardware, checks for matching JSON binding files, and falls back to stock XML templates or empty device profiles when no JSON exists. This keeps existing user bindings intact while still allowing the Launcher to pick up new or changed control definitions from BMS over time.

On BMS launch or Launcher close, the in-memory model writes updated JSON files first, then generates the legacy compatibility outputs that Falcon BMS and older tools still expect.

Because this new Launcher reasons from its JSON-based binding model, it is not currently possible to import user-edited files directly from the current Alternative Launcher v2 into this new v3 Launcher.

This Launcher is currently only compatible with **Falcon BMS 4.38**.

---

## Dependencies (NuGet)

This project uses the following NuGet packages:

- Vortice.DirectInput  
  For DirectInput bindings used for device detection, axis polling, and button input.

- System.ServiceModel.Syndication  
  For RSS feed handling in the Launcher UI.

- System.Net.Http
  For RSS feed usage in .NET 4.8

- System.Text.Json  
  For reading and writing JSON binding files while correcting small formatting issues such as trailing commas and comments

---

## Current Status

### Completed
- Install discovery
- Device discovery, sorting, and mapping (DirectInput)
- XML file loading
- KEY file loading
- In-memory model
- JSON file loading, editing, and saving
- Falcon BMS User.cfg override handling
- Callsign/name registry updates
- POP and LBK file generation
- Launcher_Log.txt diagnostic output

---

### In Progress

- UI and styling refinements
- Launcher bypass
- Control import and export
- Performance optimizations

---

## Architecture Overview

### Current JSON Binding Files

The Launcher generates aircraft specific JSON files within the selected install's `User\Config\JSON` folder.

Current keyboard JSON files:

- KeyboardBindings_F-16.json
- KeyboardBindings_F-15ABCD.json

Current device JSON files:

- DeviceBindings_{Aircraft}_{DurableDeviceKey}_{ProductName}.json

For duplicate devices with the same PID/VID, the durable device key includes a sequence number.

Examples:

- DeviceBindings_F-16_044F0402_Joystick - HOTAS Warthog.json
- DeviceBindings_F-15ABCD_06A30762_X52 Professional H.O.T.A.S.json
- DeviceBindings_F-16_044FB351_F16 MFD 1.json

Keyboard, button, and POV bindings are stored separately for each aircraft profile. Falcon BMS 4.38 still uses shared axis mappings, so axis assignments are synchronized between the F-16 and F-15 profiles.

### Compatibility Outputs

The Launcher generates Falcon BMS "legacy" files within the selected install's `User\Config` folder.

These files are generated outputs:

- BMS - Auto.key
- BMS - Auto-F15ABCD.key
- Setup.v100.[Device Name] {...}.xml

The Launcher writes these files from the in-memory/JSON binding model. They are not the source of launcher-managed binding state.

### Startup Flow

When a Falcon BMS install is loaded, the Launcher builds the binding model in one pass.

The Launcher does not treat JSON loading as an all-or-nothing step. Keyboard and device bindings are resolved independently, and device JSON is checked per discovered device.

1. Check for a selected Falcon BMS install
2. Load `BMS - Full*.key` files
3. Build the current keyboard/control catalog from the FULL key files
4. Check for existing keyboard JSON files:
   - `KeyboardBindings_F-16.json`
   - `KeyboardBindings_F-15ABCD.json`
5. If keyboard JSON files exist, overlay saved keyboard bindings onto the current catalog
6. If keyboard JSON files do not exist, keep the current FULL key defaults
7. Discover DirectInput devices
8. Match discovered devices to stock `Setup.v100.*.xml` files, using PID/VID fallback when device names differ
9. For each discovered device, check for an existing device JSON file:
   - `DeviceBindings_{Aircraft}_{DurableDeviceKey}_{ProductName}.json`
10. If device JSON exists, load that device profile from JSON
11. If device JSON does not exist, build that device profile from matched stock XML
12. If no stock XML exists for that device, build an empty device profile
13. Build the complete in-memory binding model


### Save and Launch Flow
```plaintext
Prepare for launch
   ↓
Check dirty state
   ↓
Write keyboard JSON, if bindings changed
   ↓
Write device JSON, if bindings changed
   ↓
Generate compatibility KEY files
   ↓
Generate DeviceSorting.txt
   ↓
Generate device XML files
   ↓
Generate axismapping.dat
   ↓
Generate joystick.cal
   ↓
Apply User.cfg overrides
   ↓
Generate/update POP file
   ↓
Launch Falcon BMS
```

### Services Overview

```plaintext
Views (WPF UI)
   to
ViewModels (MVVM)
   to
In-memory binding model
   to
Services
   ├── Theater discovery
   ├── RSS handling
   ├── Theme handling
   ├── Device discovery
   ├── DirectInput polling
   ├── Stock XML matching
   ├── Key catalog loading
   ├── Keyboard JSON reading/writing
   ├── Device JSON reading/writing
   ├── Axis definition handling
   ├── Launch preparation
   ├── User.cfg override handling
   ├── POP/LBK handling
   ├── Debug diagnostics / logging
   └── Legacy compatibility writers
   to
File Output (User\Config\JSON & User\Config)
```

---

## Logging

The Launcher writes diagnostic log output to:

- User\Config\Launcher_Log.txt

The log records application startup, selected install changes, device discovery, JSON loading/writing, generated compatibility file writes, launch preparation, Falcon launch events, close-time save behavior, warnings, and exceptions.

This file is overwritten on every restart of the Launcher.

Generated file writes include before/after file signatures so it is possible to see whether a file actually changed.