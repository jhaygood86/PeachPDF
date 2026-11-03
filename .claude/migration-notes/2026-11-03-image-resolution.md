# `image-resolution` is now honoured

Before, `image-resolution` was an unknown property and every raster was sized at 96 dpi whatever its file recorded. The default is unchanged (`1dppx`), so no existing document changes size. A document that sets `image-resolution: from-image`, a `<resolution>` or `snap` now sizes its images' natural size from it, as in the CSS Images 4 draft (no browser implements it; Prince does). The density a file carries does not affect size unless `from-image` is asked for.
