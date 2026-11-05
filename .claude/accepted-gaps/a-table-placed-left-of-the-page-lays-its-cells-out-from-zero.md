# A table placed left of the page by its own margin or offset lays its cells out from x=0

`CssLayoutEngineTable.ColumnAxisFloor` floors the column-axis start at 0, so `<table style="margin-left:-100px">` draws its box at -100 while its cells start at 0 (Chrome: cells inside the box). The floor is lifted for a floated table and for one anchored to an `rtl` container's right edge, which can legitimately start left of the page; lifting it for every in-flow table was left out of that change (tracking issue #2195).
