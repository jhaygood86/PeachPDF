# Read the test count, not the word

_A trap this repo has paid for at least once._

A host crash (SIGABRT) still prints a "passing" summary, for however many tests completed — 10, 29 or 209 out of 6,400. And `Console.WriteLine` is swallowed under `dotnet test`, so `Console.SetOut` to a `StringWriter` inside the test is what makes an engine trace readable; #353's height half was invisible from the outside until the table's own child list was printed either side of the restore.
