using System.ComponentModel;
using System.Runtime.CompilerServices;
using PeachReader.Core;

namespace PeachReader;

/// <summary>Base for the rows the sidebar lists: raises a change for the property that was set.</summary>
public abstract class RowBase : INotifyPropertyChanged
{
    /// <inheritdoc />
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Sets a field and raises <see cref="PropertyChanged"/> when it changed.</summary>
    /// <param name="field">the field</param>
    /// <param name="value">the new value</param>
    /// <param name="name">the property's name</param>
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    /// <summary>Raises <see cref="PropertyChanged"/> for a property that depends on another.</summary>
    /// <param name="name">the property's name</param>
    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One visible line of the table of contents: an entry, how deep it is, and whether it is open.</summary>
public sealed class OutlineRow : RowBase
{
    private bool _isExpanded, _isCurrent;

    /// <summary>Makes a row for an entry.</summary>
    /// <param name="node">the entry</param>
    /// <param name="level">how deep it is, 0 for a top-level entry</param>
    public OutlineRow(OutlineNode node, int level)
    {
        Node = node;
        Level = level;
        _isExpanded = node.IsOpen && node.Children.Count > 0;
    }

    /// <summary>The entry.</summary>
    public OutlineNode Node { get; }

    /// <summary>How deep the entry is, 0 for top level.</summary>
    public int Level { get; }

    /// <summary>The entry's text.</summary>
    public string Title => Node.Title.Length == 0 ? "(untitled)" : Node.Title;

    /// <summary>Whether the entry has entries nested under it.</summary>
    public bool HasChildren => Node.Children.Count > 0;

    /// <summary>The space left of the row, so nested entries sit further in.</summary>
    public Thickness Indent => new(8 + Level * 16, 0, 8, 0);

    /// <summary>Whether the entry's children are shown.</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            Set(ref _isExpanded, value);
            Raise(nameof(Chevron));
        }
    }

    /// <summary>The arrow in front of an entry that has children.</summary>
    public string Chevron => !HasChildren ? "" : IsExpanded ? "▾" : "▸";

    /// <summary>Whether this is the section the page on screen belongs to.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set
        {
            Set(ref _isCurrent, value);
            Raise(nameof(FontWeightValue));
        }
    }

    /// <summary>Bold for the section being read.</summary>
    public FontAttributes FontWeightValue => IsCurrent ? FontAttributes.Bold : FontAttributes.None;
}

/// <summary>One page in the page list, with its thumbnail once it has been drawn.</summary>
public sealed class PageRow : RowBase
{
    private ImageSource? _thumbnail;
    private bool _isCurrent;
    private string _badge = "";

    /// <summary>Makes a row for a page.</summary>
    /// <param name="index">the zero-based page number</param>
    /// <param name="widthPoints">the page's width in points</param>
    /// <param name="heightPoints">the page's height in points</param>
    public PageRow(int index, double widthPoints, double heightPoints)
    {
        Index = index;
        AspectHeight = 120 * heightPoints / Math.Max(1, widthPoints);
    }

    /// <summary>The zero-based page number.</summary>
    public int Index { get; }

    /// <summary>The page number as the reader counts it.</summary>
    public string Number => (Index + 1).ToString();

    /// <summary>The height of the thumbnail's frame for a 120 wide thumbnail, so the list does not jump as thumbnails arrive.</summary>
    public double AspectHeight { get; }

    /// <summary>The picture of the page, or <see langword="null"/> until it is drawn.</summary>
    public ImageSource? Thumbnail
    {
        get => _thumbnail;
        set => Set(ref _thumbnail, value);
    }

    /// <summary>Whether this is the page on screen.</summary>
    public bool IsCurrent
    {
        get => _isCurrent;
        set => Set(ref _isCurrent, value);
    }

    /// <summary>How many occurrences of a search the page has, as a short text; empty when none.</summary>
    public string Badge
    {
        get => _badge;
        set
        {
            Set(ref _badge, value);
            Raise(nameof(HasBadge));
        }
    }

    /// <summary>Whether <see cref="Badge"/> has anything to show.</summary>
    public bool HasBadge => _badge.Length > 0;
}

/// <summary>One of the reader's bookmarks in the list.</summary>
public sealed class BookmarkRow(UserBookmark bookmark)
{
    /// <summary>The bookmark.</summary>
    public UserBookmark Bookmark { get; } = bookmark;

    /// <summary>The name shown for it.</summary>
    public string Label => Bookmark.Label;

    /// <summary>Where it points, as text.</summary>
    public string Where => "Page " + (Bookmark.PageIndex + 1);
}

/// <summary>Chooses how the sidebar's one list draws a row, by what kind of row it is.</summary>
public sealed class SideTemplateSelector : DataTemplateSelector
{
    /// <summary>The template of a table-of-contents row.</summary>
    public DataTemplate? Outline { get; set; }

    /// <summary>The template of a page row.</summary>
    public DataTemplate? Page { get; set; }

    /// <summary>The template of a bookmark row.</summary>
    public DataTemplate? Bookmark { get; set; }

    /// <inheritdoc />
    protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
    {
        OutlineRow => Outline!,
        PageRow => Page!,
        _ => Bookmark!,
    };
}
