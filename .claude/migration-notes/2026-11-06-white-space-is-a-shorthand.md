# white-space is now a shorthand of white-space-collapse and text-wrap-mode

Before: `white-space` was a single longhand. Now it is a CSS Text 4 shorthand over `white-space-collapse` and
`text-wrap-mode`, so `getPropertyValue('white-space')` through the CSS-OM serializes from those two, and
`white-space` also resets both. New properties: `white-space-collapse`, `text-wrap-mode`, `text-wrap-style`
treated like other `pre` content for wrapping and no longer a separate case.
