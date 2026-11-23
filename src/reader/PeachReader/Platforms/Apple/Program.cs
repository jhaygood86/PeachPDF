using UIKit;

namespace PeachReader;

/// <summary>The entry point on iOS and Mac Catalyst.</summary>
public static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}
