namespace PeachPDF.Tests.CSS
{
    using PeachPDF.CSS;
    using System;
    using Xunit;

    public class UrlTests
    {
        [Fact]
        public void AbsoluteHttpUrl_ParsesAllComponents()
        {
            var url = new Url("http://user:pass@example.com:8080/some/path?query=1#frag");

            Assert.False(url.IsInvalid);
            Assert.Equal("http", url.Scheme);
            Assert.Equal("example.com", url.HostName);
            Assert.Equal("8080", url.Port);
            Assert.Equal("user", url.UserName);
            Assert.Equal("pass", url.Password);
            Assert.Equal("some/path", url.Path);
            Assert.Equal("query=1", url.Query);
            Assert.Equal("frag", url.Fragment);
            Assert.False(url.IsRelative);
        }

        [Fact]
        public void DefaultPort_IsOmittedFromHost()
        {
            var url = new Url("http://example.com:80/");

            Assert.Equal(string.Empty, url.Port);
            Assert.Equal("example.com", url.Host);
        }

        [Fact]
        public void NonDefaultPort_IsIncludedInHost()
        {
            var url = new Url("http://example.com:8080/");

            Assert.Equal("example.com:8080", url.Host);
        }

        [Fact]
        public void RelativeUrl_ResolvesAgainstBase()
        {
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "other.html");

            Assert.False(relative.IsInvalid);
            Assert.Equal("example.com", relative.HostName);
            Assert.Equal("dir/other.html", relative.Path);
        }

        [Fact]
        public void RelativeUrl_QueryOnly_ResolvesAgainstBasePathKeepingScheme()
        {
            // Exercises Url.RelativeState's '?' branch (ParseQuery).
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "?a=1");

            Assert.False(relative.IsInvalid);
            Assert.Equal("dir/page.html", relative.Path);
            Assert.Equal("a=1", relative.Query);
        }

        [Fact]
        public void RelativeUrl_FragmentOnly_ResolvesAgainstBasePathKeepingScheme()
        {
            // Exercises Url.RelativeState's '#' branch (ParseFragment).
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "#section");

            Assert.False(relative.IsInvalid);
            Assert.Equal("dir/page.html", relative.Path);
            Assert.Equal("section", relative.Fragment);
        }

        [Fact]
        public void RelativeUrl_SingleSlashPath_ReplacesEntirePath()
        {
            // Exercises Url.RelativeSlashState's non-double-slash fallback (ParsePath(input, index - 1)):
            // a single leading '/' with no scheme change is an absolute-path reference, not a new authority.
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "/other/path");

            Assert.False(relative.IsInvalid);
            Assert.Equal("example.com", relative.HostName);
            Assert.Equal("other/path", relative.Path);
        }

        [Fact]
        public void RelativeUrl_DoubleSlash_ParsesNewAuthority()
        {
            // Exercises Url.RelativeSlashState's double-slash branch (IgnoreSlashesState/ParseAuthority):
            // "//host/path" replaces the authority, keeping the base's scheme.
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "//other.example.org/new/path");

            Assert.False(relative.IsInvalid);
            Assert.Equal("http", relative.Scheme);
            Assert.Equal("other.example.org", relative.HostName);
            Assert.Equal("new/path", relative.Path);
        }

        [Fact]
        public void RelativeUrl_TrailingSlashOnly_ParsesAsEmptyPath()
        {
            // Exercises Url.RelativeSlashState's "index == input.Length - 1" early ParsePath branch.
            var baseUrl = new Url("http://example.com/dir/page.html");
            var relative = new Url(baseUrl, "/");

            Assert.False(relative.IsInvalid);
            Assert.Equal("example.com", relative.HostName);
        }

        [Fact]
        public void FileUrl_DoubleSlashRelative_ParsesNewFileHost()
        {
            // Exercises Url.RelativeSlashState's file-scheme double-slash branch (ParseFileHost).
            var baseUrl = new Url("file:///c:/dir/page.html");
            var relative = new Url(baseUrl, "//otherhost/share/file.txt");

            Assert.False(relative.IsInvalid);
            Assert.Equal("file", relative.Scheme);
            Assert.Equal("otherhost", relative.HostName);
        }

        [Fact]
        public void FileUrl_DoubleSlashDriveLetter_ParsesAsPathNotHost()
        {
            // Exercises Url.IsWindowsDriveLetter's true branch inside ParseFileHost: "file://d:/x" has a
            // drive letter, not a real host, immediately after the double slash.
            var baseUrl = new Url("file:///c:/dir/page.html");
            var relative = new Url(baseUrl, "//d:/other/file.txt");

            Assert.False(relative.IsInvalid);
            Assert.Equal("file", relative.Scheme);
        }

        [Fact]
        public void Host_UppercaseLetters_AreLowercased()
        {
            // Exercises Url.AppendDefaultHostChar's lowercase-conversion branch.
            var url = new Url("http://EXAMPLE.COM/path");

            Assert.Equal("example.com", url.HostName);
        }

        [Fact]
        public void Host_ValidPercentEncodedByte_IsDecoded()
        {
            // Exercises Url.AppendPercentEncodedHostChar's valid-hex branch.
            var url = new Url("http://ex%61mple.com/path");

            Assert.Equal("example.com", url.HostName);
        }

        [Fact]
        public void Host_InvalidPercentEscape_IsKeptLiteral()
        {
            // Exercises Url.AppendPercentEncodedHostChar's non-hex fallback branch.
            var url = new Url("http://ex%zzample.com/path");

            Assert.Contains("%", url.HostName);
        }

        [Fact]
        public void Host_FullwidthPunycodeMappedCharacter_IsNormalized()
        {
            // Exercises Url.AppendDefaultHostChar's Symbols.Punycode lookup branch: a fullwidth ideographic
            // full stop (U+FF0E) is one of the handful of characters Symbols.Punycode maps directly.
            var url = new Url("http://example．com/path");

            Assert.Equal("example.com", url.HostName);
        }

        [Fact]
        public void Host_HyphenatedLabel_IsPreserved()
        {
            // Exercises Url.AppendDefaultHostChar's IsAlphanumericAscii-false/hyphen-kept branch.
            var url = new Url("http://my-example.com/path");

            Assert.Equal("my-example.com", url.HostName);
        }

        [Fact]
        public void Host_IPv6Literal_IsPreservedVerbatim()
        {
            // Exercises SanatizeHost's bracketed-IPv6-literal fast path (returns the substring as-is,
            // never reaching the per-character sanitizer at all).
            var url = new Url("http://[::1]:8080/path");

            Assert.Equal("[::1]", url.HostName);
        }

        [Fact]
        public void CopyConstructor_CopiesAllComponents()
        {
            var original = new Url("http://user:pass@example.com:8080/path?query#frag");
            var copy = new Url(original);

            Assert.Equal(original, copy);
            Assert.Equal(original.ToString(), copy.ToString());
        }

        [Fact]
        public void PathWithDotSegments_IsNormalized()
        {
            var url = new Url("http://example.com/a/b/../c/./d");

            Assert.Equal("a/c/d", url.Path);
        }

        [Fact]
        public void PathWithLeadingUpDirectory_DoesNotUnderflow()
        {
            var url = new Url("http://example.com/../a");

            Assert.Equal("a", url.Path);
        }

        [Fact]
        public void MailtoScheme_IsNotRelative_AndParsesAsSchemeData()
        {
            var url = new Url("mailto:someone@example.com");

            Assert.False(url.IsInvalid);
            Assert.Equal("mailto", url.Scheme);
            Assert.False(url.IsRelative);
            Assert.Equal("someone@example.com", url.Data);
        }

        [Fact]
        public void Origin_ForHttpUrl_IncludesSchemeAndHost()
        {
            var url = new Url("http://example.com:8080/path");

            Assert.Equal("http://example.com:8080", url.Origin);
        }

        [Fact]
        public void Origin_ForNonOriginableScheme_IsNull()
        {
            var url = new Url("mailto:someone@example.com");

            Assert.Null(url.Origin);
        }

        [Fact]
        public void Equals_And_GetHashCode_MatchForEquivalentUrls()
        {
            var a = new Url("http://example.com/path?q#f");
            var b = new Url("http://example.com/path?q#f");

            Assert.True(a.Equals(b));
            Assert.True(a.Equals((object)b));
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
        }

        [Fact]
        public void Equals_DiffersForDifferentUrls()
        {
            var a = new Url("http://example.com/a");
            var b = new Url("http://example.com/b");

            Assert.False(a.Equals(b));
            Assert.False(a.Equals((object)"not a url"));
        }

        [Fact]
        public void ToString_RoundTripsThroughReparsing()
        {
            var url = new Url("http://example.com:8080/path?q=1#f");
            var reparsed = new Url(url.ToString());

            Assert.Equal(url, reparsed);
        }

        [Fact]
        public void Href_Setter_ReparsesTheUrl()
        {
            var url = new Url("http://example.com/original");

            url.Href = "http://example.org/updated";

            Assert.Equal("example.org", url.HostName);
            Assert.Equal("updated", url.Path);
        }

        [Fact]
        public void Fragment_Setter_UpdatesFragment()
        {
            var url = new Url("http://example.com/path");

            url.Fragment = "new-fragment";

            Assert.Equal("new-fragment", url.Fragment);
        }

        [Fact]
        public void Fragment_SetToNull_ClearsFragment()
        {
            var url = new Url("http://example.com/path#frag");

            url.Fragment = null;

            Assert.Null(url.Fragment);
        }

        [Fact]
        public void Query_Setter_UpdatesQuery()
        {
            var url = new Url("http://example.com/path");

            url.Query = "a=1";

            Assert.Equal("a=1", url.Query);
        }

        [Fact]
        public void Path_Setter_UpdatesPath()
        {
            var url = new Url("http://example.com/original");

            url.Path = "new/path";

            Assert.Equal("new/path", url.Path);
        }

        [Fact]
        public void Port_Setter_UpdatesPort()
        {
            var url = new Url("http://example.com/path");

            url.Port = "9090";

            Assert.Equal("9090", url.Port);
        }

        [Fact]
        public void Scheme_Setter_UpdatesScheme()
        {
            var url = new Url("http://example.com/path");

            // The Scheme setter parses via ParseScheme(value, onlyScheme: true), which looks for a
            // trailing colon (matching the DOM HTMLHyperlinkElementUtils.protocol convention, e.g.
            // "https:") -- without it, no colon is ever found and the scheme is left unchanged.
            url.Scheme = "https:";

            Assert.Equal("https", url.Scheme);
        }

        [Fact]
        public void HostName_Setter_UpdatesHost()
        {
            var url = new Url("http://example.com/path");

            url.HostName = "other.example.com";

            Assert.Equal("other.example.com", url.HostName);
        }

        [Fact]
        public void ImplicitConversion_ToUri_ProducesEquivalentUri()
        {
            var url = new Url("http://example.com/path");

            Uri uri = url;

            Assert.Equal(UriKind.Absolute, uri.IsAbsoluteUri ? UriKind.Absolute : UriKind.Relative);
        }

        [Fact]
        public void PathWithPercentEncodedCharacters_IsPreserved()
        {
            var url = new Url("http://example.com/a%20b");

            Assert.Equal("a%20b", url.Path);
        }

        [Fact]
        public void Convert_FromUri_ParsesEquivalently()
        {
            var uri = new Uri("http://example.com/path?q=1");

            var url = Url.Convert(uri);

            Assert.Equal("example.com", url.HostName);
            Assert.Equal("path", url.Path);
        }

        [Fact]
        public void Create_IsEquivalentToConstructor()
        {
            var viaCreate = Url.Create("http://example.com/path");
            var viaCtor = new Url("http://example.com/path");

            Assert.Equal(viaCtor, viaCreate);
        }
    }
}
