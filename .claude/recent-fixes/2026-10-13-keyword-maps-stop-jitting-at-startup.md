# The CSS keyword maps no longer cost ~2,700 JIT-compiled methods at startup

Rendering "hello" from a ReadyToRun-compiled CLI took ~580 ms and a plain JIT build ~890 ms. A trace of the JIT events showed
4,524 methods compiled at run time in the ReadyToRun build, and 2,717 of them were `Dictionary<string, E>`,
`FrozenDictionary<string, E>` and their `KeyCollection`/`ValueCollection`/`LengthBuckets…` helpers for E in the ~120 CSS enums
that `PeachPDF.CSS.Map` keyword tables are keyed to. A framework generic instantiated over a value type of *our* assembly is
outside our ReadyToRun version bubble, so crossgen cannot precompile it, and `Map`'s static constructor (reached from the first
declaration the UA stylesheet parses, through `Converters`) built all 120 at once: ~60% of the time to first page.

**The change.** `Map`'s fields are `KeywordMap<T>`: a frozen `string -> index` lookup (`FrozenDictionary<string, int>`, one
instantiation shared by every map and precompiled) over a plain `T[]` (our own generic, which crossgen does compile). Same
collection-initializer declarations (`new KeywordMap<T>(comparer) { {k, v} }.Freeze()`), and it still implements
`IReadOnlyDictionary<string, T>`, which is all the grammar (`ToConverter`, `CssProperty.FromCssText`) and the generated registry
consume. The arrays are grown by hand: `List<T>` and `Array.Resize<T>` over an enum would bring the same JIT back.

**Measured** (hello page, best of 5, this machine): ReadyToRun 582 -> 283 ms; plain JIT 887 -> 663 ms; jitted methods
4,524 -> 1,581. The Native AOT CLI was never affected (87 ms; 114-283 ms on the small benchmark documents, level with Typst).

**Traps.** Check this with a JIT-event trace (`Microsoft-Windows-DotNETRuntime:0x10`, `MethodJittingStarted`), not a CPU
profile: the time shows up as unmanaged code. Any new `Dictionary<string, SomeOurEnum>`/`List<SomeOurStruct>` reached on the
startup path brings it back; use a map over `int`/an array, or keep it lazy. Remaining startup JIT is ~1,500 methods
(async state machines, LINQ over our types, `List<OurStruct>`), 2-3% each.

**Evidence.** Release suite, SourceGenerators and Cli tests green; PDF words and drawings identical to main on 19 benchmark
documents and 220 showcase PDFs.
