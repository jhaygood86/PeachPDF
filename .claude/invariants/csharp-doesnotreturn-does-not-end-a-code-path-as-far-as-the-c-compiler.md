# `[DoesNotReturn]` does not end a code path as far as the C# compiler is concerned

_A trap this repo has paid for at least once._

It neither warns about a statement written after such a call (no CS0162) nor accepts its absence (CS0161 if you delete the `return`). So a throwing helper hides dead code from the compiler *and* from the reader, which is exactly how the backstop's whole recovery sat unexecuted behind a method named `ReportError` (#404). It is now `RenderError`, which **returns** the exception so every call site spells `throw` — after a literal `throw` the compiler does both things. Prefer that shape for any new failure helper.
