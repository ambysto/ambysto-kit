#!/usr/bin/env bash
# The Windows safety gate decides and never changes anything (docs/spec/safety.md, "Windows: failing closed": "The gate
# writes nothing"). This tripwire fails when the library's source names an API or access right that writes, moves,
# deletes, reads file data, changes cloud state, writes the registry or starts a process. A tripwire, not a proof.
# Usage: scripts/windows-choke-points.sh [source-dir]   (default: windows/src/Ambysto.Kit.Safety)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
SRC="${1:-$ROOT/windows/src/Ambysto.Kit.Safety}"
[ -d "$SRC" ] || { echo "FAIL: no source folder $SRC"; exit 1; }
PATTERNS=(
  '\bFile\.(Create|Delete|Move|Copy|Replace|Write[A-Za-z]*|Append[A-Za-z]*|Open[A-Za-z]*|ReadAll[A-Za-z]*|SetAttributes|Encrypt|Decrypt)\b'
  '\bDirectory\.(Create[A-Za-z]*|Delete|Move)\b'
  '\b(FileInfo|DirectoryInfo)\b'
  '\bFileStream\b|\bFileAccess\.(Read|Write|ReadWrite)\b|\bFileMode\.'
  '\b(Move|Copy|Delete|Replace)File[A-Za-z]*\b|\bRemoveDirectory[A-Za-z]*\b|\bCreateDirectory[A-Za-z]*\b'
  '\bIFileOperation\b|\bSHFileOperation[A-Za-z]*\b|\bSHEmptyRecycleBin[A-Za-z]*\b'
  '\bSetFileInformationByHandle\b|\bDeviceIoControl\b|\bSetFileAttributes[A-Za-z]*\b|\bWriteFile\b'
  '\b(FILE_READ_DATA|FILE_WRITE_[A-Z_]+|FILE_APPEND_DATA|GENERIC_(READ|WRITE|ALL|EXECUTE)|WRITE_DAC|WRITE_OWNER)\b'
  '\bDELETE\b'
  '\b(CREATE_ALWAYS|CREATE_NEW|OPEN_ALWAYS|TRUNCATE_EXISTING)\b'
  '\.(SetValue|CreateSubKey|DeleteValue|DeleteSubKey[A-Za-z]*|SetAccessControl)\s*\('
  'OpenSubKey\([^)]*(writable:\s*true|,\s*true\s*\))'
  '\bProcess\.Start\b|\bProcessStartInfo\b|\.Kill\s*\(|\bTerminateProcess\b|\bCreateProcess[A-Za-z]*\b'
  '\bRm(Shutdown|Restart|AddFilter|RemoveFilter)\b'
  '\bCf(HydratePlaceholder|DehydratePlaceholder|SetPinState|SetInSyncState|ConvertToPlaceholder|UpdatePlaceholder|RevertPlaceholder|CreatePlaceholders|Execute|RegisterSyncRoot|UnregisterSyncRoot|ConnectSyncRoot)\b'
  '\bWriteProcessMemory\b|\bVirtualAllocEx\b|\bCreateRemoteThread\b'
)
hits=0
for p in "${PATTERNS[@]}"; do
  if out=$(grep -rnE --include='*.cs' "$p" "$SRC" | grep -vE '^[^:]+:[0-9]+:\s*//' ); then
    echo "$out"; hits=$((hits + $(echo "$out" | wc -l)))
  fi
done
if [ "$hits" -gt 0 ]; then echo "FAIL: $hits forbidden use(s) in $SRC"; exit 1; fi
echo "PASS: no forbidden use in $SRC"
