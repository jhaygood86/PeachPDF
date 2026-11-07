# A text input, select or textarea inherits the surrounding `line-height`

The default stylesheet does not give `input`, `select` and `textarea` `line-height: normal`, as a browser's does, so `<div style="line-height:150%">Before <input> mid</div>` has a line 10px taller than in Chrome (the next block at 32.2px where Chrome has 22; tracking issue #2285). `button` is right. Same for `line-height:1.5`: it was hidden for a percentage only while a percentage gave every line zero height.
