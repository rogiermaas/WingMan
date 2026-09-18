using Microsoft.Win32;

namespace EscortPlane2024;

internal static class UserPreferences
{
    private static readonly string Profile = Environment.GetEnvironmentVariable("WINGMAN_TEST_PROFILE") ?? "";
    internal static string KeyPath => @"Software\WingMan" + (Profile.Length > 0 ? @"\Tests\" + Profile : "");
    internal static string DataRoot => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WingMan", Profile.Length > 0 ? "Tests/" + Profile : "");
    public static string? Get(string name)
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath);
        var result = key?.GetValue(name) as string;
        if (result != null || Profile.Length > 0 || name is not ("Formation" or "Network")) return result;
        using var legacy = Registry.CurrentUser.OpenSubKey(@"Software\EscortPlane2024");
        return legacy?.GetValue(name) as string;
    }
    public static void Put(string name, string value) { using var key = Registry.CurrentUser.CreateSubKey(KeyPath); key.SetValue(name, value, RegistryValueKind.String); }
    public static void Remove(string name) { using var key = Registry.CurrentUser.OpenSubKey(KeyPath, true); key?.DeleteValue(name, false); }
}
