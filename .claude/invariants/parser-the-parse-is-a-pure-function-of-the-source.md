# The parse is a pure function of the source

`HtmlParser.ParseDocument` must build the same tree from the same source every time, and element ordinals (`HtmlTag.SourceOrdinal`) are numbered by a
walk of the finished tree (`DomParser.NumberElements`), never during the parse.

Why: a host-driven change (form value, `:hover`/`:focus` state, text-area scroll, chosen files, attribute overrides) is applied by parsing the source
again and naming elements by ordinal, so an ordinal has to mean the same element in both parses. Anything the tree construction does that depends on
more than the source (time, a counter that survives the parse, iteration order of a hash set) makes the second parse number differently and the change
land on the wrong element. The adoption agency and reconstruction create and move elements; they are safe because the numbering happens afterwards, in
tree order.

Symptom to recognise: a form field's edit, focus ring or validation message appears on the neighbouring control after a restyle, only on documents
with misnested markup.
