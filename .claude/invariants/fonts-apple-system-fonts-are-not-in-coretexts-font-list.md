# CoreText's font list leaves out the system interface fonts

`CTFontCollectionCreateFromAvailableFonts` does not return San Francisco (`SFNS.ttf`), New York, SF Mono, SF Rounded or
their relatives, although the files are on disk and the system draws with them. Removing the macOS directory scan made
`system-ui` silently fall back to Helvetica Neue. `AppleSystemFonts.Discover` therefore adds one entry per
`AppleUiDesign` (system, serif, monospaced, rounded) asked of `CTFontCreateUIFontForLanguage` and the
`kCTFontUIFontDesign*` traits, and `FontResolver.AppleUiFamilyName` maps a design to the family the font registered under.

Their family names are private and odd (`System Font`, `.New York`, `.SF NS Mono`, `.SF NS Rounded`): never hardcode one.
A family whose name starts with a dot is Apple's own interface font; the system fallback (`PreferPublicFamilies`) prefers
a public family that covers the character, because some of them (`.Apple Color Emoji UI`) are not complete fonts.
