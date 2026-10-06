# A scroll container with a `max-height` breaks between its lines instead of moving whole

Before: `overflow: hidden | auto | scroll` with an auto `height` and a `max-height` was kept in one piece across
pages: it moved to the next page, or was sliced when taller than a page. After: it breaks between its lines like a
block while its content is under the cap, the way a browser prints it, so a long capped preview or card can start on
the page where the previous content ended. The box ends where its content reaches the cap, counted across the pages
it breaks over, and content past the cap is clipped as before. A capped box that holds a table, flex or grid container
still stays whole.
