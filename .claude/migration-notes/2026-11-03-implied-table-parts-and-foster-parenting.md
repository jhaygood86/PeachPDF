# Tables follow the HTML parsing algorithm

Before: a `<tr>` directly in a `<table>` had no `<tbody>` around it, a `<td>` or `<caption>` anywhere created a table part, and text or elements inside a
`<table>` stayed there; an `<option>` was closed only by the next `<option>` directly after it.

Now, as in a browser:

- `<table><tr>` gets a `<tbody>` (`table > tbody > tr` matches, `table > tr` does not), a cell outside a row gets a `<tr>` and `<tbody>`, `<col>` a `<colgroup>`.
- A `<td>`, `<th>`, `<tr>`, `<tbody>` or `<caption>` outside a table is dropped; its text stays.
- Text and elements a table cannot hold are moved in front of the table: `<table>x<tr><td>y` is `x` then the table, `<table><div>a</div>` puts the `<div>` before it.
- A second `<form>` inside a `<form>` is dropped, and `</form>` does not close what is open inside the form.
- In a `<select>` only options, option groups and text are kept.

A stylesheet or script-free document that relies on the old shapes (a `<div>` as a direct child of a `<table>`, or `table > tr`) renders differently.
