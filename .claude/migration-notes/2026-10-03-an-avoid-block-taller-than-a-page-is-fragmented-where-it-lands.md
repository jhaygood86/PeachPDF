# A break-inside: avoid block taller than a page is fragmented where it lands

Before: an `avoid` block that fit no page and sat across a page foot was moved whole to the next page without being re-fragmented; lines past that page's foot were clipped (their words never drawn), and the move could leave blank pages behind it.

Now: it is laid out again at the next page's top and breaks at the foot like any block, so every word is drawn. A document that relied on the old output can change page count: one comparison document went from 4 pages to 2 (blank middle pages gone), another from 4 to 5 (a trailing forced `break-before: page` div now follows content that used to be lost).