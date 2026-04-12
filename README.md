# Falcon BMS Alternative Launcher (Rebuild)

## Summary
This is a full rebuild of the Falcon BMS Alternative Launcher using .NET 8 (WPF), with strict parity to the original launcher.

This launcher is currently only compatible with Falcon BMS 4.38.

The goal of this project is to replicate the original launcher behavior exactly, including:
- File outputs
- Device handling
- Keymapping logic
- Launch-time behavior

All XML and KEY behaviors are verifiable against the original launcher

---

## Branches

### master

Close parity to original launcher. Axes and keymapping are in separate tabs.

### feature/axis-keymapping-combined

Same as master, but axes and keymapping are combined into a single table.

Goals:
- Improve usability by treating axes and keybindings as a unified "control" system
- Reduce user confusion
- Prepare for potential future support of different axis mappings per aircraft (F16 vs F15)

---

## Dependencies (NuGet)

This project uses the following NuGet packages:

- Vortice.DirectInput  
  For DirectInput bindings used for device detection, axis polling, and button input.

- Vortice.Win32  
  Supporting Windows API bindings required by DirectInput.

- System.ServiceModel.Syndication  
  For RSS feed handling in the launcher UI.

---

## Current Status

### Completed (Major Parity Systems)
- Device discovery, sorting, and mapping (DirectInput)
- XML file loading, editing, and saving
- KEY file loading, editing, and saving
- Falcon BMS User.cfg override handling
- Callsign/name registry updates
- POP and LBK file generation

All generated output files are compatible with the original launcher.

Mappings and axes are interchangeable:
- Edits in the original launcher are reflected here
- Edits here are reflected in the original launcher

---

### In Progress

- Starters and Overrides system
- UI and styling refinement
- Launcher bypass
- Performance optimizations

---

## Architecture Overview

```plaintext
Views (WPF UI)
   to
ViewModels (MVVM)
   to
Services
   ├── DeviceService (DirectInput detection and sorting)
   ├── KeyMappingService (FULL → AUTO generation)
   ├── XmlConfigService (device XML handling)
   ├── PopFileService (pilot + launch config)
   └── LauncherService (orchestration)
   to
File Output (User\Config)