using Xunit;

namespace Lyra.Core.Tests.Duplicates;

/// <summary>
/// Tests that touch the static <c>FileRecordDatabase</c> share global state, so they
/// must not run in parallel with each other.
/// </summary>
[CollectionDefinition("FileRecordDatabase", DisableParallelization = true)]
public sealed class FileRecordDatabaseCollection;
