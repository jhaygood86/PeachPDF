// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// A proxy box that references an original source box and maintains independent layout state.
    /// Used for repeating table headers and footers across multiple pages.
    /// </summary>
    /// <remarks>
    /// The source box should not be in the document tree. Multiple proxy boxes can reference
    /// the same source box, each maintaining its own layout snapshot.
    /// </remarks>
    internal sealed class CssProxyBox : CssBox
    {
        private readonly CssBox _sourceBox;
        private LayoutSnapshot? _snapshot;

        /// <summary>
        /// Creates a proxy box that references an original source box.
        /// </summary>
        /// <param name="parent">Parent box for this proxy in the document tree</param>
        /// <param name="sourceBox">The original box to proxy (should not be in document tree)</param>
        public CssProxyBox(CssBox? parent, CssBox sourceBox)
        : base(parent, sourceBox.HtmlTag)
        {
            _sourceBox = sourceBox;

            // Inherit all styles from source
            InheritStyle(sourceBox, everything: true);

            // Explicitly copy critical display properties
            Display = sourceBox.Display;
            Visibility = sourceBox.Visibility;
        }

        /// <summary>
        /// Performs layout by resetting source box, laying it out at this proxy's location,
        /// and capturing the resulting layout state.
        /// </summary>
      protected override async ValueTask PerformLayoutImp(RGraphics g)
        {
#if DEBUG
  System.Console.WriteLine($"CssProxyBox.PerformLayoutImp: START - Location={Location}, Display={Display}");
       System.Console.WriteLine($"  Source already laid out: Location={_sourceBox.Location}, ActualBottom={_sourceBox.ActualBottom}, ActualRight={_sourceBox.ActualRight}");
#endif
            
   // The source box has already been laid out by the table layout engine
            // We just need to:
            // 1. Position it at our location
    // 2. Capture the snapshot
     // 3. Copy dimensions
            
  // Update source box location to match proxy location
          var deltaX = this.Location.X - _sourceBox.Location.X;
            var deltaY = this.Location.Y - _sourceBox.Location.Y;
            
            if (deltaX != 0 || deltaY != 0)
         {
                // Offset the source box and all its children to the proxy's location
       _sourceBox.Location = this.Location;
 foreach (var row in _sourceBox.Boxes)
     {
         row.OffsetLeft(deltaX);
       row.OffsetTop(deltaY);
          }
            }

#if DEBUG
            System.Console.WriteLine($"  After positioning: Source.Location={_sourceBox.Location}");
#endif

            // Capture the layout snapshot
   _snapshot = LayoutSnapshot.Capture(_sourceBox);

#if DEBUG
  System.Console.WriteLine($"  Snapshot captured - BoxStates.Count={_snapshot.BoxStates.Count}");
#endif

            // Copy final dimensions from source to this proxy
            ActualBottom = _sourceBox.ActualBottom;
            ActualRight = _sourceBox.ActualRight;
    Size = _sourceBox.Size;

#if DEBUG
            System.Console.WriteLine($"CssProxyBox.PerformLayoutImp: END - Proxy: ActualBottom={ActualBottom}, ActualRight={ActualRight}, Size={Size}");
#endif

            await ValueTask.CompletedTask;
        }

        /// <summary>
        /// Paints by applying the captured snapshot to the source box and delegating paint.
    /// </summary>
     protected override async ValueTask PaintImp(RGraphics g)
   {
#if DEBUG
System.Console.WriteLine($"CssProxyBox.PaintImp: START - Location={Location}, Snapshot={(_snapshot == null ? "NULL" : "EXISTS")}");
#endif

if (_snapshot == null)
     {
#if DEBUG
System.Console.WriteLine("CssProxyBox.PaintImp: No snapshot, returning");
#endif
    return;
}

 // Step 1: Reset source box paint state before applying snapshot
   _sourceBox.ResetPaint();

    // Step 2: Temporarily reparent source box to this proxy for painting
   // This sets ParentBox AND adds to Boxes collection
 _sourceBox.ParentBox = this;

#if DEBUG
      System.Console.WriteLine($"CssProxyBox.PaintImp: After reparent - this.Boxes.Count={Boxes.Count}");
#endif

// Step 3: Apply our snapshot to source box
  _snapshot.Apply(_sourceBox);

#if DEBUG
      System.Console.WriteLine($"CssProxyBox.PaintImp: After snapshot apply - Source.Location={_sourceBox.Location}");
#endif

     // Step 4: Directly paint the source box (don't call base - we ARE the wrapper)
   await _sourceBox.Paint(g);

#if DEBUG
System.Console.WriteLine("CssProxyBox.PaintImp: After source paint");
#endif

 // Step 5: Remove from Boxes to avoid interference with other proxies
     Boxes.Remove(_sourceBox);

#if DEBUG
     System.Console.WriteLine("CssProxyBox.PaintImp: END");
#endif
   }

   /// <summary>
     /// Stores layout state for a box and all its descendants.
 /// </summary>
    private sealed class LayoutSnapshot
        {
            public Dictionary<CssBox, BoxLayoutState> BoxStates { get; } = new();

            /// <summary>
            /// Captures the current layout state of a box tree.
            /// </summary>
            public static LayoutSnapshot Capture(CssBox root)
            {
                var snapshot = new LayoutSnapshot();
                CaptureBox(root, snapshot);
                return snapshot;
            }

            private static void CaptureBox(CssBox box, LayoutSnapshot snapshot)
            {
                var state = new BoxLayoutState
                {
                    Location = box.Location,
                    ActualBottom = box.ActualBottom,
                    ActualRight = box.ActualRight,
                };

                // Capture rectangles
                foreach (var kvp in box.Rectangles)
                {
                    state.Rectangles[kvp.Key] = kvp.Value;
                }

                // Capture word positions
                foreach (var word in box.Words)
                {
                    state.Words.Add(new BoxLayoutState.WordState
                    {
                        Left = word.Left,
                        Top = word.Top
                    });
                }

                snapshot.BoxStates[box] = state;

                // Recursively capture children
                foreach (var child in box.Boxes)
                {
                    CaptureBox(child, snapshot);
                }
            }

            /// <summary>
            /// Applies the snapshot state to a box tree.
            /// </summary>
            public void Apply(CssBox root)
            {
                ApplyToBox(root);
            }

            private void ApplyToBox(CssBox box)
            {
                if (!BoxStates.TryGetValue(box, out var state))
                    return;

                box.Location = state.Location;
                box.ActualBottom = state.ActualBottom;
                box.ActualRight = state.ActualRight;

                // Restore rectangles
                box.Rectangles.Clear();
                foreach (var kvp in state.Rectangles)
                {
                    box.Rectangles[kvp.Key] = kvp.Value;
                }

                // Restore word positions
                for (int i = 0; i < System.Math.Min(box.Words.Count, state.Words.Count); i++)
                {
                    box.Words[i].Left = state.Words[i].Left;
                    box.Words[i].Top = state.Words[i].Top;
                }

                // Recursively apply to children
                foreach (var child in box.Boxes)
                {
                    ApplyToBox(child);
                }
            }

            public sealed class BoxLayoutState
            {
                public RPoint Location { get; init; }
                public double ActualBottom { get; init; }
                public double ActualRight { get; init; }
                public Dictionary<CssLineBox, RRect> Rectangles { get; init; } = new();
                public List<WordState> Words { get; init; } = new();

                public sealed class WordState
                {
                    public double Left { get; init; }
                    public double Top { get; init; }
                }
            }
        }
    }
}
