using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PeachReader.Core;

/// <summary>A place the reader saved in a document.</summary>
/// <param name="Id">identifies the bookmark</param>
/// <param name="PageIndex">the zero-based page number</param>
/// <param name="TopPoints">how far below the top of the page, in points</param>
/// <param name="Label">the name shown for it</param>
/// <param name="Created">when it was added</param>
public sealed record UserBookmark(Guid Id, int PageIndex, double TopPoints, string Label, DateTimeOffset Created)
{
    /// <summary>The place the bookmark leads to.</summary>
    [JsonIgnore]
    public ReaderLocation Location => new(PageIndex, TopPoints);
}

/// <summary>The bookmarks of one document, kept in the order they appear in it.</summary>
public sealed class BookmarkCollection
{
    private readonly List<UserBookmark> _items = [];

    /// <summary>Raised after a bookmark is added, removed or renamed.</summary>
    public event EventHandler? Changed;

    /// <summary>The bookmarks, by page and then by position on the page.</summary>
    public IReadOnlyList<UserBookmark> Items => _items;

    /// <summary>Adds a bookmark.</summary>
    /// <param name="location">the place to save</param>
    /// <param name="label">the name to show; a blank one becomes "Page N"</param>
    /// <returns>the new bookmark</returns>
    public UserBookmark Add(ReaderLocation location, string? label = null)
    {
        var bookmark = new UserBookmark(Guid.NewGuid(), location.PageIndex, location.TopPoints,
            string.IsNullOrWhiteSpace(label) ? "Page " + (location.PageIndex + 1) : label.Trim(), DateTimeOffset.UtcNow);
        _items.Add(bookmark);
        Sort();
        Changed?.Invoke(this, EventArgs.Empty);
        return bookmark;
    }

    /// <summary>Removes a bookmark.</summary>
    /// <param name="id">the bookmark's <see cref="UserBookmark.Id"/></param>
    /// <returns><see langword="true"/> when there was one to remove</returns>
    public bool Remove(Guid id)
    {
        int removed = _items.RemoveAll(b => b.Id == id);
        if (removed > 0) Changed?.Invoke(this, EventArgs.Empty);
        return removed > 0;
    }

    /// <summary>Changes the name of a bookmark.</summary>
    /// <param name="id">the bookmark's <see cref="UserBookmark.Id"/></param>
    /// <param name="label">the new name; a blank one is ignored</param>
    /// <returns><see langword="true"/> when the bookmark exists and was renamed</returns>
    public bool Rename(Guid id, string label)
    {
        int at = _items.FindIndex(b => b.Id == id);
        if (at < 0 || string.IsNullOrWhiteSpace(label)) return false;
        _items[at] = _items[at] with { Label = label.Trim() };
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>The bookmark on a page near a position, if any.</summary>
    /// <param name="location">the place to look</param>
    /// <param name="tolerancePoints">how far from the position still counts, in points</param>
    /// <returns>the nearest bookmark within the tolerance, or <see langword="null"/></returns>
    public UserBookmark? FindNear(ReaderLocation location, double tolerancePoints = 36) =>
        _items.Where(b => b.PageIndex == location.PageIndex && Math.Abs(b.TopPoints - location.TopPoints) <= tolerancePoints)
              .OrderBy(b => Math.Abs(b.TopPoints - location.TopPoints)).FirstOrDefault();

    /// <summary>Adds a bookmark at a place, or removes the one already there: the star of a reader's toolbar.</summary>
    /// <param name="location">the place</param>
    /// <param name="label">the name for a new bookmark</param>
    /// <returns><see langword="true"/> when a bookmark now exists there, <see langword="false"/> when one was removed</returns>
    public bool Toggle(ReaderLocation location, string? label = null)
    {
        if (FindNear(location) is { } existing)
        {
            Remove(existing.Id);
            return false;
        }

        Add(location, label);
        return true;
    }

    /// <summary>The bookmarks as JSON, for saving.</summary>
    /// <returns>the document</returns>
    public string ToJson() => JsonSerializer.Serialize(new BookmarkFile(1, [.. _items]), BookmarkJson.Default.BookmarkFile);

    /// <summary>Reads bookmarks saved by <see cref="ToJson"/>. A damaged or unknown file gives no bookmarks rather than an error.</summary>
    /// <param name="json">the saved text, or <see langword="null"/></param>
    /// <returns>the collection</returns>
    public static BookmarkCollection FromJson(string? json)
    {
        var collection = new BookmarkCollection();
        if (string.IsNullOrWhiteSpace(json)) return collection;
        try
        {
            var file = JsonSerializer.Deserialize(json, BookmarkJson.Default.BookmarkFile);
            if (file is { Version: 1, Items: { } items })
                collection._items.AddRange(items.Where(b => b is { PageIndex: >= 0 } && !string.IsNullOrWhiteSpace(b.Label)));
            collection.Sort();
        }
        catch (JsonException)
        {
            collection._items.Clear();
        }

        return collection;
    }

    private void Sort() => _items.Sort((a, b) => a.PageIndex != b.PageIndex ? a.PageIndex.CompareTo(b.PageIndex) : a.TopPoints.CompareTo(b.TopPoints));
}

internal sealed record BookmarkFile(int Version, List<UserBookmark>? Items);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(BookmarkFile))]
internal sealed partial class BookmarkJson : JsonSerializerContext;

/// <summary>Saves each document's bookmarks as a JSON file in a folder, named by a hash of the document.</summary>
public sealed class BookmarkStorage
{
    private readonly string _directory;

    /// <summary>Stores bookmarks in a folder, creating it when first saved.</summary>
    /// <param name="directory">the folder, typically in the app's data folder</param>
    public BookmarkStorage(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = directory;
    }

    /// <summary>The key that identifies a document whatever it is called or moved to: the SHA-256 of its bytes, as lower-case hex.</summary>
    /// <param name="document">the bytes of the file</param>
    /// <returns>the key</returns>
    public static string KeyOf(ReadOnlySpan<byte> document) => Convert.ToHexStringLower(SHA256.HashData(document));

    /// <summary>Reads a document's bookmarks.</summary>
    /// <param name="key">the document's key from <see cref="KeyOf"/></param>
    /// <returns>its bookmarks, none when nothing was saved or the file is damaged</returns>
    public BookmarkCollection Load(string key)
    {
        string path = PathOf(key);
        try { return BookmarkCollection.FromJson(File.Exists(path) ? File.ReadAllText(path) : null); }
        catch (IOException) { return new BookmarkCollection(); }
        catch (UnauthorizedAccessException) { return new BookmarkCollection(); }
    }

    /// <summary>Saves a document's bookmarks, replacing the file in one step so an interrupted save cannot leave half of one; with none left the file is removed.</summary>
    /// <param name="key">the document's key from <see cref="KeyOf"/></param>
    /// <param name="bookmarks">the bookmarks to keep</param>
    public void Save(string key, BookmarkCollection bookmarks)
    {
        ArgumentNullException.ThrowIfNull(bookmarks);
        string path = PathOf(key);
        if (bookmarks.Items.Count == 0)
        {
            if (File.Exists(path)) File.Delete(path);
            return;
        }

        Directory.CreateDirectory(_directory);
        string temp = path + ".tmp";
        File.WriteAllText(temp, bookmarks.ToJson());
        File.Move(temp, path, overwrite: true);
    }

    private string PathOf(string key)
    {
        // A key is a hash, so a name with anything else in it (a path) is refused rather than written outside the folder.
        if (string.IsNullOrEmpty(key) || !key.All(c => c is (>= '0' and <= '9') or (>= 'a' and <= 'f')))
            throw new ArgumentException("A document key is lower-case hex.", nameof(key));
        return Path.Combine(_directory, key + ".bookmarks.json");
    }
}
