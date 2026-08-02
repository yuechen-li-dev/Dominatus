using System.Runtime.CompilerServices;

// The utility vocabulary is implemented by Dominatus.OptFlow.  Keep the old
// assembly identity as a compatibility shim for existing compiled consumers.
[assembly: TypeForwardedTo(typeof(Dominatus.UtilityLite.Utility))]
[assembly: TypeForwardedTo(typeof(Dominatus.UtilityLite.When))]
