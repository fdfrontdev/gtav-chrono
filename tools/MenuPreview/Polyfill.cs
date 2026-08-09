// Polyfill: IsExternalInit / RequiredMemberAttribute / CompilerFeatureRequiredAttribute
// for net48 (records + required members need these compiler-visible types).
namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}
