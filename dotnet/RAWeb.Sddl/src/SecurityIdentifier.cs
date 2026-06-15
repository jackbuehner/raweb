using System;
using System.Collections.Generic;

namespace RAWeb.Sddl;

/// <summary>
/// A portable representation of a Windows security identifier (SID).
/// <br /><br />
/// Unlike <see cref="System.Security.Principal.SecurityIdentifier"/>, this type
/// only deals with the textual SDDL representation of a SID (e.g.
/// <c>S-1-5-21-...</c> or a two-letter SDDL alias such as <c>BA</c>) and performs
/// no OS interop, so it can be used on any platform.
/// </summary>
public sealed class SecurityIdentifier : IEquatable<SecurityIdentifier> {
  /// <summary>
  /// The canonical <c>S-1-...</c> string form of this SID.
  /// </summary>
  public string Value { get; }

  public SecurityIdentifier(string sddlForm) {
    Value = Parse(sddlForm);
  }

  private static string Parse(string sddlForm) {
    if (string.IsNullOrWhiteSpace(sddlForm)) {
      throw new ArgumentException("The SID string cannot be null or empty.", nameof(sddlForm));
    }

    var trimmed = sddlForm.Trim();

    if (trimmed.StartsWith("S-1-", StringComparison.OrdinalIgnoreCase)) {
      var parts = trimmed.Split('-');
      // S-<revision>-<authority>-<sub authorities...>
      if (parts.Length < 3) {
        throw new FormatException($"'{sddlForm}' is not a valid security identifier.");
      }
      for (var i = 1; i < parts.Length; i++) {
        if (!long.TryParse(parts[i], out _)) {
          throw new FormatException($"'{sddlForm}' is not a valid security identifier.");
        }
      }
      return trimmed.ToUpperInvariant();
    }

    if (WellKnownSidAliases.TryGetValue(trimmed, out var aliased)) {
      return aliased;
    }

    throw new FormatException($"'{sddlForm}' is not a recognized SID or SDDL SID alias.");
  }

  public override string ToString() => Value;

  public bool Equals(SecurityIdentifier? other) {
    return other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);
  }

  public override bool Equals(object? obj) => Equals(obj as SecurityIdentifier);

  public override int GetHashCode() => Value.GetHashCode(StringComparison.OrdinalIgnoreCase);

  /// <summary>
  /// SDDL two-letter SID aliases for commonly used well-known SIDs.
  /// See https://learn.microsoft.com/windows/win32/secauthz/sid-strings
  /// </summary>
  internal static readonly Dictionary<string, string> WellKnownSidAliases = new(StringComparer.OrdinalIgnoreCase) {
    ["AN"] = "S-1-5-7", // Anonymous Logon
    ["AU"] = "S-1-5-11", // Authenticated Users
    ["BA"] = "S-1-5-32-544", // Builtin Administrators
    ["BG"] = "S-1-5-32-546", // Builtin Guests
    ["BU"] = "S-1-5-32-545", // Builtin Users
    ["CO"] = "S-1-3-0", // Creator Owner
    ["CG"] = "S-1-3-1", // Creator Group
    ["IU"] = "S-1-5-4", // Interactively Logged-on User
    ["LS"] = "S-1-5-19", // Local Service
    ["NS"] = "S-1-5-20", // Network Service
    ["NU"] = "S-1-5-13", // Network Logon User
    ["PU"] = "S-1-5-32-547", // Power Users
    ["RD"] = "S-1-5-32-555", // Remote Desktop Users
    ["RC"] = "S-1-5-12", // Restricted Code
    ["SO"] = "S-1-5-32-549", // Server Operators
    ["SU"] = "S-1-5-6", // Service Logon User
    ["SY"] = "S-1-5-18", // Local System
    ["WD"] = "S-1-1-0", // Everyone
  };
}
