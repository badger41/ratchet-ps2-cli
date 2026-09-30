using System.Numerics;
using RatchetPs2.Core.Games;
using RatchetPs2.Games.UYA.Builders;

namespace RatchetPs2.Sdk;

public sealed record SkyboxShellComposition(
    ReadOnlyMemory<byte> SkyboxBytes,
    int ShellIndex,
    Vector3? InitialRotationRadians = null,
    Vector3? AngularVelocityRadiansPerSecond = null);

public sealed record SkyboxCompositionResult(byte[] Bytes, bool IsBasePassThrough);

public static class SkyboxComposer
{
    public static SkyboxCompositionResult Compose(
        GameId gameId,
        ReadOnlySpan<byte> baseSkyboxBytes,
        IReadOnlyList<SkyboxShellComposition> shells,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shells);

        var result = gameId switch
        {
            GameId.UYA => UyaSkyboxComposer.Compose(
                baseSkyboxBytes,
                shells.Select(shell => new UyaSkyboxComposer.ShellComposition(
                    shell.SkyboxBytes,
                    shell.ShellIndex,
                    shell.InitialRotationRadians,
                    shell.AngularVelocityRadiansPerSecond)).ToArray(),
                cancellationToken),
            _ => throw new NotSupportedException($"Skybox composition is not supported for {gameId}."),
        };

        return new SkyboxCompositionResult(result.Bytes, result.IsBasePassThrough);
    }
}
