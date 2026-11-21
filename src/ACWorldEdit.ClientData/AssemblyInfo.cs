using System.Runtime.CompilerServices;

// The xUnit project compiles the asset layer as source today; once it is a referenced assembly the
// retail native reader (CascNative) and archive internals must stay reachable from its tests.
[assembly: InternalsVisibleTo("ACWorldEdit.Tests")]
