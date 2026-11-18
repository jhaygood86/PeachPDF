namespace PeachReader.Core.Tests;

public sealed class BookmarkTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "peachreader-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    [Fact]
    public void Add_KeepsBookmarksInDocumentOrderAndNamesBlankOnesByPage()
    {
        var bookmarks = new BookmarkCollection();
        int changes = 0;
        bookmarks.Changed += (_, _) => changes++;

        bookmarks.Add(new ReaderLocation(5, 10), "  Pricing  ");
        bookmarks.Add(new ReaderLocation(1, 300));
        bookmarks.Add(new ReaderLocation(1, 20), "  ");

        Assert.Equal(["Page 2", "Page 2", "Pricing"], bookmarks.Items.Select(b => b.Label).Order().Select(l => l));
        Assert.Equal([new ReaderLocation(1, 20), new ReaderLocation(1, 300), new ReaderLocation(5, 10)], bookmarks.Items.Select(b => b.Location));
        Assert.Equal("Pricing", bookmarks.Items[2].Label);
        Assert.Equal(3, changes);
    }

    [Fact]
    public void RemoveAndRename_ChangeOnlyWhatExists()
    {
        var bookmarks = new BookmarkCollection();
        var kept = bookmarks.Add(new ReaderLocation(0), "Keep");
        var gone = bookmarks.Add(new ReaderLocation(1), "Gone");

        Assert.True(bookmarks.Rename(kept.Id, " Renamed "));
        Assert.False(bookmarks.Rename(kept.Id, "   "));
        Assert.False(bookmarks.Rename(Guid.NewGuid(), "Nope"));
        Assert.True(bookmarks.Remove(gone.Id));
        Assert.False(bookmarks.Remove(gone.Id));

        var only = Assert.Single(bookmarks.Items);
        Assert.Equal("Renamed", only.Label);
    }

    [Fact]
    public void FindNear_And_Toggle_TreatABookmarkNearAPlaceAsBeingThere()
    {
        var bookmarks = new BookmarkCollection();

        Assert.True(bookmarks.Toggle(new ReaderLocation(2, 100), "Here"));
        Assert.NotNull(bookmarks.FindNear(new ReaderLocation(2, 120)));
        Assert.Null(bookmarks.FindNear(new ReaderLocation(2, 400)));
        Assert.Null(bookmarks.FindNear(new ReaderLocation(3, 100)));
        Assert.False(bookmarks.Toggle(new ReaderLocation(2, 110)));          // already there: removed
        Assert.Empty(bookmarks.Items);
    }

    [Fact]
    public void Json_RoundTripsBookmarks()
    {
        var bookmarks = new BookmarkCollection();
        var added = bookmarks.Add(new ReaderLocation(3, 42.5), "Chart");

        var again = BookmarkCollection.FromJson(bookmarks.ToJson());

        var only = Assert.Single(again.Items);
        Assert.Equal(added, only);
        Assert.Equal(new ReaderLocation(3, 42.5), only.Location);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{\"Version\":2,\"Items\":[{\"Id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"PageIndex\":1,\"TopPoints\":0,\"Label\":\"x\",\"Created\":\"2026-01-01T00:00:00+00:00\"}]}")]
    [InlineData("{\"Version\":1,\"Items\":null}")]
    public void FromJson_GivesNoBookmarksForNothingDamagedOrUnknown(string? json)
    {
        Assert.Empty(BookmarkCollection.FromJson(json).Items);
    }

    [Fact]
    public void FromJson_DropsEntriesThatCannotBeUsed()
    {
        const string json = "{\"Version\":1,\"Items\":[" +
            "{\"Id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3301\",\"PageIndex\":-1,\"TopPoints\":0,\"Label\":\"negative page\",\"Created\":\"2026-01-01T00:00:00+00:00\"}," +
            "{\"Id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3302\",\"PageIndex\":2,\"TopPoints\":0,\"Label\":\"\",\"Created\":\"2026-01-01T00:00:00+00:00\"}," +
            "{\"Id\":\"3f2504e0-4f89-11d3-9a0c-0305e82c3303\",\"PageIndex\":4,\"TopPoints\":9,\"Label\":\"good\",\"Created\":\"2026-01-01T00:00:00+00:00\"}]}";

        var only = Assert.Single(BookmarkCollection.FromJson(json).Items);
        Assert.Equal("good", only.Label);
    }

    [Fact]
    public void Storage_SavesLoadsAndRemovesAFilePerDocument()
    {
        var storage = new BookmarkStorage(Path.Combine(_folder, "nested"));
        string key = BookmarkStorage.KeyOf("a document"u8);
        Assert.Empty(storage.Load(key).Items);

        var bookmarks = new BookmarkCollection();
        bookmarks.Add(new ReaderLocation(1, 5), "One");
        storage.Save(key, bookmarks);

        Assert.Equal("One", Assert.Single(storage.Load(key).Items).Label);
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "nested"), "*.tmp"));
        Assert.Empty(storage.Load(BookmarkStorage.KeyOf("another"u8)).Items);

        storage.Save(key, new BookmarkCollection());
        Assert.Empty(Directory.GetFiles(Path.Combine(_folder, "nested")));
        storage.Save(key, new BookmarkCollection());                          // nothing to remove is fine
    }

    [Fact]
    public void Storage_ReadsADamagedFileAsEmpty_AndRefusesKeysThatAreNotHashes()
    {
        var storage = new BookmarkStorage(_folder);
        string key = BookmarkStorage.KeyOf("x"u8);
        Directory.CreateDirectory(_folder);
        File.WriteAllText(Path.Combine(_folder, key + ".bookmarks.json"), "{ broken");

        Assert.Empty(storage.Load(key).Items);
        Assert.Throws<ArgumentException>(() => storage.Load(@"..\..\evil"));
        Assert.Throws<ArgumentException>(() => storage.Save("", new BookmarkCollection()));
        Assert.Throws<ArgumentNullException>(() => storage.Save(key, null!));
        Assert.Throws<ArgumentException>(() => new BookmarkStorage(" "));
    }

    [Fact]
    public void KeyOf_IsTheLowerCaseSha256OfTheBytes()
    {
        Assert.Equal("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", BookmarkStorage.KeyOf("abc"u8));
    }
}
