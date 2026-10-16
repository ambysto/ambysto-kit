using System;
using System.Collections.Generic;

namespace Ambysto.Kit.Safety;

/// <summary>A file or folder's identity on Windows: the volume serial and the 128-bit file id (docs/spec/safety.md, "Unchanged since shown").</summary>
/// <param name="VolumeSerial">The 64-bit serial of the volume that holds the item.</param>
/// <param name="FileId">The 128-bit file id within that volume.</param>
public readonly record struct FileIdentity(ulong VolumeSerial, UInt128 FileId);

/// <summary>What an app recorded about an item when it showed it, and what the gate compares before a move.</summary>
/// <param name="Identity">Volume serial and file id.</param>
/// <param name="LastWriteTimeUtcTicks">Last-write time as a Windows FILETIME: 100 ns intervals since 1601-01-01 UTC.</param>
/// <param name="Length">The file's length in bytes; null for a folder.</param>
public readonly record struct FileStamp(FileIdentity Identity, long LastWriteTimeUtcTicks, long? Length);
