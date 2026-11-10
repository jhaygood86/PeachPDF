using BenchmarkDotNet.Running;

// Run from src/ (a Release build; BenchmarkDotNet refuses a Debug one), on a machine otherwise idle - never during a test or coverage run:
//
//   dotnet run -c Release --project harness/PeachDrawing.Benchmarks -- --filter "*FillBenchmarks*"
//   dotnet run -c Release --project harness/PeachDrawing.Benchmarks -- --filter "*FilterBenchmarks*"
//
// With no --filter the switcher lists the benchmarks and asks which to run.
BenchmarkSwitcher.FromAssembly(typeof(PeachDrawing.Benchmarks.FillBenchmarks).Assembly).Run(args);
