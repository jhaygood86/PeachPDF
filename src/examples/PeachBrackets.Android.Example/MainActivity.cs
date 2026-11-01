using Android.App;
using Android.OS;
using Android.Util;
using Android.Views;
using Android.Widget;
using PeachBrackets.Android;

namespace PeachBrackets.Android.Example;

// One HtmlView full screen. After the page loads the activity logs (tag PEACH_SMOKE) what the document says at a few points, so the view can
// be exercised from `adb` without looking at it; the touch handlers log what they see too.
[Activity(Label = "PeachBrackets", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private const string Tag = "PEACH_SMOKE";

    private const string Page = """
        <style>
        body { font: 16px sans-serif; margin: 16px }
        h1 { margin: 0 0 12px }
        .card { display: block; padding: 12px; margin: 8px 0; background: #dfeaf7; border: 1px solid #5b7fa8; cursor: crosshair }
        .card:active { background: #f7e3c4 }
        a { color: #1a5fb4 }
        .locked { user-select: none; color: #777 }
        .pinned { touch-action: none; padding: 8px; background: #eee; margin: 8px 0 }
        .tall { height: 1600px; background: linear-gradient(#fff, #cfe3ff) }
        @media (hover: none) { .hint::after { content: ' (touch only)' } }
        </style>
        <h1>PeachBrackets.Android</h1>
        <p id="lead">Long-press to select, or follow <a id="link" href="https://peachpdf.net/">a link</a>. <span class="hint">Hint</span></p>
        <div class="card" id="card">Press and hold this card: :active restyles it.</div>
        <p class="locked" id="locked">This line cannot be selected.</p>
        <div class="pinned" id="pinned">touch-action: none - a drag here does not scroll.</div>
        <div class="tall" id="tall">Scroll down. Pinch or double-tap to zoom.</div>
        """;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var view = new HtmlView(this) { Html = Page };
        // Android 15 draws edge to edge; the root keeps the page out from under the status and title bars.
        var root = new FrameLayout(this);
        root.SetFitsSystemWindows(true);
        root.AddView(view);
        SetContentView(root);

        view.DocumentFailed += (_, e) => Log.Error(Tag, "FAILED " + e.Exception);
        view.DocumentLoaded += async (_, _) =>
        {
            var document = view.Document!;
            document.Selection.SelectionChanged += (_, e) => Log.Info(Tag, $"selection anchor={e.Anchor} focus={e.Focus}");
            Log.Info(Tag, $"loaded pages={document.Pages.Count} hint={document.TextRuns.Any(r => r.Text.Contains("touch only"))}");
            var link = document.GetElementBounds("link")!.Value;
            var locked = document.GetElementBounds("locked")!.Value;
            var pinned = document.GetElementBounds("pinned")!.Value;
            Log.Info(Tag, $"link={link} locked={locked} pinned={pinned}");
            Log.Info(Tag, $"link-cursor={document.GetCursorAt(0, link.X + 2, link.Y + 2).Shape} locked-selectable={document.GetInteractionAt(0, locked.X + 2, locked.Y + 2).CanSelectText} pinned-touch={document.GetInteractionAt(0, pinned.X + 2, pinned.Y + 2).TouchActions}");
            await Task.CompletedTask;
        };
        view.TouchStarted += (_, e) => Log.Info(Tag, $"touch page={e.PageIndex} x={e.X:0} y={e.Y:0} allowed={e.Allowed}");
        view.Tapped += (_, e) => Log.Info(Tag, $"tapped x={e.X:0} y={e.Y:0}");
        view.LongPressed += (_, e) =>
        {
            Log.Info(Tag, $"longpressed x={e.X:0} y={e.Y:0}");
            view.PostDelayed(() => Log.Info(Tag, $"selected=\"{view.SelectedText}\""), 800);
        };
        view.LinkTapped += (_, e) => Log.Info(Tag, $"link-tapped {e.Uri}");
        view.ScrollChanged += (_, _) => Log.Info(Tag, "scroll");
        view.ZoomChanged += (_, _) => Log.Info(Tag, $"zoom {view.Zoom:0.00}");
    }
}
