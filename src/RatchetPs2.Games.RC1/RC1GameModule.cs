using RatchetPs2.Core.Games;
using RatchetPs2.Core.Moby;

namespace RatchetPs2.Games.RC1;

public sealed class RC1GameModule : IGameModule, IMobyGameModule
{
    public GameId Id => GameId.RC1;
    public string DisplayName => "Ratchet & Clank";
    public MobyAnimationFormat AnimationFormat => MobyAnimationFormat.Standard;
    public MobyModelFormat ModelFormat => MobyModelFormat.Rc1;
}
