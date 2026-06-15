using System.Collections.Generic;
using System.Linq;

namespace RAWeb.Sddl;

public static class SecurityDescriptorExtensions {
  /// <summary>
  /// Gets the list of allowed ACEs from a RawSecurityDescriptor.
  /// <br /><br />
  /// If you need to find which SIDs are allowed AND NOT DENIED,
  /// use <see cref="GetAllowedSids(RawSecurityDescriptor, FileAccessRights?)"/> instead.
  /// </summary>
  public static List<CommonAce> GetAccessAllowedAces(this RawSecurityDescriptor securityDescriptor, FileAccessRights? requiredRights = null) {
    if (securityDescriptor.DiscretionaryAcl is null) {
      return [];
    }

    return securityDescriptor.DiscretionaryAcl
      .Where(ace => {
        var isAllowedAce = ace.AceType == AceType.AccessAllowed;
        var hasRequiredRights = !requiredRights.HasValue || (ace.AccessMask & (int)requiredRights) == (int)requiredRights;
        return isAllowedAce && hasRequiredRights;
      })
      .ToList();
  }

  /// <summary>
  /// Gets the list of denied ACEs from a RawSecurityDescriptor.
  /// </summary>
  public static List<CommonAce> GetAccessDeniedAces(this RawSecurityDescriptor securityDescriptor, FileAccessRights? requiredRights = null) {
    if (securityDescriptor.DiscretionaryAcl is null) {
      return [];
    }

    return securityDescriptor.DiscretionaryAcl
      .Where(ace => {
        var isDeniedAce = ace.AceType == AceType.AccessDenied;
        var hasRequiredRights = !requiredRights.HasValue || (ace.AccessMask & (int)requiredRights) == (int)requiredRights;
        return isDeniedAce && hasRequiredRights;
      })
      .ToList();
  }

  /// <summary>
  /// Gets the list of allowed SIDs from a RawSecurityDescriptor,
  /// excluding any that are also explicitly denied.
  /// </summary>
  public static List<SecurityIdentifier> GetAllowedSids(this RawSecurityDescriptor securityDescriptor, FileAccessRights? requiredRights = null) {
    // since we need to exclude denied SIDs, get the denied ACEs first
    var deniedAces = securityDescriptor.GetAccessDeniedAces();

    // get the allowed ACEs that are not also denied
    return securityDescriptor
      .GetAccessAllowedAces(requiredRights)
      .Select(ace => {
        // only include if not also denied
        if (!deniedAces.Any(deniedAce => deniedAce.SecurityIdentifier.Equals(ace.SecurityIdentifier))) {
          return ace.SecurityIdentifier;
        }
        return null;
      })
      // filter out nulls
      .Where(sid => sid != null)
      .ToList()!;
  }

  /// <summary>
  /// Gets the list of explicitly allowed SIDs from a RawSecurityDescriptor.
  /// </summary>
  public static List<SecurityIdentifier> GetExplicitlyAllowedSids(this RawSecurityDescriptor securityDescriptor, FileAccessRights? requiredRights = null) {
    return [.. securityDescriptor.GetAccessAllowedAces(requiredRights).Select(ace => ace.SecurityIdentifier)];
  }

  /// <summary>
  /// Gets the list of explicitly denied SIDs from a RawSecurityDescriptor.
  /// </summary>
  public static List<SecurityIdentifier> GetExplicitlyDeniedSids(this RawSecurityDescriptor securityDescriptor, FileAccessRights? requiredRights = null) {
    return [.. securityDescriptor.GetAccessDeniedAces(requiredRights).Select(ace => ace.SecurityIdentifier)];
  }
}
