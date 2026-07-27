# A *target* may cross a container; a *move* may only do so with a fit test

_CSS Fragmentation Level 3. Tracker: [#320](https://github.com/jhaygood86/PeachPDF/issues/320)._

Resolving a target across a container is free, because only the target moves. Moving a run's members while the container stays put is not a state layout settles into — the container has to travel too, and only where it *fits* the destination (`EarlyBreak`'s own band check standing in for `CanBeLaidOutAgain`'s). A container that cannot travel is left behind as a stated rung on the ladder, not a silent skip.
