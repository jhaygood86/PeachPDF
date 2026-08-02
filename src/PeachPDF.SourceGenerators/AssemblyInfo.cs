using System.Runtime.CompilerServices;

// The <InternalsVisibleTo> MSBuild item convenience isn't reliably picked up for a netstandard2.0
// analyzer project referenced only via OutputItemType="Analyzer" (see PeachPDF.SourceGenerators.csproj's
// remarks), so this is declared explicitly instead.
[assembly: InternalsVisibleTo("PeachPDF.SourceGenerators.Tests")]
