#!/usr/bin/env bash
# The Windows safety gate and process reader never change, terminate, signal, suspend or spawn anything
# (docs/spec/safety.md, "Windows: failing closed"; docs/spec/process.md, "What is not here"). This tripwire fails when
# the libraries' source names an API or access right that writes, moves, deletes, reads file data, changes cloud state,
# writes the registry, starts a process, or terminates/signals/suspends/modifies a process. A tripwire, not a proof.
# Usage: scripts/windows-choke-points.sh [source-dir ...]
#        (default: windows/src/Ambysto.Kit.Safety and windows/src/Ambysto.Kit.Process)
set -euo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [ "$#" -eq 0 ]; then
  SRCS=("$ROOT/windows/src/Ambysto.Kit.Safety" "$ROOT/windows/src/Ambysto.Kit.Process")
else
  SRCS=("$@")
fi
for SRC in "${SRCS[@]}"; do
  [ -d "$SRC" ] || { echo "FAIL: no source folder $SRC"; exit 1; }
done
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
  '\bWriteProcessMemory\b|\bVirtualAllocEx\b|\bCreateRemoteThread\b|\b(Nt|Zw)TerminateProcess\b'
  '\b(PROCESS_(TERMINATE|VM_WRITE|VM_OPERATION|CREATE_THREAD|DUP_HANDLE|SET_INFORMATION|SET_QUOTA|SUSPEND_RESUME|ALL_ACCESS))\b'
  '\b(GenerateConsoleCtrlEvent|DebugActiveProcess(Stop)?|SuspendThread|(Nt|Zw)(Suspend|Resume)Process|(Post|Send)(Thread)?Message[A-Za-z]*)\b'
  '\b(FILE_GENERIC_(READ|WRITE|EXECUTE)|FILE_ALL_ACCESS|MAXIMUM_ALLOWED|STANDARD_RIGHTS_(WRITE|ALL|REQUIRED)|ACCESS_SYSTEM_SECURITY)\b'
  '\b(Nt|Zw)(SetInformationFile|WriteFile|CreateFile|OpenFile|DeleteFile|SetSecurityObject|FsControlFile)\b'
  '\bReg(Set|Create|Delete|Save|Restore|Replace|Load|Unload|Rename|Copy)[A-Za-z]*\b|\bRegistryKeyPermissionCheck\.ReadWrite|\bRegistry\.SetValue\b'
  '\bSetFileTime\b|\bCreateHardLink[A-Za-z]*\b|\bCreateSymbolicLink[A-Za-z]*\b|\bSet(Named)?SecurityInfo[A-Za-z]*\b|\bSetKernelObjectSecurity\b|\bSetFileSecurity[A-Za-z]*\b'
  '\b(StreamReader|StreamWriter|BinaryReader|BinaryWriter)\b|\bFile\.ReadLines\b|\bReadFile(Ex|Scatter)?\b|\bMemoryMappedFile\b'
  '\bSHSetKnownFolderPath\b|\bSHChangeNotify\b|\bShellExecute[A-Za-z]*\b'
)
hits=0
for SRC in "${SRCS[@]}"; do
  for p in "${PATTERNS[@]}"; do
    if out=$(grep -rnE --include='*.cs' "$p" "$SRC" | grep -vE '^[^:]+:[0-9]+:\s*//' ); then
      echo "$out"; hits=$((hits + $(echo "$out" | wc -l)))
    fi
  done
done
if [ "$hits" -gt 0 ]; then echo "FAIL: $hits forbidden use(s) in ${SRCS[*]}"; exit 1; fi
echo "PASS: no forbidden use in ${SRCS[*]}"
