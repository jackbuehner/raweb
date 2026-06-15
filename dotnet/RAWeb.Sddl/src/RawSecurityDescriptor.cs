using System;
using System.Text;

namespace RAWeb.Sddl;

/// <summary>
/// A portable representation of a Windows security descriptor, supporting
/// parsing and serialization of the SDDL string form
/// (<c>O:...G:...D:...S:...</c>).
/// <br /><br />
/// This is a minimal, SDDL-only equivalent of
/// <see cref="System.Security.AccessControl.RawSecurityDescriptor"/> covering
/// owner, group, and discretionary/system ACLs made up of
/// <see cref="CommonAce"/> entries.
/// </summary>
public sealed class RawSecurityDescriptor {
  public ControlFlags ControlFlags { get; }
  public SecurityIdentifier? Owner { get; }
  public SecurityIdentifier? Group { get; }
  public RawAcl? SystemAcl { get; }
  public RawAcl? DiscretionaryAcl { get; }

  public RawSecurityDescriptor(
    ControlFlags flags,
    SecurityIdentifier? owner,
    SecurityIdentifier? group,
    RawAcl? systemAcl,
    RawAcl? discretionaryAcl
  ) {
    ControlFlags = flags;
    Owner = owner;
    Group = group;
    SystemAcl = systemAcl;
    DiscretionaryAcl = discretionaryAcl;
  }

  /// <summary>
  /// Parses a security descriptor from its SDDL string form
  /// (e.g. <c>D:(A;;0x1;;;S-1-5-21-...)(D;;0x1;;;S-1-5-32-545)</c>).
  /// </summary>
  public RawSecurityDescriptor(string sddlForm) {
    if (sddlForm is null) {
      throw new ArgumentNullException(nameof(sddlForm));
    }

    var flags = ControlFlags.None;
    var i = 0;
    while (i < sddlForm.Length) {
      if (i + 1 >= sddlForm.Length || sddlForm[i + 1] != ':') {
        throw new FormatException($"'{sddlForm}' is not a valid security descriptor string.");
      }

      var section = char.ToUpperInvariant(sddlForm[i]);
      i += 2;

      switch (section) {
        case 'O':
        case 'G': {
          var start = i;
          while (i < sddlForm.Length && !IsSectionBoundary(sddlForm, i)) {
            i++;
          }
          var sid = new SecurityIdentifier(sddlForm[start..i]);
          if (section == 'O') {
            Owner = sid;
          }
          else {
            Group = sid;
          }
          break;
        }
        case 'D':
        case 'S': {
          var flagsStart = i;
          while (i < sddlForm.Length && sddlForm[i] != '(' && !IsSectionBoundary(sddlForm, i)) {
            i++;
          }
          var aclFlags = SddlFormat.ParseAclFlags(isSacl: section == 'S', sddlForm[flagsStart..i]);
          flags |= aclFlags;

          var acl = new RawAcl();
          while (i < sddlForm.Length && sddlForm[i] == '(') {
            var close = sddlForm.IndexOf(')', i);
            if (close < 0) {
              throw new FormatException($"'{sddlForm}' is not a valid security descriptor string.");
            }
            acl.InsertAce(acl.Count, CommonAce.Parse(sddlForm[(i + 1)..close]));
            i = close + 1;
          }

          if (section == 'D') {
            DiscretionaryAcl = acl;
            flags |= ControlFlags.DiscretionaryAclPresent;
          }
          else {
            SystemAcl = acl;
            flags |= ControlFlags.SystemAclPresent;
          }
          break;
        }
        default:
          throw new FormatException($"'{sddlForm}' contains an unrecognized SDDL section '{section}:'.");
      }
    }

    ControlFlags = flags;
  }

  /// <summary>
  /// Determines whether the given position begins a new top-level SDDL section
  /// (<c>O:</c>, <c>G:</c>, <c>D:</c>, or <c>S:</c>). SID and alias tokens never
  /// contain a colon, so this check is unambiguous.
  /// </summary>
  private static bool IsSectionBoundary(string sddlForm, int index) {
    var letter = char.ToUpperInvariant(sddlForm[index]);
    return (letter is 'O' or 'G' or 'D' or 'S')
      && index + 1 < sddlForm.Length
      && sddlForm[index + 1] == ':';
  }

  /// <summary>
  /// Serializes this security descriptor to its SDDL string form.
  /// </summary>
  public string GetSddlForm() {
    var sb = new StringBuilder();

    if (Owner is not null) {
      sb.Append("O:").Append(Owner.Value);
    }

    if (Group is not null) {
      sb.Append("G:").Append(Group.Value);
    }

    if (DiscretionaryAcl is not null) {
      sb.Append("D:").Append(SddlFormat.FormatAclFlags(isSacl: false, ControlFlags));
      foreach (var ace in DiscretionaryAcl) {
        sb.Append(ace.ToSddlString());
      }
    }

    if (SystemAcl is not null) {
      sb.Append("S:").Append(SddlFormat.FormatAclFlags(isSacl: true, ControlFlags));
      foreach (var ace in SystemAcl) {
        sb.Append(ace.ToSddlString());
      }
    }

    return sb.ToString();
  }
}
