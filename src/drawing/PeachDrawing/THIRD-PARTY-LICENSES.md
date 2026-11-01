# Third-Party Licenses (PeachDrawing)

PeachDrawing is BSD 3-Clause-licensed (see [LICENSE](../../LICENSE)). The source it is built from includes the following third-party work, which carries its own terms. The notice ships inside the NuGet package so it travels with the binaries.

## W3C Filter Effects Module Level 1 (feTurbulence reference implementation)

- **Location:** [`Filters/Turbulence.cs`](Filters/Turbulence.cs): a C# transliteration of the reference implementation of the `feTurbulence` Perlin noise published in the specification. Its constants, seeding, lattice and gradient set-up, noise function and stitching logic are the specification's, so a given seed, frequency and octave count gives the same picture as every conforming renderer.
- **Upstream source:** [Filter Effects Module Level 1](https://www.w3.org/TR/filter-effects-1/), the `feTurbulence` reference code
- **Copyright:** Copyright © 2018 W3C® (MIT, ERCIM, Keio, Beihang)
- **License:** the W3C permissive document license that the specification states it is published under (see <https://www.w3.org/copyright/>)

Everything else in this package is original PeachPDF code or follows other public specifications (W3C Compositing and Blending Level 1, Filter Effects Level 1) without reproducing code.
