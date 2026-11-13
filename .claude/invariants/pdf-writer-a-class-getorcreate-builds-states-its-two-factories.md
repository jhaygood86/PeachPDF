# PeachDrawing.PDF: a class `GetOrCreate` builds must state its two factories

A dictionary or array class that is requested through `Elements.GetOrCreate<T>` / `GetOrCreateArray<T>` / `GetExisting<T>` must implement `IPdfDictionaryValue<T>` / `IPdfArrayValue<T>` (explicit static `Create(PdfDocument)` and `Wrap(PdfDictionary or PdfArray)`), and `Wrap` needs a
constructor that takes the general object (`: base(dict)`). There is no reflection fallback and no `[PdfKey]` attribute: the type is the generic argument at the call site.

**Why it is explicit.** The two members are implemented explicitly (`static T IPdfDictionaryValue<T>.Create(...)`) because a derived class's `Create` would otherwise hide its base class's with a different return type. **Why the wrapping constructor matters.** A class without one used to fail at run time
when a plain dictionary was found under its key (reflection: "No appropriate constructor"). `PdfValueFactoryTests` creates and wraps each production class; add a case there when adding a class.

**Trap seen.** An analyzer reports a private constructor that is only reached through `Wrap` or reflection as unused (IDE0051); check before deleting one.
