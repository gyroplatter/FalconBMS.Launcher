# Falcon BMS Launcher v3

Faclon BMS Launcher v3 has been designed from the bottom up to make control setup easier to understand and edit, all while keeping existing bindings up to date as Falcon BMS adds or changes controls. This means not having to rebuild your control setup after as BMS evolves.

This manual explains how to use Launcher v3 to configure your keyboard and device controls.

Launcher v3 is compatible with both Windows and Linux.

The Launcher replaces the ‘Controllers’ tab located within the in-game Falcon BMS Setup menu. Do not modify your controls using the Falcon BMS in-game ‘Controllers’ tab.

## Getting Started

Launcher v3 is included with Falcon BMS versions 4.38.2 and newer.

After installing Falcon BMS 4.38.2, a shortcut to the Launcher will be added to your desktop.

You can also run the Launcher by opening your Falcon BMS installation folder, opening the ‘Launcher’ folder, and double clicking ‘FalconBMS.Launcher.exe’.

### First Run on a New Falcon BMS 4.38 Install
When running Launcher v3 for the first time on a new Falcon BMS installation, the Launcher will start normally and allow you to begin setting up your controls.

### First Run After Updating From an Older Version of Falcon BMS 4.38
Launcher v3 uses a new control file system that is not compatible with older versions of the Launcher.

After updating from an older version of Falcon BMS, Launcher v3 will check for existing Launcher v2 KEY and XML control files. If found, it will automatically perform a one-time conversion into the new Launcher v3 format.

A backup of your older Launcher v2 files will automatically be created in:
\User\Config\Launcher-Backups

When the conversion is complete, the Launcher will start normally and allow you to continue setting up your controls.

If any mappings could not be converted, a message will show which controls you must be remapped manually.


### Subsequent Runs
The Launcher was built to handle future BMS control updates automatically, so users no longer need to delete and rebuild their AUTO key files when Falcon BMS introduces new controls.

The Launcher first loads the current FULL key files, then applies any user changes on top. This allows new controls to appear automatically while preserving existing custom bindings.

For example, if a BMS update adds new F-15 callbacks, the Launcher will load them from the updated FULL key file, display them in the Launcher’s 'Controls' tab, and generate a new BMS - Auto-F15ABCD.key file.


---

# Developer Details

The main goal of Launcher v3 is to make control setup easier to understand and edit, while keeping existing bindings up to date as Falcon BMS adds or changes controls. This means users do not have to rebuild their control setup after Falcon BMS updates.

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
2. Run the first-launch legacy import check:
   - If v2 Launcher files are found and import has not already been handled, back up v2 files and convert them to JSON
   - If no legacy files are found, or the import was already handled, skip import and continue normal startup
3. Load `BMS - Full*.key` files
4. Build the current keyboard/control catalog from the FULL key files
5. Check for existing keyboard JSON files:
   - `KeyboardBindings_F-16.json`
   - `KeyboardBindings_F-15ABCD.json`
6. If keyboard JSON files exist, overlay saved keyboard bindings onto the current catalog
7. If keyboard JSON files do not exist, keep the current FULL key defaults
8. Discover DirectInput devices
9. Match discovered devices to stock `Setup.v100.*.xml` files, using PID/VID fallback when device names differ
10. For each discovered device, check for an existing device JSON file:
   - `DeviceBindings_{Aircraft}_{DurableDeviceKey}_{ProductName}.json`
11. If device JSON exists, load that device profile from JSON
12. If device JSON does not exist, build that device profile from matched stock XML
13. If no stock XML exists for that device, build an empty device profile
14. Retain saved profiles for devices that are currently offline
15. Build the complete in-memory binding model


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

---

## Logging

The Launcher writes diagnostic log output to:

- User\Config\Launcher_Log.txt

The log records application startup, selected install changes, device discovery, JSON loading/writing, generated compatibility file writes, launch preparation, Falcon launch events, close-time save behavior, warnings, and exceptions.

This file is overwritten on every restart of the Launcher.

Generated file writes include before/after file signatures so it is possible to see whether a file actually changed.