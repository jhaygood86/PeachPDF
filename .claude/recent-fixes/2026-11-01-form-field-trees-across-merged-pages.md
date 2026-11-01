# Form fields that span pages: pruning in ObjectCopy, one name in Redraw

- **ObjectCopy**: `PdfObjectCopier` copied a field's whole `/Kids` tree, so taking some pages left the widgets of the other pages in the
  field with no page. `FinishForm` now runs `PruneToPlacedWidgets` (widgets are recorded in `_placedWidgets` as they are put on a page):
  kids that were not placed are removed, a field with none left is not added to the form, and a radio group / check box whose `/V` named
  a removed widget's state is reset to `/Off` (with its remaining widgets' `/AS`).
- **Redraw**: the unique-name map in `CarryNavigation` was per output page, so a field `total` with widgets on source pages 1 and 2 came
  out as `total` and `total_2`. It is now keyed by (source, round, name), where a round counts how many times that source page has been
  placed, so the pages of one round share one field and a page taken again still gets fields of its own.

Evidence: `PdfObjectCopyTests` (`FieldWhoseOtherWidgetWasOnAPageNotTaken...`, `RadioGroup_WhoseChosenButtonWasOnAPageNotTaken...`) and
`PdfOperationTests.RedrawnFormField_WithWidgetsOnSeveralPages...`; the `TwoPageField` helper builds the two-page fixture.
