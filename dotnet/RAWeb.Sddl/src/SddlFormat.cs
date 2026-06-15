using System;
using System.Collections.Generic;
using System.Text;

namespace RAWeb.Sddl;

/// <summary>
/// Helpers for parsing and formatting the small subset of SDDL syntax
/// (https://learn.microsoft.com/windows/win32/secauthz/security-descriptor-string-format)
/// needed by <see cref="RawSecurityDescriptor"/> and <see cref="CommonAce"/>.
/// </summary>
internal static class SddlFormat {
  /// <summary>
  /// SDDL two-letter ACE flag abbreviations and their corresponding bit values.
  /// </summary>
  private static readonly Dictionary<string, AceFlags> AceFlagLetters = new(StringComparer.OrdinalIgnoreCase) {
    ["CI"] = AceFlags.ContainerInherit,
    ["OI"] = AceFlags.ObjectInherit,
    ["NP"] = AceFlags.NoPropagateInherit,
    ["IO"] = AceFlags.InheritOnly,
    ["ID"] = AceFlags.Inherited,
    ["SA"] = AceFlags.SuccessfulAccess,
    ["FA"] = AceFlags.FailedAccess,
  };

  /// <summary>
  /// SDDL two-letter access right abbreviations and their corresponding bit values.
  /// See https://learn.microsoft.com/windows/win32/secauthz/access-mask-format
  /// </summary>
  private static readonly Dictionary<string, int> AccessRightLetters = new(StringComparer.OrdinalIgnoreCase) {
    ["GA"] = unchecked((int)0x10000000), // GENERIC_ALL
    ["GR"] = unchecked((int)0x80000000), // GENERIC_READ
    ["GW"] = 0x40000000, // GENERIC_WRITE
    ["GX"] = 0x20000000, // GENERIC_EXECUTE
    ["RC"] = 0x00020000, // READ_CONTROL
    ["SD"] = 0x00010000, // DELETE
    ["WD"] = 0x00040000, // WRITE_DAC
    ["WO"] = 0x00080000, // WRITE_OWNER
    ["FA"] = 0x001F01FF, // FILE_ALL_ACCESS
    ["FR"] = 0x00120089, // FILE_GENERIC_READ
    ["FW"] = 0x00120116, // FILE_GENERIC_WRITE
    ["FX"] = 0x001200A0, // FILE_GENERIC_EXECUTE
  };

  public static AceFlags ParseAceFlags(string flagsString) {
    var flags = AceFlags.None;
    for (var i = 0; i < flagsString.Length; i += 2) {
      if (i + 2 > flagsString.Length) {
        throw new FormatException($"'{flagsString}' is not a valid ACE flags string.");
      }
      var letters = flagsString.Substring(i, 2);
      if (!AceFlagLetters.TryGetValue(letters, out var flag)) {
        throw new FormatException($"'{letters}' is not a recognized ACE flag.");
      }
      flags |= flag;
    }
    return flags;
  }

  public static string FormatAceFlags(AceFlags flags) {
    var sb = new StringBuilder();
    foreach (var (letters, flag) in AceFlagLetters) {
      if (flags.HasFlag(flag)) {
        sb.Append(letters);
      }
    }
    return sb.ToString();
  }

  public static int ParseAccessMask(string maskString) {
    if (string.IsNullOrEmpty(maskString)) {
      return 0;
    }

    if (maskString.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
      return unchecked((int)Convert.ToUInt32(maskString[2..], 16));
    }

    if (int.TryParse(maskString, out var numeric)) {
      return numeric;
    }

    // a concatenation of two-letter generic/standard access right abbreviations, e.g. "GRGW"
    var mask = 0;
    for (var i = 0; i < maskString.Length; i += 2) {
      if (i + 2 > maskString.Length) {
        throw new FormatException($"'{maskString}' is not a valid access mask.");
      }
      var letters = maskString.Substring(i, 2);
      if (!AccessRightLetters.TryGetValue(letters, out var right)) {
        throw new FormatException($"'{letters}' is not a recognized access right.");
      }
      mask |= right;
    }
    return mask;
  }

  public static string FormatAccessMask(int mask) {
    return "0x" + ((uint)mask).ToString("x");
  }

  /// <summary>
  /// SDDL ACL flag letters for the D: and S: sections.
  /// </summary>
  private static readonly Dictionary<string, (ControlFlags Dacl, ControlFlags Sacl)> AclFlagLetters = new(StringComparer.OrdinalIgnoreCase) {
    ["P"] = (ControlFlags.DiscretionaryAclProtected, ControlFlags.SystemAclProtected),
    ["AR"] = (ControlFlags.DiscretionaryAclAutoInheritRequired, ControlFlags.SystemAclAutoInheritRequired),
    ["AI"] = (ControlFlags.DiscretionaryAclAutoInherited, ControlFlags.SystemAclAutoInherited),
  };

  public static ControlFlags ParseAclFlags(bool isSacl, string flagsString) {
    var flags = ControlFlags.None;
    var remaining = flagsString;
    while (remaining.Length > 0) {
      var matched = false;
      foreach (var (letters, value) in AclFlagLetters) {
        if (remaining.StartsWith(letters, StringComparison.OrdinalIgnoreCase)) {
          flags |= isSacl ? value.Sacl : value.Dacl;
          remaining = remaining[letters.Length..];
          matched = true;
          break;
        }
      }
      if (!matched) {
        throw new FormatException($"'{flagsString}' is not a valid ACL flags string.");
      }
    }
    return flags;
  }

  public static string FormatAclFlags(bool isSacl, ControlFlags flags) {
    var sb = new StringBuilder();
    foreach (var (letters, value) in AclFlagLetters) {
      var flag = isSacl ? value.Sacl : value.Dacl;
      if (flags.HasFlag(flag)) {
        sb.Append(letters);
      }
    }
    return sb.ToString();
  }
}
