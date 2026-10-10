# File safety

Platform-neutral rules shared by every Ambysto app that moves files to the Trash. The macOS implementation lives in this repository (see the macOS note). Apps never delete permanently through these rules; each app keeps its own single deletion point.

## Never touch

Refused at selection **and** immediately before every move, including everything beneath, and refused when the item **contains** one of them (a folder holding protected data):

| Home-relative (macOS) | What it is | Windows note |
|---|---|---|
| `.claude/projects` | coding-agent transcripts | `%USERPROFILE%\.claude\projects` |
| `.ssh`, `.gnupg` | keys | same under `%USERPROFILE%` |
| `.Trash` | the Trash (reading its size is allowed) | the Recycle Bin |
| `Pictures`, any `*.photoslibrary` | photos | `Pictures` |
| `Library/Keychains`, any `*.keychain`/`*.keychain-db` | credentials | Credential Manager store |
| `Library/Mail`, `Library/Messages` | mail and messages | mail client stores |
| `Library/Mobile Documents`, `Library/CloudStorage` | iCloud Drive and other cloud drives (deleting there deletes in the cloud) | OneDrive, Dropbox, Google Drive folders |
| `Library/Calendars`, `Library/Accounts`, `Library/Application Support/AddressBook` | personal data | Outlook data |
| `Library/Application Support/MobileSync/Backup`, `Backups.backupdb`, `*.backupbundle` | device and Time Machine backups | File History |
| `Library/Application Support/com.apple.TCC` | the privacy database | — |
| main browser profiles (Safari, Chrome, Arc, Firefox, Brave, Edge) | browsing data | `%LOCALAPPDATA%` browser profiles |
| any child of `Library/Containers`, `Library/Group Containers` or `Library/Application Support` named `com.apple.*` or `group.com.apple.*` | Apple app data | — |

System locations (`/System`, `/Library`, `/private`, `/usr`, `/opt`, `/cores`, `/bin`, `/sbin`, `/Volumes`) and anything the system marks restricted are never changed. Windows note: `C:\Windows`, `C:\Program Files*`, `C:\ProgramData`.

Apps **add** their own data folders (for example Space's journal folder) to this list; nothing can remove an entry.

## The gate

Immediately before every move, in this order; the first failure refuses the item with its reason:

1. The item exists ("Already gone").
2. It is not a symbolic link (links are never followed).
3. Its canonical path is known: symlinks resolved, letter case normalised, and on macOS firmlinked spellings collapsed (`/System/Volumes/Data/Users/x` is `/Users/x`).
4. It is unchanged since it was shown: same device, inode and modification time, and for files the same length ("Changed since the scan").
5. It is on the same volume as the home folder.
6. It is not the root or the home folder.
7. **Protection** (shared): not inside a system location; not inside, and not containing, a never-touch entry — compared both by path and by file identity (device + inode) of the item and of every folder above it, so no alternative spelling slips past; no protected component (photo library, keychain, backup) in its path; not Apple app data; not restricted.
8. **Allowed roots** (the app's rule): whether this app may change this location at all. The rule runs only after protection passed, so no rule can widen protection.

## Putting back

A destination that does not exist yet passes: its folder exists; protection (step 7, by path and by identity of every existing folder above it); and the app's separate restore rule (a restore is judged differently from a delete: the item is not there yet).

## In use

An item is in use if any readable process has its executable, its working directory, or a command-line argument inside it (relative arguments resolved against that process's working directory), or has a file inside it open or mapped into memory (mapped files from a snapshot refreshed after 10 s). Files are matched by identity as well as by path. Processes that cannot be read are counted and reported; they never block.

Windows note: open handles via the Restart Manager API; working directories and command lines from the process environment block where readable.

macOS note: `SafetyGate`, `AllowedRootRule`, `SafetyContext`, `NeverTouch`, `PathCanonicalizer`, `FileStamp`, `InUseChecker`, `FullDiskAccess` in `AmbystoSafety`; Full Disk Access is detected by reading the privacy database (readable only with it).
