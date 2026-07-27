# "Monolithic" is two questions, and so are the gates around it

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

§2's own set (replaced elements, scroll containers) is not PeachPDF's "an engine that paginates its own content"; `MonolithicContent` keeps them apart, and the engine set is now the **table alone**. Separately, `IsFragmenting` asks *may a break token be recorded here?* while `SuppressWordPageBreaks` gates the legacy per-word relocation — a multi-column container is monolithic to the driver while its content still paginates word by word.
