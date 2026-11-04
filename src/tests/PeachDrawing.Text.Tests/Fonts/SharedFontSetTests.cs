namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// A <see cref="FontSet"/> that several render contexts and documents share: a font added to it is read once, a reader can tell when a family
    /// has appeared, and the set can be used from more than one thread.
    /// </summary>
    public class SharedFontSetTests
    {
        private static byte[] Font(string name = "SourceSans3-Regular.ttf") => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, name));

        [Fact]
        public void AddingAFont_ChangesTheVersionAndNamesTheFamily()
        {
            var set = new FontSet();
            var before = set.Version;

            var family = set.AddData(Font(), new AddOptions { FamilyName = "Shared Sans" });

            Assert.Equal("Shared Sans", family.Name);
            Assert.NotEqual(before, set.Version);
            Assert.Contains("Shared Sans", set.AddedFamilyNames);
        }

        [Fact]
        public void TheSameFontAddedAgain_IsNotReadAgain()
        {
            var set = new FontSet();
            var options = new AddOptions { FamilyName = "Shared Sans", Weight = 400 };

            set.AddData(Font(), options);
            var version = set.Version;
            var again = set.AddData(Font(), new AddOptions { FamilyName = "Shared Sans", Weight = 400 });

            Assert.Equal("Shared Sans", again.Name);
            Assert.Equal(version, set.Version);
            Assert.Single(set.AddedFamilyNames);
        }

        [Fact]
        public void TheSameBytesWithAnotherDescription_AreAnotherFace()
        {
            var set = new FontSet();

            set.AddData(Font(), new AddOptions { FamilyName = "Shared Sans", Weight = 400 });
            var version = set.Version;
            set.AddData(Font(), new AddOptions { FamilyName = "Shared Sans", Weight = 700 });

            Assert.NotEqual(version, set.Version);
            Assert.Single(set.AddedFamilyNames);
        }

        [Fact]
        public void TheInstalledFamilies_AreNotAddedFamilies()
        {
            Assert.Empty(new FontSet().AddedFamilyNames);
        }

        [Fact]
        public void ASetUsedFromSeveralThreads_AddsAndMatchesWithoutFailing()
        {
            var set = new FontSet();
            var data = Font();
            var failures = new System.Collections.Concurrent.ConcurrentBag<Exception>();

            Parallel.For(0, 32, i =>
            {
                try
                {
                    var name = "Shared " + (i % 4);
                    set.AddData(data, new AddOptions { FamilyName = name, Weight = 400 + (i % 3) * 100 });
                    if (set.TryFindFamily(name, out var family))
                        Assert.True(family.TryMatch(new TypefaceQuery { Weight = 400 }, out _));

                    set.HasExplicitRanges(name);
                }
                catch (Exception ex)
                {
                    failures.Add(ex);
                }
            });

            Assert.Empty(failures);
            Assert.Equal(4, set.AddedFamilyNames.Count);
        }
    }
}
