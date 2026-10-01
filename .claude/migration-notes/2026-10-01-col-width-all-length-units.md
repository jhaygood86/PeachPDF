# `<col>` width now honours mm/cm/pt/in/em

Before: a `<col style="width: 30mm">` was ignored and columns split evenly. Now the width applies, matching a `<td>` and browsers. Templates that worked around it with `<col width="px">` are unaffected.
