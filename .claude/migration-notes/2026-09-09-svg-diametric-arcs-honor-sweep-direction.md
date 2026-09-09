# SVG arcs between diametrically opposite points honor `sweep-flag`

An SVG elliptical-arc command whose endpoints are exactly opposite each other on the ellipse could
choose the wrong semicircle. In particular, two clockwise 180-degree arcs intended to form a circle
could overlap on one side and leave the other side empty.

Such arcs now select the side requested by `sweep-flag`. The `large-arc-flag` remains geometrically
irrelevant for an exact 180-degree arc, as required by SVG's endpoint arc model.
