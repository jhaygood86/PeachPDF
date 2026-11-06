namespace PeachBrackets.Apple.Example;

/// <summary>The page the examples load for their smoke checks: one element for each thing the view reports on.</summary>
internal static class SamplePage
{
    public const string Html = """
        <style>
        body { font: 16px sans-serif; margin: 16px }
        h1 { margin: 0 0 12px }
        .card { display: block; padding: 12px; margin: 8px 0; background: #dfeaf7; border: 1px solid #5b7fa8; cursor: crosshair }
        .card:hover { background: #f7e3c4 }
        a { color: #1a5fb4 }
        .locked { user-select: none; color: #777 }
        .pinned { touch-action: none; padding: 8px; background: #eee; margin: 8px 0 }
        .tall { height: 1600px; background: linear-gradient(#fff, #cfe3ff) }
        @media (hover: none) { .hint::after { content: ' (touch only)' } }
        </style>
        <h1>PeachBrackets.Apple</h1>
        <p id="lead">Select this text, or follow <a id="link" href="https://peachpdf.net/">a link</a>. <span class="hint">Hint</span></p>
        <div class="card" id="card">Hover this card: :hover restyles it.</div>
        <p class="locked" id="locked">This line cannot be selected.</p>
        <div class="pinned" id="pinned">touch-action: none - a drag here does not scroll.</div>
        <form id="form" action="https://example.invalid/submit" style="margin: 8px 0">
        <label><input type="checkbox" id="agree" name="agree"> Agree</label>
        <input type="text" id="name" name="name" value="" style="width: 180px; font: 16px sans-serif">
        <select id="color" name="color"><option value="red">Red</option><option value="green">Green</option></select>
        <br><input type="date" id="when" name="when" value="2026-01-15">
        <input type="color" id="tint" name="tint" value="#3366aa">
        <input type="file" id="doc" name="doc">
        <br><input type="range" id="level" name="level" min="0" max="10" value="2" style="width: 200px">
        <button type="submit" id="send">Send</button>
        </form>
        <div class="tall" id="tall">Scroll down. Pinch to zoom.</div>
        """;
}
