using System;

namespace RAWeb.Sddl;

/// <summary>
/// A minimal, portable subset of <see cref="System.Security.AccessControl.FileSystemRights"/>
/// covering the access mask bits used by managed resource security descriptors.
/// </summary>
[Flags]
public enum FileAccessRights {
  ReadData = 0x1,
}
