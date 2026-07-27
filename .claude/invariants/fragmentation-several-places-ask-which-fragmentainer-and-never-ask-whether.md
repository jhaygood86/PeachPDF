# Several places ask *which* fragmentainer and never ask whether breaking is live

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

**Several places ask *which* fragmentainer and never ask whether breaking is live**, so a suppressed context cannot stop them — only an absence can. `CurrentFragmentainer is { HasOwnBand: true }` appears five times in `CssBox.LayoutBlockChildren` alone, none of them consulting `IsFragmenting`, which is how a table cell reached the *column* arms of that loop while the table engine was laying it out (#400 (c)). The driver's own last-resort relaxation is the one pass that is genuinely suppressed rather than absent: it has a real slot the emitter reads, and only must not break again.
