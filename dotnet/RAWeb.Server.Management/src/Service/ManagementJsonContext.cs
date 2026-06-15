using System.Text.Json.Serialization;
using RAWeb.Sddl;

namespace RAWeb.Server.Management;

[JsonSourceGenerationOptions(
  PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
  PropertyNameCaseInsensitive = true
)]
[JsonSerializable(typeof(SecurityDescriptionDTO))]
[JsonSerializable(typeof(RemoteAppProperties))]
[JsonSerializable(typeof(RemoteAppProperties.FileTypeAssociation))]
[JsonSerializable(typeof(RemoteAppProperties.FileTypeAssociationCollection))]
[JsonSerializable(typeof(SystemRemoteApps.SystemRemoteApp))]
[JsonSerializable(typeof(SystemDesktop))]
[JsonSerializable(typeof(ManagedFileResource))]
[JsonSerializable(typeof(ManagedResource))]
[JsonSerializable(typeof(ManagedResources))]
[JsonSerializable(typeof(InstalledApp))]
[JsonSerializable(typeof(InstalledApp[]))]
[JsonSerializable(typeof(RawSecurityDescriptor), TypeInfoPropertyName = "SddlRawSecurityDescriptor")]
[JsonSerializable(typeof(RawAcl), TypeInfoPropertyName = "SddlRawAcl")]
[JsonSerializable(typeof(ControlFlags), TypeInfoPropertyName = "SddlControlFlags")]
[JsonSerializable(typeof(SecurityIdentifier), TypeInfoPropertyName = "SddlSecurityIdentifier")]
public partial class ManagementJsonContext : JsonSerializerContext { }
