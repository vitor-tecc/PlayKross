using System.Runtime.InteropServices;
internal static class FixtureNative
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint GetPropW(nint window, string name);
}
