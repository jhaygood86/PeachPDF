# Canvas tainting trusts crossorigin and judges a redirect by its first address

A cross-origin image drawn on a canvas taints it unless its element has a `crossorigin` attribute, in which case it is treated as allowed. The resource loader does not report the
response's `Access-Control-Allow-Origin` header or the address a redirect ended at, so a server that did not allow the page (where a browser fails the load) is read as allowed, and an
image that redirects to another origin is judged by the address it was asked for. The `:visited` colors that taint a canvas through an SVG filter cannot happen, because `:visited`
never matches. Closing it means the loader reporting the final address and the CORS result with the image; then `SourceTainted` compares the final address and fails the load.
