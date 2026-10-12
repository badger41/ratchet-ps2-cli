using RatchetPs2.Core.Tfrags;
using RatchetPs2.Sdk;

internal static class TfragPieceTransformTests
{
    public static void Run()
    {
        var vertices = new[]
        {
            Vertex(new(1, 0, 0), new(1, 0, 0), new(0, 1, 0)),
            Vertex(new(0, 1, 0), new(0, 1, 0), new(0, 0, 1)),
            Vertex(new(0, 0, 1), new(0, 0, 1), new(1, 0, 0)),
        };
        var geometry = new TfragPieceGeometry(
            new(new(0, 0, 0), new(1, 1, 1), 1),
            [7],
            [
                new(0, [new(new(7, false, false), vertices, [0, 1, 2])]),
                new(1, []),
                new(2, []),
            ]);
        var halfRoot = MathF.Sqrt(0.5f);
        var transform = new TfragPieceTransform(
            new(10, 20, 30),
            new(0, 0, halfRoot, halfRoot),
            new(2, 2, 2));

        var transformed = TfragPieceConverter.Transform(geometry, transform);
        var result = transformed.Lods[0].Primitives[0];
        Equal(new TfragPieceVector3(10, 22, 30), result.Vertices[0].Position,
            "position should apply scale, rotation, and translation in native space");
        Near(new(0, 1, 0), result.Vertices[0].Normal,
            "normal should rotate and remain normalized");
        Near(new(-1, 0, 0), result.Vertices[0].LightNormal,
            "light normal should rotate and remain normalized");
        Expect(result.Indices.SequenceEqual([0u, 1u, 2u]),
            "positive uniform transforms should preserve triangle winding");
        Equal(new TfragPieceVector3(8, 20, 30), transformed.Bounds.Minimum,
            "world bounds minimum should be rebuilt from transformed vertices");
        Equal(new TfragPieceVector3(10, 22, 32), transformed.Bounds.Maximum,
            "world bounds maximum should be rebuilt from transformed vertices");
        Equal(2f, transformed.Bounds.Radius, "uniform scale should update the source radius");
        Expect(TfragPieceCanonicalCodec.Write(transformed).SequenceEqual(
                TfragPieceCanonicalCodec.Write(TfragPieceConverter.Transform(geometry, transform))),
            "repeated tfrag transforms should round deterministically");
        Equal(new TfragPieceVector3(1, 0, 0), vertices[0].Position,
            "transforming a piece should not mutate canonical source geometry");

        ExpectInvalid(() => TfragPieceConverter.ValidateTransform(transform with { Scale = new(2, 1, 2) }),
            "non-uniform tfrag scale should be rejected");
        ExpectInvalid(() => TfragPieceConverter.ValidateTransform(transform with { Scale = new(-1, -1, -1) }),
            "mirrored tfrag scale should be rejected");
        ExpectInvalid(() => TfragPieceConverter.ValidateTransform(transform with { Rotation = new(0, 0, 0, 0) }),
            "empty tfrag rotation should be rejected");
        ExpectInvalid(() => TfragPieceConverter.ValidateTransform(transform with
            { Position = new(float.NaN, 0, 0) }), "non-finite tfrag transform should be rejected");
        ExpectInvalid(() => TfragPieceConverter.ValidateTransform(
            new(new(1, 0, 0), new(0, 1, 1), 1), transform),
            "invalid tfrag bounds should be rejected before transforming geometry");
        ExpectOverflow(() => TfragPieceConverter.Transform(
            geometry,
            transform with { Position = new(0, 0, 0), Scale = new(float.MaxValue, float.MaxValue, float.MaxValue) }),
            "overflowing tfrag output should be rejected");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        ExpectCanceled(() => TfragPieceConverter.Transform(geometry, transform, cancellation.Token));
    }

    private static TfragPieceVertex Vertex(
        TfragPieceVector3 position,
        TfragPieceVector3 normal,
        TfragPieceVector3 lightNormal) => new(
        position,
        normal,
        new(0, 0),
        new(1, 1, 1, 1),
        0,
        new(1, 1, 1, 1),
        lightNormal,
        1);

    private static void Near(TfragPieceVector3 expected, TfragPieceVector3 actual, string message)
    {
        if (MathF.Abs(expected.X - actual.X) <= 0.000001f
            && MathF.Abs(expected.Y - actual.Y) <= 0.000001f
            && MathF.Abs(expected.Z - actual.Z) <= 0.000001f) return;
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void Equal<T>(T expected, T actual, string message) where T : notnull
    {
        if (EqualityComparer<T>.Default.Equals(expected, actual)) return;
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void ExpectOverflow(Action action, string message)
    {
        try { action(); }
        catch (OverflowException) { return; }
        throw new InvalidOperationException(message);
    }

    private static void ExpectCanceled(Action action)
    {
        try { action(); }
        catch (OperationCanceledException) { return; }
        throw new InvalidOperationException("Canceled tfrag transform should not return partial output.");
    }
}
