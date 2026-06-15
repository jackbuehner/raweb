using System;
using System.Collections.Generic;
using System.Linq;

namespace RAWeb.Sddl;

public static class SecurityTransformers {
  /// <summary>
  /// Builds a <see cref="RawSecurityDescriptor"/> from collections of allowed and denied SIDs.
  /// </summary>
  /// <param name="allowedSids">
  /// A collection of tuples (Sid, Rights) representing SIDs granted access.
  /// Optional. May be <see langword="null"/>.
  /// If <c>Rights</c> is <see langword="null"/>, defaults to <see cref="FileAccessRights.ReadData"/>.
  /// </param>
  /// <param name="deniedSids">
  /// A collection of tuples (Sid, Rights) representing SIDs explicitly denied access.
  /// Optional. May be <see langword="null"/>.
  /// </param>
  /// <returns>
  /// A <see cref="RawSecurityDescriptor"/> containing a DACL with the specified allowed and denied entries.
  /// </returns>
  /// <remarks>
  /// <para>
  /// Deny access entries are inserted before allow entries.
  /// </para>
  /// <para>
  /// The created descriptor contains only a DACL; the owner, group, and SACL fields are <see langword="null"/>.
  /// </para>
  /// </remarks>
  public static RawSecurityDescriptor? SidRightsToRawSecurityDescriptor(
    IEnumerable<Tuple<string, FileAccessRights?>>? allowedSids = null,
    IEnumerable<Tuple<string, FileAccessRights?>>? deniedSids = null
  ) {
    var dacl = new RawAcl();
    var aceIndex = 0;

    if ((deniedSids is null || !deniedSids.Any()) && (allowedSids is null || !allowedSids.Any())) {
      return null;
    }

    // add deny access control entries (ACEs) first
    if (deniedSids is not null) {
      foreach (var (sidStr, rights) in deniedSids) {
        var sid = new SecurityIdentifier(sidStr);
        var rightsValue = rights ?? FileAccessRights.ReadData;

        var ace = new CommonAce(
            AceType.AccessDenied,
            AceFlags.None,
            (int)rightsValue,
            sid
        );

        dacl.InsertAce(aceIndex++, ace);
      }
    }

    // add allow access control entries (ACEs)
    if (allowedSids is not null) {
      foreach (var (sidStr, rights) in allowedSids) {
        var sid = new SecurityIdentifier(sidStr);
        var rightsValue = rights ?? FileAccessRights.ReadData;

        var ace = new CommonAce(
            AceType.AccessAllowed,
            AceFlags.None,
            (int)rightsValue,
            sid
        );

        dacl.InsertAce(aceIndex++, ace);
      }
    }

    // combine ACEs into a RawSecurityDescriptor
    var descriptor = new RawSecurityDescriptor(
        ControlFlags.DiscretionaryAclPresent,
        owner: null,
        group: null,
        systemAcl: null,
        discretionaryAcl: dacl
    );

    return descriptor;
  }
}
