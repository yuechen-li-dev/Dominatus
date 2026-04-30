using Ariadne.OptFlow.Commands;

namespace Dominatus.StrideConn;

public interface IStrideDialogueSurface
{
    bool TryShowLine(DiagLineCommand command, Action onAdvance);
    bool TryShowChoose(DiagChooseCommand command, Action<string> onChoose);
    bool TryShowAsk(DiagAskCommand command, Action<string> onSubmit);
}
