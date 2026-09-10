# Standard fixed-point formatting is not the PDF writer's custom format

The PDF graphics writer historically formats coordinates with custom invariant patterns such as
`0.####`. Do not replace that with `Utf8Formatter`'s `F4` output followed by trimming trailing zeroes.

A randomized comparison found real finite coordinates where the last emitted decimal differed (for
example, one path produced `-225163.6761824` with the custom format but `-225163.6761823` through the
apparently equivalent fixed-point route). Both APIs ultimately perform decimal conversion, but their
rounding paths are not interchangeable.

Any numeric-writer optimization must compare against `double.ToString`/`TryFormat` using the exact
existing custom format over boundary cases and a broad finite-value sample. PDF byte equivalence is the
contract; visually indistinguishable coordinates are not sufficient.
