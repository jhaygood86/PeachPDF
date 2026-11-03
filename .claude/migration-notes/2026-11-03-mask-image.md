# `mask-image` now masks the element

Before, `mask-image`, `mask-mode`, `mask-repeat`, `mask-position`, `mask-size` and `mask-origin` (and their `-webkit-` forms) were ignored on HTML elements and the element drew solid. Now gradient and `url()` image layers mask the element and its content through a PDF soft mask. The `mask` shorthand, `mask-composite` and `mask-clip` are still not applied.
