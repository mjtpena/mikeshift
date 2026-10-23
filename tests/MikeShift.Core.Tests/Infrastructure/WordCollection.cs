namespace MikeShift.Core.Tests;

/// <summary>
/// Test classes that start a real Microsoft Word (directly, or through a CLI run on a legacy .doc) share this collection, so
/// they never run at the same time. Word instances launched together stall each other, and the Word tests look at the machine-wide
/// list of WINWORD.EXE processes ("my Word is gone after a cancel"), which another test's Word would change under them.
/// </summary>
[CollectionDefinition(Name)]
public sealed class WordCollection
{
    public const string Name = "WordCom";
}
