using System.Runtime.CompilerServices;

// The xUnit project references this assembly, so the retail native reader (CascNative) and archive
// internals must stay reachable from its tests.
[assembly: InternalsVisibleTo("ACWorldEdit.Tests")]
