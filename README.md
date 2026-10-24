# jellyfin-plugin-mediatracker
[Jellyfin](https://github.com/jellyfin/jellyfin) plugin for [MediaTracker](https://github.com/bonukai/MediaTracker)

This fork publishes the current Jellyfin-compatible plugin builds from `PacmanForever/jellyfin-plugin-mediatracker`.

## Requirements

Minimum MediaTracker version: `0.1.0`

## Features

- per user configuration
- progress and seen scrobbler

## Installation

- Add new Repository in Jellyfin (Dashboard -> Plugins -> Repositories -> +) from url
```
https://raw.githubusercontent.com/PacmanForever/jellyfin-plugin-mediatracker/main/manifest.json
```
- Refresh the plugin catalogue if needed, then install MediaTracker from Catalogue (Dashboard -> Plugins -> Catalogue)

## Configuration

Set your MediaTracker instance url and API keys in the plugin settings

## Privacy

The plugin does not collect analytics or send telemetry to its maintainers or other third parties. To provide playback scrobbling, it sends playback state and progress, IMDb/TMDb identifiers, episode details when applicable, duration, device name, and the user's API token to the MediaTracker server configured by the administrator. Requests use HTTPS, except when the configured server is localhost.
