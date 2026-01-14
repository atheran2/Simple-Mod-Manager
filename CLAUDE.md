# Claude Code Instructions

## Git Workflow - IMPORTANT

**Current working branch:** `feature/game-instances`

**Push commands must use:**
```
git push fork feature/game-instances
```

**DO NOT touch:** `feature/mod-categories-and-submenu-fix` - this is an open PR to the upstream repo (Interzoneism/Simple-Mod-Manager). Leave it alone until the PR is approved/merged.

**Remotes:**
- `origin` = upstream (Interzoneism/Simple-Mod-Manager) - read only
- `fork` = atheran2's fork - push here

**After PR is merged:** Rebase `feature/game-instances` onto updated master, then this restriction can be lifted.

## Project Overview

Vintage Story Mod Manager - A WPF desktop app for managing Vintage Story mods.

## Build

```
dotnet build
```

Or use `build.bat` for release builds.

## Current Work in Progress

- Game Instances system (Beta) - isolated mod environments
- Dependency Resolver - automatic mod dependency management

### Remaining TODOs
1. Instance details/edit dialog (currently uses message box)
2. Instance duplication feature
