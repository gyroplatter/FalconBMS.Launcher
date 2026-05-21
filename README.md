# Falcon BMS Alternative Launcher (Rebuild)

## Summary
This is a full rebuild of the Falcon BMS Launcher using .NET 4.8 (WPF) x64.

This launcher is currently only compatible with Falcon BMS 4.38.

The goal of this project is to modernize the Launcher code and reimagine the UI/UX to make it easier to new users to understand. This Launcher also serves as a bridge between BMS's current file handling and possible future enhancements.

The project direction is to reduce the number of moving parts in Falcon BMS control configuration. Keyboard, HOTAS, button, POV, axis, and aircraft-specific bindings should move toward a cleaner JSON-based model.

Legacy Falcon BMS files are still generated so Falcon BMS and existing third-party tools continue to work, but those files are compatibility outputs rather than the primary editing model. Since this new Launcher reads from JSON files on startup, it is not currently possible to import files from the current v2 Alternative Launcher into this new v3 Launcher. 

---

## Branches

### master

Goals:
- Improve usability by treating axes and keybindings as a unified "control" system
- Reduce user confusion
- Prepare for potential future support of different axis mappings per aircraft (F16 vs F15)
- Implement an in-memory binding model
- Continue support for "legacy" .KEY and .XML files

---

## Dependencies (NuGet)

This project uses the following NuGet packages:

- Vortice.DirectInput  
  For DirectInput bindings used for device detection, axis polling, and button input.

- System.ServiceModel.Syndication  
  For RSS feed handling in the launcher UI.

- System.Net.Http
  For RSS feed usage in .NET 4.8

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

Launcher-managed JSON files are written under:

- User\Config\JSON

Current keyboard JSON files:

- KeyboardBindings.json
- KeyboardBindings_F-15ABCD.json

Current device JSON files:

- DeviceBindings_{DurableDeviceKey}_{ProductName}.json

For duplicate devices with the same PID/VID, the durable device key includes a sequence number.

Examples:

- DeviceBindings_044F0402_Joystick - HOTAS Warthog.json
- DeviceBindings_06A30762_X52 Professional H.O.T.A.S.json
- DeviceBindings_044FB351_F16 MFD 1.json

### Compatibility Outputs

The Launcher generates Falcon BMS "legacy" compatibility files under the selected install's User\Config folder.

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
   - `KeyboardBindings.json`
   - `KeyboardBindings_F-15ABCD.json`
5. If keyboard JSON files exist, overlay saved keyboard bindings onto the current catalog
6. If keyboard JSON files do not exist, keep the current FULL key defaults
7. Discover DirectInput devices
8. Match discovered devices to stock `Setup.v100.*.xml` files
9. For each discovered device, check for an existing device JSON file:
   - `DeviceBindings_{DurableDeviceKey}_{ProductName}.json`
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
Write keyboard JSON
   ↓
Write device JSON
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
   ├── Device discovery
   ├── DirectInput polling
   ├── Key catalog loading
   ├── Keyboard JSON reading/writing
   ├── Device JSON reading/writing
   ├── Axis definition handling
   ├── Stock XML matching
   ├── Launch preparation
   ├── User.cfg override handling
   ├── POP/LBK handling
   ├── Theater discovery
   ├── RSS handling
   ├── Theme handling
   └── Legacy compatibility writers
   to
File Output (User\Config)
```