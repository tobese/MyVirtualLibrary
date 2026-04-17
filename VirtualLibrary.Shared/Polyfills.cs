// Polyfill for C# 9 init-only setters / record positional parameters
// when targeting older frameworks (e.g. netstandard2.1) that don't
// include the System.Runtime.CompilerServices.IsExternalInit type.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
#endif
