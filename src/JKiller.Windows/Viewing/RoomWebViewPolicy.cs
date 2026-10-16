namespace JKiller.Windows.Viewing;
public sealed class RoomWebViewPolicy(Uri origin)
{
    public bool AllowsNavigation(Uri target) => target.IsAbsoluteUri && origin.IsAbsoluteUri && target.UserInfo.Length == 0 &&
        target.Scheme is "https" or "http" && (target.Scheme == "https" || target.IsLoopback) &&
        string.Equals(target.GetLeftPart(UriPartial.Authority), origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase);
    // The room page has no native command bridge, even on the authorized origin.
    public bool CanInvokeNative(Uri target) => false;
    public bool AllowsPopup => false;
    public bool AllowsMediaPermission => false;
}
