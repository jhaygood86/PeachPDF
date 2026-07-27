# The reflow fixtures are platform-sensitive by design

_A trap this repo has paid for at least once._

`ManyParagraphsAcrossPages_ReflowConverges_EachBlockOwnPageMeasure` has caught a §5.4 regression **twice**, both times on `windows-latest` only, because its font metrics put the fixture at a different page boundary. Anything moving content between pages in a per-page-margin document must not read a green Linux run as a verdict.
