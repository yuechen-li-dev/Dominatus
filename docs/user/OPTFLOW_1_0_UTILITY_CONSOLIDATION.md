# OptFlow 1.0 utility consolidation

`Dominatus.OptFlow` additionally contains the explicit pending-safe `Operation`/`Ai.Perform` authoring substrate; it does not alter the retained UtilityLite surface.

Utility decision authoring is part of the `Dominatus.OptFlow` package experience. New projects install one package:

```xml
<PackageReference Include="Dominatus.OptFlow" Version="future-release-version" />
```

The vocabulary intentionally keeps its historical namespace:

```csharp
using Dominatus.OptFlow;
using Dominatus.UtilityLite;

var score = Utility.Bb(Keys.Alerted);
var fallback = When.Score((_, _) => 0.4f);
```

This is deliberate. A `Dominatus.OptFlow.Utility` alongside `Dominatus.UtilityLite.Utility` would make existing projects ambiguous when both namespaces are imported. `Utility` and `When` are therefore implemented once, in the OptFlow assembly, while retaining the source-compatible `Dominatus.UtilityLite` namespace.

`Dominatus.UtilityLite` remains supported as a compatibility package. It depends on OptFlow and type-forwards `Utility` and `When`, so old source and previously compiled consumers resolve the exact same types. There are no utility wrappers, duplicate scoring implementations, or semantic changes.

Migration is package-only for most applications: remove the direct UtilityLite package reference, keep `using Dominatus.UtilityLite;`, and reference OptFlow. Projects may reference both packages during transition without source ambiguity because there is only one forwarded type identity.

The dependency direction is `Dominatus.Core <- Dominatus.OptFlow <- Dominatus.UtilityLite`. Publish in that order. The first release containing this change should use a new package version; no removal date is declared for UtilityLite.
