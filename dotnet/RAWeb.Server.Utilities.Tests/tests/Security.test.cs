using RAWeb.Sddl;

namespace RAWeb.Server.Utilities.Tests;

public class SecurityTests {
  [Test]
  public async Task SidRightsToRawSecurityDescriptor_RoundTripsThroughSddl_WithoutNTAccountTranslation() {
    var allowedSid = "S-1-5-21-1000-1001-1002-1003";
    var deniedSid = "S-1-5-32-545";

    var descriptor = SecurityTransformers.SidRightsToRawSecurityDescriptor(
      allowedSids: [new Tuple<string, FileAccessRights?>(allowedSid, FileAccessRights.ReadData)],
      deniedSids: [new Tuple<string, FileAccessRights?>(deniedSid, FileAccessRights.ReadData)]
    );

    await Assert.That(descriptor).IsNotNull();

    var sddl = descriptor!.GetSddlForm();
    var roundTripped = new RawSecurityDescriptor(sddl);

    var allowedSids = roundTripped.GetExplicitlyAllowedSids().Select(sid => sid.Value).ToList();
    var deniedSids = roundTripped.GetExplicitlyDeniedSids().Select(sid => sid.Value).ToList();

    await Assert.That(allowedSids).Contains(allowedSid);
    await Assert.That(deniedSids).Contains(deniedSid);
  }

  [Test]
  public async Task SidRightsToRawSecurityDescriptor_WithNoAllowedOrDeniedSids_ReturnsNull() {
    var descriptor = SecurityTransformers.SidRightsToRawSecurityDescriptor();

    await Assert.That(descriptor).IsNull();
  }

  [Test]
  public async Task GetAllowedSids_ExcludesSidsThatAreAlsoExplicitlyDenied() {
    var sid = new SecurityIdentifier("S-1-5-21-1000-1001-1002-1003").Value;
    var descriptor = new RawSecurityDescriptor($"D:(D;;0x1;;;{sid})(A;;0x1;;;{sid})");

    var allowedSids = descriptor.GetAllowedSids().Select(s => s.Value).ToList();

    await Assert.That(allowedSids).DoesNotContain(sid);
  }
}
