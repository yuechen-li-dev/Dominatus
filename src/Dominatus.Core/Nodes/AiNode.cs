using Dominatus.Core.Runtime;

namespace Dominatus.Core.Nodes;

public delegate IEnumerator<AiStep> AiNode(AiWorld world, AiAgent agent);