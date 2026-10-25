namespace MikeShift.Core.Tests;

/// <summary>
/// Test classes that start a real Microsoft Word (directly, or through a CLI run on a legacy .doc) share this collection, so
/// they never run at the same time. Word instances launched together stall each other, and the Word tests look at the machine-wide
/// list of WINWORD.EXE processes ("my Word is gone after a cancel"), which another test's Word would change under them.
/// The collection also holds <see cref="WordLock"/> for as long as it runs, so the CLI test assembly (which watches the same process list to
/// prove that a run did NOT start Word) cannot overlap with it either.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WordCollection : ICollectionFixture<WordLock>
{
    public const string Name = "WordCom";
}

/// <summary>
/// A machine-wide lock (a named semaphore, so it works across the two test assemblies, and across threads) around everything that starts
/// Word or asserts about Word processes. Held by <see cref="WordCollection"/> in this assembly and by <c>WordWatch</c> in MikeShift.Cli.Tests.
/// </summary>
public sealed class WordLock : IDisposable
{
    private const string Name = @"Local\MikeShift.Tests.WordProcesses";
    private readonly Semaphore _semaphore;

    /// <summary>Waits (up to 15 minutes) for every other holder to finish.</summary>
    public WordLock()
    {
        _semaphore = new Semaphore(1, 1, Name);
        if (!_semaphore.WaitOne(TimeSpan.FromMinutes(15)))
        {
            _semaphore.Dispose();
            throw new TimeoutException("Another test run has been using Word for more than 15 minutes.");
        }
    }

    public void Dispose()
    {
        _semaphore.Release();
        _semaphore.Dispose();
    }
}
