# Instance System Implementation Plan

---
## 📊 IMPLEMENTATION STATUS SUMMARY (Updated 2026-01-14)

### ✅ COMPLETED (MVP + New Features)
**Instance System (MVP):**
- Instance model (`GameInstance.cs`) and service (`InstanceService.cs`)
- Instance folder initialization (Mods, Saves, ModConfig, Logs, Cache)
- Instance configuration persistence (`instances-config.json`)
- Menu-based instance selector (File > Instances)
- Create/Delete/Open folder for instances
- Mod discovery per instance (via `_dataDirectory` swap)
- Game launch per instance (with `--dataPath`)
- Install/delete mods per instance
- Player session (login) copied to new instances

**Instance Browser UI (5.1):** ✅ COMPLETE
- New "Instances" tab in main window (default tab on launch)
- Card-based UI with image/icon area and details area
- Click image to launch game, click details to view info
- Create/Delete/Open folder actions
- Right-click context menu (themed properly)
- Empty state with "Create Your First Instance" prompt
- Buttons follow app theme (IMM.ButtonBaseStyle)

**Instance Edit Dialog:** ✅ COMPLETE (2026-01-14)
- Edit instance name
- Edit custom icon (browse/clear)
- Edit notes
- Auto-detected game version display (read-only)
- Open instance folder button

**Instance Duplication:** ✅ COMPLETE (2026-01-14)
- Full folder copy (mods, saves, configs)
- New instance with "(Copy)" suffix
- Runs on background thread
- Resets playtime/last played for new copy

**Automatic Dependency Resolution (G.2):** ✅ COMPLETE
- `DependencyResolverService` for recursive dependency resolution
- Auto-prompt after installing mod: "Install dependencies?"
- "Fix all missing dependencies" menu option (Mods menu)
- Recursive resolution (A→B→C all at once)
- Nested dependency check after install

### ⚠️ PARTIALLY DONE
- Multiple VS versions: Model supports it, no UI to configure

### ❌ REMAINING WORK
- **Instance Rename** from UI (currently must edit via dialog)
- **Base Mods Directory** (5.2) - Shared mods auto-copied to new instances
- **Migration Wizard** - Convert profiles to instances
- **Shared Mod Library** - Central cache with copy/symlink
- **Modpack Import/Export**
- UI indicator showing "Install to [Instance Name]"
- Version compatibility warnings

### 💭 MAYBE (Complex/Uncertain Feasibility)
- **Auto-download game versions** - Download different VS versions per instance from VS account
  - Requires VS account authentication (no public API)
  - Would need to reverse-engineer download system
  - Complex undertaking, may not be feasible

### 💡 SUGGESTED FEATURES (Phase 6) - Future
- Instance Templates/Presets (6.1)
- Instance Groups/Categories (6.2)
- Mod Health/Diagnostics (G.1) - Batch scan for broken mods
- Per-Instance Backup Integration (6.4)
- Quick Switching Hotkeys (6.5)
- Statistics Dashboard (6.6)
- Instance Comparison View (6.8)

### 🐛 KNOWN BUGS
1. ~~**Creating new instance does not set it as active**~~ ✅ FIXED

2. **Deleting active instance doesn't switch away properly** - When deleting the currently active instance, the UI doesn't switch to another instance or back to profile.
   - **Status**: Can now be fixed (Instance Browser exists)
   - **Location**: `DeleteInstanceMenuItem_OnClick` in MainWindow.xaml.cs

3. ~~**App startup doesn't load last active instance**~~ ✅ FIXED

---

## Overview

Transform the current "profile" system into a full **Instance-based** architecture (similar to Minecraft launchers like MultiMC/Prism). Each instance is a completely isolated VS environment with its own mods, saves, and configurations.

### Current State
- Single shared `Mods` folder per DataDirectory
- Profiles enable/disable mods via `clientsettings.json` disabled list
- All profiles share the same physical mod files
- Launch already uses `--dataPath` argument

### Target State
- Each instance = isolated DataPath folder
- Mods physically present in instance = enabled
- No enable/disable tracking needed per instance
- Full isolation: mods, saves, configs, worlds

---

## Architecture Decision: Suggested Approach

### Instance Storage Structure
```
{InstancesRoot}/                          # User-configurable, default: app folder
├── VS Hardcore/
│   ├── Mods/                             # Mods for this instance only
│   ├── Saves/                            # Worlds for this instance
│   ├── ModConfig/                        # Mod configurations
│   ├── clientsettings.json               # Instance-specific settings
│   ├── Logs/
│   └── instance.json                     # Instance metadata (managed by app)
├── VS Exploration/
│   └── ...
└── Vanilla/
    └── ...
```

### Instance Metadata (`instance.json`)
```json
{
  "name": "VS Hardcore",
  "created": "2024-01-15T10:30:00Z",
  "lastPlayed": "2024-01-20T18:45:00Z",
  "vsVersion": "1.20.0",                  // Target VS version (optional)
  "gameDirectory": "C:\\Program Files\\Vintagestory",  // Can differ per instance
  "iconPath": "icon.png",                 // Optional custom icon
  "notes": "My hardcore survival playthrough",
  "totalPlaytime": 3600                   // Seconds (optional tracking)
}
```

---

## Phase 1: Core Instance Infrastructure

### 1.1 Instance Model & Service ✅ IMPLEMENTED
**Goal**: Create the data model and service for managing instances

**Files to create/modify**:
- [x] `Models/GameInstance.cs` - Instance data model
- [x] `Services/InstanceService.cs` - CRUD operations for instances

**Model properties**:
```csharp
public class GameInstance
{
    public string Id { get; set; }              // GUID
    public string Name { get; set; }
    public string Path { get; set; }            // Full path to instance folder
    public string? GameDirectory { get; set; }  // VS installation path
    public string? TargetVsVersion { get; set; }
    public DateTime Created { get; set; }
    public DateTime? LastPlayed { get; set; }
    public string? IconPath { get; set; }
    public string? Notes { get; set; }
}
```

**Service methods**:
```csharp
public interface IInstanceService
{
    string InstancesRootPath { get; set; }
    IReadOnlyList<GameInstance> GetAllInstances();
    GameInstance? GetInstance(string id);
    GameInstance CreateInstance(string name, string? vsVersion = null);
    void DeleteInstance(string id, bool deleteFiles = false);
    void RenameInstance(string id, string newName);
    string GetInstanceModsPath(string id);
    string GetInstanceSavesPath(string id);
    string GetInstanceConfigPath(string id);
}
```

**Test**:
- Create instance via service
- Verify folder structure created
- Read instance back
- Delete instance

---

### 1.2 Instance Configuration Persistence ✅ IMPLEMENTED (in InstanceService)
**Goal**: Save/load instances root path and instance list

**Files to modify**:
- [x] ~~`Services/UserConfigurationService.cs`~~ - Handled in InstanceService via `instances-config.json`
- [x] ~~`DevConfig.cs`~~ - Default path is `{AppDirectory}/Instances`

**Settings to add**:
- [x] `InstancesRootPath` - where instances are stored (in InstanceService)
- [x] `ActiveInstanceId` - currently selected instance (in InstanceService)
- [ ] `InstancesEnabled` - feature toggle during development (NOT IMPLEMENTED - instances always available)

**Test**:
- Change instances root path
- Restart app, verify path persisted
- Switch active instance, verify persisted

---

### 1.3 Instance Folder Initialization ✅ IMPLEMENTED
**Goal**: Create proper VS data folder structure when creating instance

**Required subfolders**:
```
Mods/
Saves/
ModConfig/
Logs/
Cache/
```

**Also create**:
- [x] `clientsettings.json` with player session data copied from source profile
- [x] `instance.json` metadata file

**Test**:
- Create new instance
- Verify all folders exist
- Verify VS can launch with `--dataPath` pointing to instance

---

## Phase 2: UI Integration

### 2.1 Instance Selector UI ✅ PARTIALLY IMPLEMENTED (Menu-based)
**Goal**: Replace profile dropdown with instance selector

**Options** (pick one):
- [ ] **Option A**: Sidebar with instance list (like Prism Launcher)
- [x] **Option B**: Menu in File menu (implemented as submenu, not dropdown in toolbar)
- [ ] **Option C**: Separate "Instances" tab/view

**Suggested**: Start with Option B (dropdown), upgrade to Option A later

**UI elements needed**:
- [x] Instance selector (as menu items under File > Instances)
- [x] "New Instance" button (Create Instance menu item)
- [ ] "Manage Instances" dialog (NOT IMPLEMENTED - no dedicated dialog)
- [x] Instance context menu items: delete, open folder
- [ ] Instance rename (NOT IMPLEMENTED from UI)
- [ ] Instance duplicate (NOT IMPLEMENTED)

**Test**:
- Create instance from UI
- Switch between instances
- Verify mod list updates to show instance's mods

---

### 2.2 Instance Management Dialog ❌ NOT IMPLEMENTED
**Goal**: Dialog for creating/editing instances

**Fields**:
- [ ] Instance name (only via simple InputBox on create)
- [ ] Target VS version (dropdown of installed versions?)
- [ ] Game directory (optional override)
- [ ] Notes/description
- [ ] Icon selection (optional)

**Actions**:
- [x] Create new instance (simple InputBox prompt)
- [ ] Duplicate existing instance
- [x] Delete instance (with confirmation)
- [x] Open instance folder in explorer

**Test**:
- Create instance with custom settings
- Edit instance name/notes
- Duplicate instance with all mods

---

### 2.3 Update Mod Discovery for Instances ✅ IMPLEMENTED (via _dataDirectory swap)
**Goal**: ModDiscoveryService reads from active instance's Mods folder

**Files to modify**:
- [x] ~~`Services/ModDiscoveryService.cs`~~ - Works via MainWindow updating `_dataDirectory` and calling `ReloadViewModelAsync()`
- [x] ~~`Services/ClientSettingsStore.cs`~~ - Works automatically since data directory changes

**Implementation approach** (different from plan):
Instead of modifying ModDiscoveryService, MainWindow swaps `_dataDirectory` to the instance path and reloads the ViewModel, which reinitializes ModDiscoveryService with the new path.

```csharp
// In MainWindow.InstanceMenuItem_OnClick:
_dataDirectory = instance.Path;
await ReloadViewModelAsync();
```

**Test**:
- Add mod to Instance A
- Switch to Instance B
- Verify mod not visible
- Switch back, verify mod visible

---

### 2.4 Update Game Launch ✅ IMPLEMENTED
**Goal**: Launch button uses active instance's path

**Implementation** (MainWindow.xaml.cs:8354-8391):
```csharp
var launchDataPath = activeInstance?.Path ?? _dataDirectory;
var launchGameDirectory = activeInstance?.GameDirectory ?? _gameDirectory;
// ...
startInfo.ArgumentList.Add("--dataPath");
startInfo.ArgumentList.Add(launchDataPath);
```

Also updates `LastPlayed` timestamp on the instance when launching.

**Test**:
- Launch game from Instance A
- Verify correct mods loaded in-game
- Verify saves are in Instance A folder

---

## Phase 3: Mod Management Per Instance

### 3.1 Install Mods to Instance ✅ IMPLEMENTED (via _dataDirectory)
**Goal**: Downloaded mods go to active instance's Mods folder

**Files to modify**:
- [x] ~~`ViewModels/ModBrowserViewModel.cs`~~ - Uses callback to MainWindow
- [x] ~~`Services/ModApiService.cs`~~ - MainWindow provides target path

**Implementation**:
MainWindow's `TryGetInstallTargetPath()` uses `_dataDirectory` which is set to the active instance's path when an instance is selected. No changes needed to ModApiService.

**New flow**:
1. User clicks "Install" on mod
2. Mod downloaded to `_dataDirectory/Mods/` (which is instance path when active)
3. Only that instance has the mod

**Test**:
- Install mod to Instance A
- Verify mod file in `{InstanceA}/Mods/`
- Switch to Instance B, mod not visible

---

### 3.2 Delete Mods from Instance ✅ IMPLEMENTED (via _dataDirectory)
**Goal**: Deleting a mod only affects current instance

**Implementation**: Delete operates on current `_dataDirectory/Mods/` which points to instance folder when active.

**Test**:
- Install same mod to Instance A and B
- Delete from Instance A
- Verify still exists in Instance B

---

### 3.3 Mod Version Selection Per Instance ✅ AUTOMATIC (via isolation)
**Goal**: Each instance can have different mod versions

**This is automatic** with instance isolation:
- Instance A has ModX v1.0
- Instance B has ModX v2.0
- No conflict, different folders

**UI consideration**:
- [ ] When installing, show "Install to [Instance Name]" (NOT IMPLEMENTED)
- [ ] Warn if mod version incompatible with instance's target VS version (NOT IMPLEMENTED)

**Test**:
- Install ModX v1.0 to Instance A (VS 1.19)
- Install ModX v2.0 to Instance B (VS 1.20)
- Verify each instance has correct version

---

## Phase 4: Advanced Features (Future) ❌ NOT IMPLEMENTED

### 4.1 Shared Mod Library (Optional) ❌ NOT IMPLEMENTED
**Goal**: Cache downloaded mods centrally, copy/link to instances

**Structure**:
```
{AppData}/SimpleVSManager/
├── ModLibrary/                           # Shared download cache
│   ├── modx/
│   │   ├── 1.0.0/
│   │   │   └── modx_v1.0.0.zip
│   │   └── 2.0.0/
│   │       └── modx_v2.0.0.zip
│   └── mody/
│       └── ...
└── Instances/                            # Instance folders
```

**Benefits**:
- Faster installs (copy from cache)
- Less disk space (if using symlinks)
- Track all downloaded versions

**Installation flow**:
1. Download mod to library (if not cached)
2. Copy (or symlink) to instance Mods folder

---

### 4.2 Instance Import/Export (Modpacks) ❌ NOT IMPLEMENTED
**Goal**: Share instances as modpacks

**Export options**:
- [ ] Full export: Mods + Configs + Saves (large)
- [ ] Modpack export: Mods + Configs only
- [ ] Manifest export: Just mod list (smallest, requires re-download)

**Export format** (`modpack.json`):
```json
{
  "name": "VS Hardcore Pack",
  "version": "1.0.0",
  "author": "YourName",
  "vsVersion": "1.20.0",
  "mods": [
    { "modId": "wildcraftmod", "version": "1.5.0", "required": true },
    { "modId": "xskills", "version": "0.8.0", "required": true }
  ],
  "optionalMods": [
    { "modId": "vtmlib", "version": "1.2.0" }
  ]
}
```

**Import flow**:
1. User selects modpack file
2. Create new instance
3. Download mods from mod database (with progress)
4. Copy configs if included

---

### 4.3 Multiple VS Versions Support ⚠️ PARTIAL (model ready, no UI)
**Goal**: Support different VS installations per instance

**Instance setting**:
- [x] `gameDirectory` - path to specific VS installation (property exists in model)
- [x] Launch uses `activeInstance?.GameDirectory ?? _gameDirectory` (implemented)

**Considerations**:
- [ ] Auto-detect installed VS versions (NOT IMPLEMENTED)
- [ ] Warn on mod version incompatibility (NOT IMPLEMENTED)
- [x] Different exe paths for different versions (works via GameDirectory property)

---

### 4.4 Instance Duplication ❌ NOT IMPLEMENTED
**Goal**: Clone an instance with all its content

**Options on duplicate**:
- [ ] Copy mods only
- [ ] Copy mods + configs
- [ ] Copy mods + configs + saves
- [ ] Copy everything

---

## Migration Path ⚠️ PARTIAL (Option A implemented)

### From Current Profiles to Instances

**Option A: Clean Start** ✅ CURRENT APPROACH
- [x] Instances are separate from profiles
- [x] Users manually create instances and install mods
- [x] Profiles continue to work for legacy users ("Use Profile" option)

**Option B: Migration Wizard** ❌ NOT IMPLEMENTED
- [ ] Detect existing profile with mods
- [ ] Offer to create instance from profile
- [ ] Copy enabled mods to new instance folder
- [ ] Copy relevant configs

**Migration steps**:
1. For each profile with `disabledMods` list:
   - Create instance folder
   - Copy all mods NOT in disabled list
   - Copy ModConfig folder
   - Set instance vsVersion from profile
2. Mark migration complete in config

---

## Implementation Priority

### Must Have (MVP) ✅ COMPLETE
1. [x] Instance model and service (1.1)
2. [x] Instance folder creation (1.3)
3. [x] Basic instance selector UI (2.1 - menu-based)
4. [x] Mod discovery per instance (2.3)
5. [x] Game launch per instance (2.4)
6. [x] Install mods to instance (3.1)

### Should Have ⚠️ PARTIAL
7. [ ] Instance management dialog (2.2) - ❌ NOT DONE
8. [x] Instance configuration persistence (1.2)
9. [x] Delete mods per instance (3.2)
10. [ ] Instance duplication (4.4) - ❌ NOT DONE

### Nice to Have ❌ NOT STARTED
11. [ ] Shared mod library (4.1)
12. [ ] Import/Export modpacks (4.2)
13. [ ] Multiple VS versions (4.3) - Model ready, no UI
14. [ ] Migration wizard

---

## Open Questions / Decisions Needed (RESOLVED)

1. **Instances root default location**: ✅ DECIDED
   - [x] App folder (`./Instances/`) - **IMPLEMENTED**
   - [ ] AppData (`%AppData%/SimpleVSManager/Instances/`)
   - [ ] User Documents
   - [ ] Prompt on first run

2. **What happens to existing profiles?**: ✅ DECIDED
   - [x] Keep both systems (profiles + instances) - **IMPLEMENTED** ("Use Profile" option)
   - [ ] Migrate profiles to instances
   - [ ] Deprecate profiles, instances only

3. **Mod library caching**: ✅ DECIDED
   - [x] Always copy mods to instances (simple, more disk space) - **IMPLEMENTED**
   - [ ] Use symlinks/hardlinks (complex, saves space)
   - [ ] Optional per-user setting

4. **Enable/disable mods within instance?**: ✅ DECIDED
   - [x] No - mod present = enabled (like Minecraft) - **IMPLEMENTED** (full isolation)
   - [ ] Yes - keep disabled list per instance
   - [ ] Hybrid - can disable but discouraged

5. **Saves management**: ✅ DECIDED
   - [x] Saves stay in instance (default) - **IMPLEMENTED**
   - [ ] Option to share saves between instances
   - [ ] Import/export individual saves

---

## Phase 5: Enhanced Instance Features (NEW)

### 5.1 Instance Browser UI ✅ IMPLEMENTED
**Goal**: Dedicated tab/view for browsing and managing instances with card-based UI

**UI Design - Card Layout**:
```
┌─────────────────────────────┐
│                             │
│      INSTANCE IMAGE/ICON    │  ← Click here = LAUNCH GAME
│      (large, top area)      │
│                             │
├─────────────────────────────┤
│ Instance Name               │  ← Click here = OPEN DETAILS/EDIT
│ 🕐 12.5 hrs │ 🎮 1.20.3    │
│ 📦 24 mods                  │
└─────────────────────────────┘
```

**Card Top Area (Image/Icon)**:
- Large instance icon or custom image
- **Click action**: Launch game with this instance immediately
- Default icon if none set (VS logo or generated from name)

**Card Bottom Area (Details)**:
- Instance name (prominent)
- Time played (from `TotalPlaytimeSeconds`)
- Game version (from `TargetVsVersion` or detected)
- Mod count (count files in instance's Mods folder)
- **Click action**: Open instance details/edit panel or dialog

**Details Panel/Dialog** (when clicking bottom area):
- Edit instance name
- Edit notes/description
- Set/change target VS version
- Set/change game directory override
- Set/change icon image
- View full stats (created date, last played, total playtime)
- Actions: Duplicate, Delete, Open Folder

**Tab Placement**:
- New "Instances" tab alongside "Mods" and "Database" tabs
- Or: Replace current profile system entirely with instance cards

**Additional Features**:
- "Create New Instance" card/button (+ icon)
- Grid layout, responsive to window size
- Right-click context menu as fallback
- Keyboard navigation support

**Implementation notes**:
- Use `ItemsControl` or `ListBox` with `WrapPanel` for card grid
- Each card is a custom `UserControl`
- ViewModel: `InstanceBrowserViewModel` with `ObservableCollection<InstanceCardViewModel>`
- Need to calculate mod count on demand (or cache it)
- Need to track playtime (currently model has field but not populated)

---

### 5.2 Base Mods Directory ❌ NOT IMPLEMENTED
**Goal**: Optional shared "base" folder with mods that auto-copy to new instances

**Concept**:
```
{InstancesRoot}/
├── _BaseMods/                    # Special folder for shared mods
│   ├── essential-mod.zip
│   └── utility-lib.zip
├── Instance A/
│   └── Mods/                     # Gets copies of _BaseMods on creation
└── Instance B/
    └── Mods/
```

**Behavior**:
- When creating a new instance, copy all mods from `_BaseMods/` to the new instance's `Mods/` folder
- Deleting a mod from an instance does NOT affect `_BaseMods/`
- `_BaseMods/` is managed separately (add/remove via dedicated UI or folder)
- Optional: Checkbox on instance creation "Include base mods"

**UI Elements**:
- "Manage Base Mods" menu item or button
- Shows list of mods in `_BaseMods/`
- Add from file, remove, open folder
- Optional: "Add to Base Mods" context menu on installed mods

**Configuration**:
- `BaseMods` path in `instances-config.json`
- Default: `{InstancesRoot}/_BaseMods/`
- Option to enable/disable auto-copy on instance creation

**Use cases**:
- Library mods everyone needs (VTMLlib, etc.)
- Personal "must-have" QoL mods
- Modpack creators maintaining a base set

---

## Phase 6: Developer Suggestions (Future Ideas)

### 6.1 Instance Templates / Presets
**Goal**: Save an instance configuration as a template for quick creation

**Concept**:
- "Save as Template" option on an instance
- Templates store: mod list (with versions), configs, settings
- "Create from Template" when making new instance
- Templates could be shared as files

---

### 6.2 Instance Groups / Categories
**Goal**: Organize instances into groups (like mod categories)

**Use cases**:
- Group by VS version (1.19.x, 1.20.x)
- Group by play style (Hardcore, Creative, Multiplayer)
- Group by modpack source

---

### 6.3 Instance Health / Diagnostics
**Goal**: Quick status check for each instance

**Features**:
- Detect broken/missing mods
- Version compatibility warnings (mod vs VS version)
- Mod conflict detection
- "Repair" option to re-download missing mods from database

---

### 6.4 Instance Sync / Backup Integration
**Goal**: Per-instance backup and sync options

**Features**:
- Automatic backup before launching (already exists for profiles)
- Cloud sync support (OneDrive, Google Drive folder)
- Backup history per instance
- Restore instance from backup

---

### 6.5 Quick Instance Switching Hotkey
**Goal**: Keyboard shortcut to quickly switch instances

**Implementation**:
- Ctrl+1, Ctrl+2, etc. for first N instances
- Or Ctrl+Shift+I to open instance switcher popup

---

### 6.6 Instance Statistics Dashboard
**Goal**: Show playtime, mod usage stats per instance

**Features**:
- Total playtime tracking (already have `TotalPlaytimeSeconds` in model)
- Most used mods across instances
- Instance creation/usage timeline

---

### ~~6.7 Mod Dependency Auto-Resolution for Instances~~ → Moved to G.2
See **G.2 Enhanced Dependency Resolution** in General App Features section.

---

### 6.8 Instance Comparison View
**Goal**: Compare mods between two instances side-by-side

**Use cases**:
- See what's different between working and broken instance
- Plan mod migration between instances
- Identify version differences for same mod

---

## General App Features (Not Instance-Specific)

### G.1 Mod Health / Diagnostics ❌ NOT IMPLEMENTED
**Goal**: Quick status check for all installed mods

**Features**:
- Scan all mods for broken/corrupt zip files
- Detect mods with missing dependencies (batch operation)
- Version compatibility warnings (mod version vs game version)
- Mod conflict detection (duplicate modIds, incompatible mods)
- "Repair All" option to fix all issues at once

**Implementation notes**:
- `ModDiscoveryService` already detects `MissingDependencies` and `DependencyHasErrors`
- Could add a "Health Check" menu item or toolbar button
- Show results in a dialog with fix options

---

### G.2 Automatic Dependency Resolution ✅ IMPLEMENTED
**Goal**: Automatically resolve dependencies when installing mods (like Prism Launcher)

**Implemented** (2026-01-13):
- ✅ **DependencyResolverService** - New service for recursive dependency resolution
- ✅ **On install**: After installing a mod, automatically detects missing dependencies and prompts to install them
- ✅ **Recursive**: Resolves entire dependency tree - if A needs B and B needs C, all are detected
- ✅ **"Fix All Dependencies"** menu option: Mods menu > Fix all missing dependencies
- ✅ **Confirmation dialog**: Shows list of dependencies to install with version info
- ✅ **Nested check**: After installing deps, checks if new deps have their own missing deps

**Files created/modified**:
- `Services/DependencyResolverService.cs` - New service with `ExtractDependenciesFromZip()` and `ResolveAllDependenciesAsync()`
- `MainWindow.xaml.cs` - `CheckAndOfferDependencyInstallAsync()`, `InstallResolvedDependenciesAsync()`, `FixAllDependenciesAsync()`
- `MainWindow.xaml` - Added "Fix all missing dependencies" menu item under Mods menu

**How it works**:
1. After mod is installed via Mod Browser, `CheckAndOfferDependencyInstallAsync()` is called
2. Extracts `modinfo.json` from the installed zip to find dependencies
3. Uses `DependencyResolverService.ResolveAllDependenciesAsync()` to check what's missing
4. Shows dialog: "This mod requires X, Y, Z. Install them?"
5. If user accepts, installs all missing dependencies
6. After installation, checks for nested dependencies and offers to fix those too

---

## Research Findings

### Dependency Auto-Resolution Feasibility ✅ FEASIBLE

**How it works**:
1. Each mod's `modinfo.json` contains a `dependencies` object:
   ```json
   "dependencies": {
     "game": "1.20.0",
     "vslib": "1.5.0",
     "someothermod": ""
   }
   ```
2. The app already parses this in `ModDiscoveryService.GetDependencies()` (line 953)
3. The mod database API (`/api/mod/{modid}`) can be queried by modId string
4. Existing "Fix" code in `MainWindow.xaml.cs:5490-5600` handles single-mod repair

**What Rustique does** (for reference):
- Extracts dependencies from `modinfo.json` inside downloaded zips
- Recursively resolves entire dependency chains in one pass
- `install --missing-dependencies` fixes all at once
- The mod database API does NOT return dependency data directly - must extract from mod files

**Current limitation in your app**:
- Manual per-mod "Fix" action only
- Non-recursive: if mod A needs B, and B needs C, user must fix twice
- No prompt on install

**Recommendation**:
1. Add recursive dependency resolution (follow chain until all resolved)
2. Trigger on install, not just as repair action
3. Add "Fix All Dependencies" batch operation

---

### App-Level Authentication ❌ NOT FEASIBLE (No Public API)

**Research findings**:
- Vintage Story uses `auth.vintagestory.at` for authentication
- **No public OAuth API** or documented authentication flow for third-party apps
- Session tokens are stored in `clientsettings.json` under `stringSettings`:
  - `sessionkey`
  - `sessionsignature`
  - `useremail`
  - `playeruid`
  - `playername`
  - `mptoken`

**Current workaround** (already implemented):
- `InstanceService.CopyPlayerSessionToInstance()` copies these fields from source data directory
- This means: user logs in once via the game, then session is shared to instances

**Why no app-level auth**:
- VS auth server is not documented for third-party use
- No OAuth endpoints or API tokens available
- The game client handles login internally
- Other mod managers (Rustique, Vintage Launcher) also don't implement direct auth

**Alternatives considered**:
1. **Browser-based login** - Would need to scrape/intercept (fragile, potentially against ToS)
2. **Ask user to paste session token** - Poor UX, security concerns
3. **Current approach** - Copy session from existing game data (what you're doing now)

**Recommendation**: Keep current session-copying approach. It's the most reliable and what other tools do.

---

## Notes

- The app already launches with `--dataPath`, so the core mechanism exists
- `ModDiscoveryService` already supports multiple search paths
- `DownloadModAsync` in `ModApiService` already handles mod downloads
- Instance system can coexist with profiles during transition

---

## Related Existing Code

| Component | File | Relevance |
|-----------|------|-----------|
| Launch with dataPath | `MainWindow.xaml.cs:8139` | Already uses `--dataPath` |
| Mod discovery | `ModDiscoveryService.cs:540-559` | `BuildSearchPaths()` needs instance path |
| Download mods | `ModApiService.cs:55-59` | `DownloadModAsync` destination |
| Profile state | `UserConfigurationService.cs:3955` | `GameProfileState` as reference |
| Mod browser install | `ModBrowserViewModel.cs` | Install destination path |

---

## 📋 RECOMMENDED NEXT STEPS (Priority Order)

### ✅ COMPLETED
1. ~~**Fix instance activation bug**~~ ✅
2. ~~**Fix app startup not loading last instance**~~ ✅
3. ~~**Instance Browser UI** (5.1)~~ ✅ - Card-based view with launch/details
4. ~~**Automatic Dependency Resolution** (G.2)~~ ✅ - Auto-install deps, "Fix All" menu
5. ~~**Instance Edit Dialog** (2.2)~~ ✅ - Edit name, icon, notes, view game version
6. ~~**Instance Duplication**~~ ✅ - Full folder copy with background thread
7. ~~**Context menu theming**~~ ✅ - Override ModernWpf styles
8. ~~**Button theming**~~ ✅ - Use IMM.ButtonBaseStyle
9. ~~**Instances tab as default**~~ ✅ - Opens on launch

### Next Up
10. **Fix: Deleting active instance** - Switch away properly when deleting
11. **Instance Rename** from UI (name edit works in dialog, but no dedicated rename)
12. **UI indicator** - Show "Install to [Instance Name]" somewhere visible

### Medium-term
13. **Base Mods Directory** (5.2) - Shared mods for new instances
14. **Version compatibility warnings** - Warn if mod incompatible with VS version

### Long-term (Nice to Have)
15. **Mod Health/Diagnostics** (G.1) - Batch scan and repair
16. **Modpack Import/Export** (4.2)
17. **Migration Wizard** - Convert profiles to instances
18. **Shared Mod Library** (4.1) - Central cache

### Maybe (Complex)
19. **Auto-download game versions** - Requires VS account auth, may not be feasible
