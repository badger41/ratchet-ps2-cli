namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionComposer
{
    public static UyaCollisionComposition Compose(
        byte[] sourceBytes,
        IReadOnlyList<UyaCollisionPieceEdit> edits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(edits);
        var source = UyaCollisionReader.Read(sourceBytes);
        var byKey = new Dictionary<(UyaCollisionPieceKind Kind, int Index), UyaCollisionPieceEdit>();
        foreach (var edit in edits)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateEdit(source, edit);
            if (!byKey.TryAdd((edit.Kind, edit.SourcePieceIndex), edit))
            {
                throw new ArgumentException(
                    $"Collision piece {edit.Kind} {edit.SourcePieceIndex} has more than one edit.",
                    nameof(edits));
            }
        }

        var effective = byKey.Values
            .Where(edit => edit.Remove
                || edit.TranslationX64 != 0
                || edit.TranslationY64 != 0
                || edit.TranslationZ64 != 0)
            .OrderBy(edit => edit.Kind)
            .ThenBy(edit => edit.SourcePieceIndex)
            .ToArray();
        if (effective.Length == 0)
        {
            return new(sourceBytes, false, []);
        }

        var solidPieces = new List<UyaCollisionSolidPiece>();
        foreach (var piece in source.SolidPieces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byKey.TryGetValue((UyaCollisionPieceKind.Solid, piece.SourceIndex), out var edit))
            {
                solidPieces.Add(piece);
            }
            else if (!edit.Remove)
            {
                solidPieces.Add(new(piece.SourceIndex, piece.Faces
                    .Select(face => Translate(face, edit))
                    .ToArray()));
            }
        }

        var barriers = new List<UyaCollisionPlayerBarrier>();
        foreach (var barrier in source.PlayerBarriers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!byKey.TryGetValue((UyaCollisionPieceKind.PlayerBarrier, barrier.SourceIndex), out var edit))
            {
                barriers.Add(barrier);
            }
            else if (!edit.Remove)
            {
                barriers.Add(new(
                    barrier.SourceIndex,
                    new(
                        Translate(barrier.BoundingSphere.Center, edit),
                        barrier.BoundingSphere.Radius64),
                    barrier.Vertices.Select(vertex => Translate(vertex, edit)).ToArray(),
                    barrier.Triangles));
            }
        }

        var expected = new UyaMapCollision(solidPieces, barriers, 0, 0, 0);
        var bytes = UyaCollisionWriter.Write(expected, cancellationToken);
        Verify(expected, UyaCollisionReader.Read(bytes));
        return new(bytes, true, effective);
    }

    private static void ValidateEdit(UyaMapCollision source, UyaCollisionPieceEdit edit)
    {
        if (edit.SourcePieceIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(edit), "Collision source piece index cannot be negative.");
        }

        var count = edit.Kind switch
        {
            UyaCollisionPieceKind.Solid => source.SolidPieces.Count,
            UyaCollisionPieceKind.PlayerBarrier => source.PlayerBarriers.Count,
            _ => throw new ArgumentOutOfRangeException(nameof(edit), $"Unknown collision piece kind {edit.Kind}."),
        };
        if (edit.SourcePieceIndex >= count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(edit),
                $"Collision {edit.Kind} piece {edit.SourcePieceIndex} does not exist; source count is {count}.");
        }

        if (edit.Kind == UyaCollisionPieceKind.Solid
            && ((edit.TranslationX64 & 3) != 0 || (edit.TranslationY64 & 3) != 0))
        {
            throw new ArgumentException(
                "Solid collision X/Y translation must use 1/16-unit increments.",
                nameof(edit));
        }
    }

    private static UyaCollisionSolidFace Translate(
        UyaCollisionSolidFace face,
        UyaCollisionPieceEdit edit) => new(
            face.Type,
            Translate(face.A, edit),
            Translate(face.B, edit),
            Translate(face.C, edit),
            face.IsQuad ? Translate(face.D, edit) : default,
            face.IsQuad);

    private static UyaCollisionVertex Translate(
        UyaCollisionVertex vertex,
        UyaCollisionPieceEdit edit) => new(
            checked(vertex.X64 + edit.TranslationX64),
            checked(vertex.Y64 + edit.TranslationY64),
            checked(vertex.Z64 + edit.TranslationZ64));

    private static void Verify(UyaMapCollision expected, UyaMapCollision actual)
    {
        var expectedFaces = expected.SolidPieces.SelectMany(piece => piece.Faces).ToHashSet();
        var actualFaces = actual.SolidPieces.SelectMany(piece => piece.Faces).ToHashSet();
        if (!expectedFaces.SetEquals(actualFaces))
        {
            throw new InvalidDataException("Composed UYA solid collision failed semantic verification.");
        }

        if (expected.PlayerBarriers.Count != actual.PlayerBarriers.Count)
        {
            throw new InvalidDataException("Composed UYA player barrier count failed semantic verification.");
        }

        for (var index = 0; index < expected.PlayerBarriers.Count; index++)
        {
            var left = expected.PlayerBarriers[index];
            var right = actual.PlayerBarriers[index];
            if (left.BoundingSphere != right.BoundingSphere
                || !left.Vertices.SequenceEqual(right.Vertices)
                || !left.Triangles.SequenceEqual(right.Triangles))
            {
                throw new InvalidDataException(
                    $"Composed UYA player barrier {index} failed semantic verification.");
            }
        }
    }
}
