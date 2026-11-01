namespace RatchetPs2.Core.Occlusion;

public sealed class OcclusionCoverageSeedException(int graphIndex, int seedIndex, string message) : InvalidOperationException(message)
{
    public int GraphIndex { get; } = graphIndex;
    public int SeedIndex { get; } = seedIndex;
}
