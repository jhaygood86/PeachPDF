# A `nowrap` span after a float that leaves it no room is placed below the float

A `white-space: nowrap` element whose text does not fit beside a preceding float (a float as wide as its container, for
example) used to be drawn past the container's right edge, clipped away. It is now placed below the float, at the start of
the line, as a browser does.
