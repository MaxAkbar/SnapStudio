using System.Runtime.InteropServices;
using System.Text;

namespace SnapStudio.Platform.Windows;

public interface IWindowsPackageIdentityService
{
    bool HasPackageIdentity();
}

public sealed class WindowsPackageIdentityService : IWindowsPackageIdentityService
{
    private const int ErrorInsufficientBuffer = 122;
    private const int AppModelErrorNoPackage = 15700;

    public bool HasPackageIdentity()
    {
        int length = 0;
        int result = GetCurrentPackageFullName(ref length, null);
        if (result == AppModelErrorNoPackage)
        {
            return false;
        }

        if (result != ErrorInsufficientBuffer || length <= 0)
        {
            return false;
        }

        var packageFullName = new StringBuilder(length);
        result = GetCurrentPackageFullName(ref length, packageFullName);

        return result == 0 && packageFullName.Length > 0;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetCurrentPackageFullName(
        ref int packageFullNameLength,
        StringBuilder? packageFullName);
}
