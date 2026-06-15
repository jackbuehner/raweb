using System;

namespace RAWeb.Sddl;

/// <summary>
/// The type of a <see cref="CommonAce"/>. Only the two types needed for
/// discretionary access control entries are represented.
/// </summary>
public enum AceType {
  AccessAllowed = 0,
  AccessDenied = 1,
}

/// <summary>
/// Flags describing inheritance and auditing behavior of an ACE.
/// See https://learn.microsoft.com/windows/win32/secauthz/security-descriptor-string-format
/// </summary>
[Flags]
public enum AceFlags {
  None = 0x00,
  ObjectInherit = 0x01,
  ContainerInherit = 0x02,
  NoPropagateInherit = 0x04,
  InheritOnly = 0x08,
  Inherited = 0x10,
  SuccessfulAccess = 0x40,
  FailedAccess = 0x80,
}

/// <summary>
/// A portable representation of an access control entry (ACE) that grants or
/// denies a set of rights to a <see cref="SecurityIdentifier"/>.
/// </summary>
public sealed class CommonAce {
  public AceType AceType { get; }
  public AceFlags AceFlags { get; }
  public int AccessMask { get; }
  public SecurityIdentifier SecurityIdentifier { get; }

  public CommonAce(AceType aceType, AceFlags aceFlags, int accessMask, SecurityIdentifier securityIdentifier) {
    AceType = aceType;
    AceFlags = aceFlags;
    AccessMask = accessMask;
    SecurityIdentifier = securityIdentifier;
  }

  internal string ToSddlString() {
    var typeLetters = AceType switch {
      AceType.AccessAllowed => "A",
      AceType.AccessDenied => "D",
      _ => throw new NotSupportedException($"ACE type '{AceType}' cannot be represented in SDDL."),
    };
    var flagsLetters = SddlFormat.FormatAceFlags(AceFlags);
    var rights = SddlFormat.FormatAccessMask(AccessMask);
    return $"({typeLetters};{flagsLetters};{rights};;;{SecurityIdentifier.Value})";
  }

  internal static CommonAce Parse(string aceString) {
    var parts = aceString.Split(';');
    if (parts.Length != 6) {
      throw new FormatException($"'{aceString}' is not a valid ACE string.");
    }

    var aceType = parts[0].Trim().ToUpperInvariant() switch {
      "A" => AceType.AccessAllowed,
      "D" => AceType.AccessDenied,
      var other => throw new FormatException($"'{other}' is not a supported ACE type."),
    };

    var aceFlags = SddlFormat.ParseAceFlags(parts[1].Trim());
    var accessMask = SddlFormat.ParseAccessMask(parts[2].Trim());
    var sid = new SecurityIdentifier(parts[5].Trim());

    return new CommonAce(aceType, aceFlags, accessMask, sid);
  }
}
