using System;

namespace RAWeb.Sddl;

/// <summary>
/// Flags describing the presence and properties of the ACLs in a
/// <see cref="RawSecurityDescriptor"/>.
/// See https://learn.microsoft.com/windows/win32/api/winnt/ns-winnt-security_descriptor
/// </summary>
[Flags]
public enum ControlFlags {
  None = 0x0000,
  OwnerDefaulted = 0x0001,
  GroupDefaulted = 0x0002,
  DiscretionaryAclPresent = 0x0004,
  DiscretionaryAclDefaulted = 0x0008,
  SystemAclPresent = 0x0010,
  SystemAclDefaulted = 0x0020,
  DiscretionaryAclAutoInheritRequired = 0x0100,
  SystemAclAutoInheritRequired = 0x0200,
  DiscretionaryAclAutoInherited = 0x0400,
  SystemAclAutoInherited = 0x0800,
  DiscretionaryAclProtected = 0x1000,
  SystemAclProtected = 0x2000,
  SelfRelative = 0x8000,
}
