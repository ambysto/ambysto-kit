# File safety

Platform-neutral rules shared by every Ambysto app that moves files to the Trash. The macOS implementation lives in this repository (see the macOS note), the Windows one in `windows/` (see the Windows sections). Apps never delete permanently through these rules; each app keeps its own single deletion point.

"The Trash" is the macOS Trash or, on Windows, the user's Recycle Bin. **A move must be recoverable**: an item whose move would not land in the Trash is refused before the move. On macOS the Trash always keeps what it is given; on Windows it does not, and "Windows: recoverable moves" lists what is refused.

## Never touch

Refused at selection **and** immediately before every move, including everything beneath, and refused when the item **contains** one of them (a folder holding protected data):

| Home-relative (macOS) | What it is | Windows note |
|---|---|---|
| `.claude/projects` | coding-agent transcripts | `%USERPROFILE%\.claude\projects` |
| `.ssh`, `.gnupg` | keys | same under `%USERPROFILE%` |
| `.Trash` | the Trash (reading its size is allowed) | `$Recycle.Bin` on every volume |
| `Pictures`, any `*.photoslibrary` | photos | the Pictures known folder |
| `Library/Keychains`, any `*.keychain`/`*.keychain-db` | credentials | `%APPDATA%\Microsoft\{Protect, Credentials, Crypto, SystemCertificates}`, `%LOCALAPPDATA%\Microsoft\{Credentials, Vault}`; the user's registry hives `NTUSER.DAT*`, `UsrClass.dat*`; any `*.kdbx` password vault |
| `Library/Mail`, `Library/Messages` | mail and messages | `%LOCALAPPDATA%\Microsoft\Outlook`, any `*.pst` / `*.ost`, Thunderbird profiles |
| `Library/Mobile Documents`, `Library/CloudStorage` | iCloud Drive and other cloud drives (deleting there deletes in the cloud) | every cloud-sync root: the sync roots registered with the system for this user, `%OneDrive%`, `%OneDriveConsumer%`, `%OneDriveCommercial%`, and these folders directly in the profile: `Dropbox`, `Google Drive`, `My Drive`, `OneDrive`, `OneDrive - *`, `Box`, `Box Sync`, `pCloud Drive`, `iCloudDrive`, `iCloud Drive` |
| `Library/Calendars`, `Library/Accounts`, `Library/Application Support/AddressBook` | personal data | Outlook data (above) |
| `Library/Application Support/MobileSync/Backup`, `Backups.backupdb`, `*.backupbundle` | device and Time Machine backups | `%APPDATA%\Apple Computer\MobileSync\Backup`, `%USERPROFILE%\Apple\MobileSync\Backup`, File History (`%LOCALAPPDATA%\Microsoft\Windows\FileHistory`, and any `FileHistory` folder at a volume root), any `WindowsImageBackup` folder |
| `Library/Application Support/com.apple.TCC` | the privacy database | none |
| main browser profiles (Safari, Chrome, Arc, Firefox, Brave, Edge) | browsing data | Chrome, Edge, Brave under `%LOCALAPPDATA%`, Firefox under `%APPDATA%\Mozilla`, Arc under `%LOCALAPPDATA%\Packages` |
| any child of `Library/Containers`, `Library/Group Containers` or `Library/Application Support` named `com.apple.*` or `group.com.apple.*` | Apple app data | Store app data: `%LOCALAPPDATA%\Packages` |

System locations (`/System`, `/Library`, `/private`, `/usr`, `/opt`, `/cores`, `/bin`, `/sbin`, `/Volumes`) and anything the system marks restricted are never changed. Windows note: `%SystemRoot%`, `Program Files`, `Program Files (x86)`, `ProgramData`, `Recovery`, `Windows.old`, `$WINDOWS.~BT`, `%LOCALAPPDATA%\Microsoft\WindowsApps`, and on every volume `$Recycle.Bin`, `System Volume Information`, `pagefile.sys`, `hiberfil.sys`, `swapfile.sys`; "restricted" is anything owned by TrustedInstaller or SYSTEM or protected by Windows Resource Protection. Every Windows location is resolved through the system's known folders and environment, never from a fixed drive letter, so a redirected folder is still covered.

Apps **add** their own data folders (for example Space's journal folder) to this list; nothing can remove an entry.

Windows note: a virtual-machine disk (`*.vhdx`, `*.vhd`) in an app's allowed root is not protected, like a VM image on macOS.

Windows note, the facts an allowed-root rule may use: the item's canonical path, the profile folder, the profiles folder, the item's volume and the profile's volume, whether the item is in a system location, and for the item and every folder above it its owner and whether that owner is the current user. "Owned by the current user" means the user's own account only: a folder owned by `Administrators` (made from an elevated prompt or by an installer) is not.

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

Windows note: the order on Windows, the first failure refusing the item:
1. **Environment**: not elevated, not SYSTEM, an interactive session (the counterpart of the macOS Full Disk Access check).
2. **Exists**, read without opening first (the spelling rules of step 4 are applied to the given path after step 1 and before this step, so a network path to a missing file is refused for its form, not as gone); an item marked "recall on open" is never opened and goes straight to the cloud refusal (step 9), the one exception to this order. Otherwise it is opened for attributes only, never for data.
3. **Not a link or special file**: the item has no reparse point, or a cloud one (step 9); any other kind (symbolic link, junction, mount point, app execution alias, deduplication, projection, unknown) is refused. A compressed (WOF) file is not refused, whether or not Windows shows its tag (see "Windows: recoverable moves").
4. **Spelling**: network (UNC), device and stream paths and names ending in a dot or a space are refused, on the given path before step 2 and again on the canonical path (a mapped drive resolves to a network path). The canonical path is the plain form with short (8.3) names expanded and no `\\?\` prefix.
5. **Volume**: a fixed local NTFS or ReFS volume, compared by volume serial, never by drive letter; removable and network volumes are refused (the Recycle Bin does not keep their items), and FAT and exFAT are refused (no stable file identity).
6. **Unchanged since shown**: same volume serial and 128-bit file id, same last-write time, and for files the same length.
7. **Measure** the item now, walking a folder without entering reparse points and without opening any file for data. A cloud-tagged folder inside the item is not entered either, and the item is refused as a cloud file (step 9).
8. **Recoverable**: see "Windows: recoverable moves".
9. **Cloud**: see "Windows: recoverable moves"; an online-only or partly downloaded file (or a folder holding one) is checked before the cloud-sync roots.
10. Not a volume root, the profile folder, the profiles folder, another user's profile, `Public` or `Default`. This is a check on the canonical path alone and runs **right after step 5**, so a drive or a profile is never walked; the steps keep their numbers.
11. **Protection**: as step 7 above, by path and by identity (volume serial + file id) of the item and every folder above it; neither the item nor anything in it owned by TrustedInstaller or SYSTEM or protected by Windows Resource Protection.
12. **Allowed roots**: as step 8 above.

## Windows: recoverable moves

Measured on Windows 11 (2026-10-16): moving an item to the Recycle Bin through the shell can delete it for good while the shell reports success. So the gate refuses, before the move:
- an item not on a fixed local NTFS or ReFS volume (removable and network items are deleted for good);
- an item on a volume whose Recycle Bin is off for this user or disallowed by policy (an absent setting means the system default; a setting that cannot be read refuses);
- an item larger than the Recycle Bin's whole size on that volume ("too large"); otherwise an item larger than the room left in it **without** purging older items ("Recycle Bin full": the shell silently deletes older items for good to make room). Measured: sizes are logical lengths (not the space on disk), the size setting is in MiB, and the Recycle Bin and its room are this user's own. When the size setting is absent, the shell writes its default on its first query of that volume's Recycle Bin; the gate lets it do so and reads the value, and a value still absent after that refuses;
- an item with any path in its tree of 260 characters or more, measured on the canonical path (step 4). Measured: such a tree is deleted for good, short files included, on a volume without short names, and on any volume when the moving process is long-path-aware with long paths turned on; elsewhere it was kept through its short names. It is refused everywhere: the gate cannot know how the mover will run;
- an online-only or partially downloaded cloud file, and a folder holding one (recycling the folder would download it first; an online-only file is deleted for good). Two signals, either refuses: the "recall on data access" or "recall on open" attributes, and fewer bytes on disk than the file's length;
- an item inside, or containing, a cloud-sync root (Never touch): deleting there deletes on every device.

Inside a folder, a link, junction or mount point is never entered. Measured (2026-10-16): recycling a folder that holds a junction or a symbolic link leaves the target untouched, the link lands in the Recycle Bin as a link and comes back with its target; so such a folder is allowed. A folder holding a **volume mount point** is refused ("contains a link or special file"): the Recycle Bin counts the mounted volume's whole capacity as part of the folder, so the folder can be deleted for good. A compressed (WOF) file, on its own or in a folder, recycles and comes back intact and is allowed; Windows shows it to a standard user as a plain file. Any other special file inside a folder (a reparse kind that is not a link, junction, mount point, cloud placeholder or WOF file) refuses the folder the same way as a mount point: what the Recycle Bin does with it is not known.

Within step 8 the checks run in this order, the first failure refusing: the Recycle Bin off or disallowed; a link or special file inside (below); a path of 260 characters or more; too large; Recycle Bin full.

What the gate cannot close (between the gate and the move another process may fill the Recycle Bin, or a cloud provider may free the file's space) is the consumer's duty:
- run the gate immediately before each single item, never for a batch;
- to keep a cloud provider from freeing a file's space during the move, hold the file open through the Cloud Files oplock open while moving it (measured: it makes the provider's free-up fail, "cloud file in use", and the recycle still lands; an attributes-only handle does not);
- abort the shell's delete whenever any pre-delete callback, the item's or a descendant's, does not say "recycle if possible";
- after the move, open the item the shell reports in the Recycle Bin and compare volume serial, file id and size with the approved item; when they do not match, or the shell reports none, the item is treated as not recovered and the session stops.

## Windows: failing closed

The gate writes nothing. The one side effect it allows is the shell's own: asking for a volume's Recycle Bin size may make the shell create that user's Recycle Bin folder and size setting on the volume.

Every check that cannot establish its fact (an error, access denied, an unknown file system or reparse kind, an unreadable setting) refuses the item with "Could not verify: <check>". No check passes on an error.

## Windows: refusal reasons

| Reason | Message |
|---|---|
| not an interactive session | "Moving is off outside your desktop session." |
| elevated or SYSTEM | "Moving is off while running as administrator." |
| gone | "Already gone" |
| link or special file | "A link or special file: never followed." |
| contains a link or special file | "Holds a mounted drive or a special file the Recycle Bin may not keep." |
| path form | "Network, device and stream paths are not supported." |
| volume | "Only fixed NTFS or ReFS drives are supported." |
| changed | "Changed since the scan" |
| Recycle Bin off | "The Recycle Bin is off for this drive." |
| Recycle Bin full | "The Recycle Bin is full: make room in it or raise its size, then try again." |
| too large | "Too large for the Recycle Bin on this drive." |
| path too long | "A path inside is 260 characters or longer; the Recycle Bin would not keep it." |
| cloud file | "Not fully on this PC: a cloud file that is online only or partly downloaded, or a folder holding one." |
| cloud-sync folder | "Inside a cloud-synced folder: deleting here deletes on every device." |
| root or profile | "A drive, a profile or the profiles folder itself is never moved." |
| protected | "Protected location" |
| system-owned | "Owned by Windows" |
| destination exists | "Something already exists at the original location" |
| destination folder gone | "The original folder no longer exists or moved" |
| not allowed | the app's own text |
| could not verify | "Could not verify: <check>" |

## Putting back

A destination that does not exist yet passes: its folder exists; protection (step 7, by path and by identity of every existing folder above it); and the app's separate restore rule (a restore is judged differently from a delete: the item is not there yet).

Windows note: putting back is a move too, so the environment check (Windows step 1) runs first; identity is volume serial + file id. A destination that already exists is refused ("destination exists"), and so is one whose folder is gone ("destination folder gone"); a destination inside a cloud-sync root is refused as a cloud-sync folder. The restore move itself is the app's, never the shell's restore, which blocks on a dialog when the destination is taken.

## In use

An item is in use if any readable process has its executable, its working directory, or a command-line argument inside it (relative arguments resolved against that process's working directory), or has a file inside it open or mapped into memory (mapped files from a snapshot refreshed after 10 s). Files are matched by identity as well as by path. Processes that cannot be read are counted and reported; they never block.

Windows note: an item is in use when **any** of these names a holder, for every file and folder in its tree: the per-file query of processes using a file (it names open files, loaded libraries, mapped views and a folder used as a current directory), the Restart Manager (it also names elevated and other-session holders, and is used only to list, never to stop or restart), and, where readable, a process's image, current directory and command line. An unreadable holder that is named still blocks; processes that cannot be read at all are counted and reported. Antivirus scanning a file is a real holder and is reported like any other.

macOS note: `SafetyGate`, `AllowedRootRule`, `SafetyContext`, `NeverTouch`, `PathCanonicalizer`, `FileStamp`, `InUseChecker`, `FullDiskAccess` in `AmbystoSafety`; Full Disk Access is detected by reading the privacy database (readable only with it).
