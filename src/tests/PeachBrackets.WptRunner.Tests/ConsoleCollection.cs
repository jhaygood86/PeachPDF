namespace PeachBrackets.WptRunner.Tests;

/// <summary>Tests that redirect the process-wide console run one at a time.</summary>
[CollectionDefinition("Console", DisableParallelization = true)]
public sealed class ConsoleCollection;
