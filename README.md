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
