# Claude Code Instructions

## Important Files

**Implementation Plan:** `C:\Users\Atheran\Desktop\Claude Projects\ToDo\Vintage Story Modding\Mod Manager\Instance System Implementation Plan.md`

A hardlink exists at `./PLAN.md` for convenience.

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

See `PLAN.md` (hardlinked) for full implementation status and remaining work.
