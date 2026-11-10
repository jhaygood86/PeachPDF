# Building PeachPDF requires the .NET 11 SDK

**Before:** a machine with only the .NET 10 SDK built net10.0; net11.0 joined when the 11 SDK was present.
**Now:** the 11 SDK is required (`global.json`), and every package is always built for net10.0 and net11.0. Package consumers
are unaffected (still net10.0 and net11.0 assets); contributors and forks need the 11 SDK. net11.0 assets are compiled with
runtime-async.
