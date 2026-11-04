using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace PeachBrackets.WinUI.UITests;

/// <summary>Views as the rows of a virtualized list - an <see cref="ItemsRepeater"/> in a <see cref="ScrollViewer"/> - recycled as it scrolls.</summary>
public class HtmlViewListTests
{
    private const int Count = 100;

    // Mixed heights, 30 to 250 device-independent pixels, each a different colour so a row painted with another's content shows.
    private static double HeightOf(int index) => 30 + (index * 37 % 221);

    private static string MessageHtml(int index) =>
        $"<style>body{{margin:0;font:14px sans-serif}}</style><div id=m style='height:{HeightOf(index)}px;background:rgb({(index * 53) % 256},{(index * 29 + 80) % 256},{(index * 71 + 40) % 256})'>Message {index} <a href='https://example.com/{index}'>link</a></div>";

    /// <summary>Makes the views of the list and keeps the ones the list has finished with to be used again.</summary>
    private sealed class Factory : IElementFactory
    {
        private readonly Stack<HtmlView> _pool = new();

        public int Created { get; private set; }

        public int Reused { get; private set; }

        public UIElement GetElement(ElementFactoryGetArgs args)
        {
            var index = (int)args.Data;
            HtmlView view;
            if (_pool.Count > 0)
            {
                view = _pool.Pop();
                Reused++;
            }
            else
            {
                view = new HtmlView { ScrollMode = HtmlViewScrollMode.Disabled, IsLayoutSynchronous = true, PageBackground = Microsoft.UI.Colors.Transparent };
                Created++;
            }

            view.Html = MessageHtml(index);
            return view;
        }

        public void RecycleElement(ElementFactoryRecycleArgs args)
        {
            if (args.Element is HtmlView view)
                _pool.Push(view);
        }
    }

    private static async Task SettleAsync(FrameworkElement root)
    {
        for (var i = 0; i < 3; i++)
        {
            await Task.Delay(60);
            root.UpdateLayout();
        }
    }

    private static IEnumerable<(int Index, HtmlView View)> Realized(ItemsRepeater repeater) =>
        Enumerable.Range(0, Count).Select(i => (Index: i, View: repeater.TryGetElement(i) as HtmlView)).Where(p => p.View is not null).Select(p => (p.Index, p.View!));

    private static int CountScrollers(DependencyObject root)
    {
        var count = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            count += (child is ScrollViewer ? 1 : 0) + CountScrollers(child);
        }

        return count;
    }

    [Fact]
    public async Task AHundredRowsOfMixedHeightsScrollAsAListWithExactHeightsAndNothingLeftUnpainted()
    {
        var (expectedExtent, extent, checkedRows, created, reused, problems) = await UiThread.RunAsync(async window =>
        {
            var factory = new Factory();
            var repeater = new ItemsRepeater
            {
                ItemsSource = Enumerable.Range(0, Count).ToList(),
                ItemTemplate = factory,
                Layout = new StackLayout { Spacing = 0 },
            };
            var scroller = new ScrollViewer { Width = 400, Height = 600, Content = repeater };
            window.Content = scroller;
            await SettleAsync(scroller);

            var problems = new List<string>();
            var rowsChecked = 0;
            var shown = new List<(int Index, HtmlView View)>();

            void CheckVisibleRows()
            {
                foreach (var (index, view) in Realized(repeater))
                {
                    rowsChecked++;
                    if (Math.Abs(view.ActualHeight - HeightOf(index)) > 0.5)
                        problems.Add($"row {index}: ActualHeight {view.ActualHeight} should be {HeightOf(index)}");
                    if (Math.Abs(view.ContentHeight - HeightOf(index)) > 0.5)
                        problems.Add($"row {index}: ContentHeight {view.ContentHeight} should be {HeightOf(index)}");
                    if (view.Document is null)
                        problems.Add($"row {index}: no document");
                    // Only what shows is painted: the list also realizes rows just outside the viewport.
                    var top = view.TransformToVisual(scroller).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
                    if (top < scroller.ViewportHeight && top + view.ActualHeight > 0 && view.PaintedRegionCount == 0)
                        shown.Add((index, view));
                    if (view.Document is not null)
                    {
                        view.SelectAll();
                        if (!view.SelectedText.Contains($"Message {index} "))
                            problems.Add($"row {index}: shows '{view.SelectedText}'");
                    }
                }
            }

            // Painting follows layout by a frame, which a loaded runner can take a while to deliver: a shown row gets a moment before it counts as unpainted.
            async Task CheckPaintedAsync()
            {
                foreach (var (index, view) in shown)
                {
                    for (var wait = 0; wait < 60 && view.PaintedRegionCount == 0; wait++)
                        await Task.Delay(50);

                    if (view.PaintedRegionCount == 0)
                        problems.Add($"row {index}: shown but never painted");
                }

                shown.Clear();
            }

            // Down in big steps, then up again, as a fast flick does.
            for (var offset = 0.0; offset <= scroller.ScrollableHeight; offset += 700)
            {
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                await SettleAsync(scroller);
                CheckVisibleRows();
                await CheckPaintedAsync();
            }

            for (var offset = scroller.ScrollableHeight; offset >= 0; offset -= 900)
            {
                scroller.ChangeView(null, offset, null, disableAnimation: true);
                await SettleAsync(scroller);
                CheckVisibleRows();
                await CheckPaintedAsync();
            }

            return (Enumerable.Range(0, Count).Sum(HeightOf), scroller.ExtentHeight, rowsChecked, factory.Created, factory.Reused, problems);
        });

        Assert.Empty(problems);
        // The list estimates the extent of rows it has not realized from the ones it has, so only the rows themselves are exact.
        Assert.InRange(extent, expectedExtent * 0.97, expectedExtent * 1.03);
        Assert.True(checkedRows > 50);
        Assert.True(reused > 0, $"views were created {created} times and never reused");
        Assert.True(created < Count / 2, $"{created} views were created for {Count} rows");
    }

    [Fact]
    public async Task ARowHasNoScrollerOfItsOwn_SoTheWheelOverItScrollsTheList()
    {
        var scrollers = await UiThread.RunAsync(async window =>
        {
            var repeater = new ItemsRepeater { ItemsSource = Enumerable.Range(0, 5).ToList(), ItemTemplate = new Factory(), Layout = new StackLayout() };
            var scroller = new ScrollViewer { Width = 400, Height = 300, Content = repeater };
            window.Content = scroller;
            await SettleAsync(scroller);

            return Realized(repeater).Sum(r => CountScrollers(r.View));
        });

        Assert.Equal(0, scrollers);
    }

    [Fact]
    public async Task SelectionAndLinksWorkInsideARowOfTheList()
    {
        var (selected, uri) = await UiThread.RunAsync(async window =>
        {
            var repeater = new ItemsRepeater { ItemsSource = Enumerable.Range(0, 5).ToList(), ItemTemplate = new Factory(), Layout = new StackLayout() };
            var scroller = new ScrollViewer { Width = 400, Height = 300, Content = repeater };
            window.Content = scroller;
            await SettleAsync(scroller);

            var view = (HtmlView)repeater.TryGetElement(2)!;
            view.SelectAll();
            return (view.SelectedText, view.Document!.Annotations.Links.First().Uri);
        });

        Assert.Contains("Message 2", selected);
        Assert.Equal("https://example.com/2", uri);
    }
}
