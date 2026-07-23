using OneDesk.Desktop.Services;
using OneDesk.Desktop.Storage;
using Xunit;

namespace OneDesk.Desktop.Tests;

public sealed class PairingServiceStableIdentityTests
{
    [Fact]
    public void TrustedUpgradeBindsStableKeyAndPreservesIdentityAfterReload()
    {
        var root = Path.Combine(Path.GetTempPath(), $"onedesk-pairing-{Guid.NewGuid():N}");
        try
        {
            var paths = new OneDeskDataPaths(root);
            var pairing = new PairingService(paths);
            var original = pairing.CreateTrustCredential("mobile-existing", "Android Test");
            pairing.RenameTrustedDevice(original.DeviceId, "客厅控制器");

            Assert.True(pairing.BindStableDeviceKey(
                original.DeviceId,
                "android:stable-upgrade-test",
                "android",
                "arm64"));

            var reloaded = new PairingService(paths);
            var matched = reloaded.FindPairingIdentity("android:stable-upgrade-test", "Android Test");
            Assert.NotNull(matched);
            Assert.Equal(original.DeviceId, matched.DeviceId);
            Assert.Equal("客厅控制器", matched.Remark);

            var rotated = reloaded.CreateTrustCredential(
                matched.DeviceId,
                matched.DisplayName,
                matched.StableDeviceKey,
                matched.Platform,
                matched.Architecture);
            Assert.Equal(original.DeviceId, rotated.DeviceId);
            Assert.Equal("客厅控制器", rotated.Remark);
            Assert.NotEqual(original.Token, rotated.Token);
            Assert.Single(reloaded.TrustedDevices());
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
