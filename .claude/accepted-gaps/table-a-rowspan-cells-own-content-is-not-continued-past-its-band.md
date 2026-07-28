# A rowspan cell's own content is not continued past the band it began in

[#511](https://github.com/jhaygood86/PeachPDF/issues/511) fragments a `rowspan` cell's **box** where the
span reaches out of the band it was placed in: the box closes level with the table's slice on that band
and `CloseSpanningCell` states the depth it covers in each later band as continuation geometry.

Where the cell's own *content* is taller than that band, the close is declined and the cell keeps one box
running past the boundary, as it did before. [Issue #521](https://github.com/jhaygood86/PeachPDF/issues/521),
stated reader-facing under [Breaks between table rows](../../docs/html-css-support.md#breaks-in-tables).

**The guard is a measured requirement, not caution.** Only the box is fragmented, so closing one above its
own content puts everything below the close inside no fragment at all. Removing the third clause of

```csharp
if (cellSlot < slot
    && HtmlContainerInt.FallsPast(rowMaxBottom, band)
    && !HtmlContainerInt.FallsPast(CssBox.GetMaximumBottom(cell, 0d), band))
```

fails `TableCellBreakTokenTests.APaginatingTable_DropsNoWord` on its `rowspan` case with roughly 100 words
*"claimed by no page"*. That theory is the suite's word census and is the only thing that catches it.

The case #511 does close is narrow for a real reason worth not re-deriving: a spanning cell whose content
did not fit the band it was placed in **stopped** when its own row was placed, and the row loop stops at a
row a cell stopped in — so a spanning cell that reaches its ending row is normally one whose content fits
where it already is. This gap is the residue: a cell that overflows without stopping.

Closing it needs the flow-level continuation §6.1 asks for rather than a stated rectangle — the cell
resuming in the next fragmentainer from where its own content stopped, which is the
`UnfinishedCells`/`TableBreakToken` machinery applied to a cell owned by an **earlier** row than the one
the row loop is placing. That is [#390](https://github.com/jhaygood86/PeachPDF/issues/390)'s
size-then-position split.
