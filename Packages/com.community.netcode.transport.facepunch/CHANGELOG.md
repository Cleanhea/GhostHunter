# Changelog
All notable changes to this package will be documented in this file. The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/)

## 2.0.0-ghosthunter.2

### Changed
- Updated the bundled Facepunch.Steamworks managed and native Unity files together to 2.5.2.
- Replaced the separate Linux/macOS managed assemblies with the matched Posix assembly.
- Restored native Apple Silicon initialization by using the current `SteamInternal_SteamAPI_Init` binding.

## 2.0.0

### Changed
- Targets the Netcode for GameObjects 1.0.0 package.
- Renamed namespaces from MLAPI to Netcode.

### Removed
- Removed support for channels.
- No longer send 1 byte of channel information in each message.

## 1.0.0
First version of the Facepunch Transport as a Unity package.
