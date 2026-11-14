# Table rows now honor `break-after/before: avoid` and `break-inside: avoid` on row groups

Before: a paginated table could leave a heading row (`break-after: avoid`) alone at the foot of a page, and a
`<tbody>` with `break-inside: avoid` could be split between rows. Now the heading moves to the next page with the
row after it and the tbody stays whole when it fits a page (css-break-3 §4.4); an unsatisfiable avoid is relaxed.
Documents using these values on rows will paginate differently (pages may end earlier).
