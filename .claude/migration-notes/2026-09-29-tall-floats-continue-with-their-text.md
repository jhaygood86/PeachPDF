# A float taller than a page continues with the text beside it

Until this change, a block-level float taller than the space left on its page laid its own content out
unbroken and each page showed the slice that fell in it. The text that flowed beside it did not carry on: on a
float taller than one page, most or all of the lines beside it were dropped after the first page.

Now the float breaks between its own lines and blocks like any block, and the text beside it carries on beside
the continuation on every page the float reaches, with content that clears the float following its last line.
A page count can change for a document with a tall float, and text that used to be missing is now present.

Two things a document author may notice next to that:

- Text that used to be dropped can now appear next to a `float: right` box that overlaps another `float: right`
  box (see the accepted gap on second right floats), so an overlap that was hidden by the lost text is visible.
- A float that is not tall is laid out as before.

Checked against the last release (v0.9.20), whose documentation already said a tall float "does not continue
*beside* the text that flows around it onto the next page".
