using System.Numerics;

namespace RatchetPs2.Games.UYA.Collision;

public static class UyaCollisionComposer
{
    private const int MaximumAdditions = 100_000;
    private const int MaximumAddedFaces = 1_000_000;

    private sealed record PreparedComposition(
        UyaMapCollision Collision,
        IReadOnlyList<UyaCollisionPieceEdit> EffectiveEdits,
        IReadOnlyDictionary<int, string> AdditionIds,
        bool Changed);

    public static UyaCollisionAnalysis AnalyzeComposition(
        byte[] sourceBytes,
        IReadOnlyList<UyaCollisionPieceEdit> edits,
        IReadOnlyList<UyaCollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(sourceBytes, edits, additions, cancellationToken);
        return UyaCollisionWriter.Analyze(
            prepared.Collision,
            prepared.AdditionIds,
            cancellationToken);
    }

    public static UyaCollisionComposition Compose(
        byte[] sourceBytes,
        IReadOnlyList<UyaCollisionPieceEdit> edits,
        CancellationToken cancellationToken = default)
        => Compose(sourceBytes, edits, [], cancellationToken);

    public static UyaCollisionComposition Compose(
        byte[] sourceBytes,
        IReadOnlyList<UyaCollisionPieceEdit> edits,
        IReadOnlyList<UyaCollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(sourceBytes, edits, additions, cancellationToken);
        if (!prepared.Changed)
        {
            return new(sourceBytes, false, []);
        }

        var analysis = UyaCollisionWriter.Analyze(
            prepared.Collision,
            prepared.AdditionIds,
            cancellationToken);
        var bytes = UyaCollisionWriter.Write(prepared.Collision, cancellationToken);
        Verify(prepared.Collision, UyaCollisionReader.Read(bytes));
        return new(bytes, true, prepared.EffectiveEdits, analysis);
    }

    public static UyaCollisionComposition EncodeStandalone(
        IReadOnlyList<UyaCollisionSolidAddition> additions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(additions);
        ValidateAdditions(additions, cancellationToken);
        var pieces = additions.Select((addition, index) => new UyaCollisionSolidPiece(
            index,
            addition.Faces.Select(UyaCollisionReader.Canonicalize).ToArray())).ToArray();
        var expected = new UyaMapCollision(pieces, [], 0, 0, 0);
        ValidateUniqueFaces(expected);
        var ownership = additions.Select((addition, index) => (addition.Id, index))
            .ToDictionary(value => value.index, value => value.Id);
        var analysis = UyaCollisionWriter.Analyze(expected, ownership, cancellationToken);
        var bytes = UyaCollisionWriter.Write(expected, cancellationToken);
        Verify(expected, UyaCollisionReader.Read(bytes));
        return new(bytes, true, [], analysis);
    }

    private static PreparedComposition Prepare(
        byte[] sourceBytes,
        IReadOnlyList<UyaCollisionPieceEdit> edits,
        IReadOnlyList<UyaCollisionSolidAddition> additions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceBytes);
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(additions);
        ValidateAdditions(additions, cancellationToken);
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
        if (effective.Length == 0 && additions.Count == 0)
        {
            return new(source, [], new Dictionary<int, string>(), false);
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

        var additionIds = new Dictionary<int, string>();
        for (var index = 0; index < additions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sourceIndex = checked(source.SolidPieces.Count + index);
            solidPieces.Add(new(sourceIndex, additions[index].Faces
                .Select(UyaCollisionReader.Canonicalize)
                .ToArray()));
            additionIds.Add(sourceIndex, additions[index].Id);
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
        ValidateUniqueFaces(expected);
        return new(expected, effective, additionIds, true);
    }

    private static void ValidateUniqueFaces(UyaMapCollision collision)
    {
        var faces = new HashSet<UyaCollisionSolidFace>();
        if (collision.SolidPieces.SelectMany(piece => piece.Faces).Any(face => !faces.Add(face)))
        {
            throw new InvalidDataException(
                "Composed UYA solid collision contains an exact duplicate logical face.");
        }
    }

    private static void ValidateAdditions(
        IReadOnlyList<UyaCollisionSolidAddition> additions,
        CancellationToken cancellationToken)
    {
        if (additions.Count > MaximumAdditions)
        {
            throw new ArgumentException(
                $"UYA collision additions exceed the {MaximumAdditions} item limit.",
                nameof(additions));
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var faces = new HashSet<UyaCollisionSolidFace>();
        var faceCount = 0;
        foreach (var addition in additions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ArgumentNullException.ThrowIfNull(addition);
            if (string.IsNullOrWhiteSpace(addition.Id) || addition.Id.Length > 256)
            {
                throw new ArgumentException(
                    "UYA collision addition ID must contain 1 through 256 non-whitespace characters.",
                    nameof(additions));
            }

            if (!ids.Add(addition.Id))
            {
                throw new ArgumentException(
                    $"UYA collision addition ID '{addition.Id}' is duplicated.",
                    nameof(additions));
            }

            ArgumentNullException.ThrowIfNull(addition.Faces);
            if (addition.Faces.Count == 0)
            {
                throw new ArgumentException(
                    $"UYA collision addition '{addition.Id}' has no faces.",
                    nameof(additions));
            }

            faceCount = checked(faceCount + addition.Faces.Count);
            if (faceCount > MaximumAddedFaces)
            {
                throw new ArgumentException(
                    $"UYA collision additions exceed the {MaximumAddedFaces} face limit.",
                    nameof(additions));
            }

            foreach (var face in addition.Faces)
            {
                ValidateFace(addition.Id, face);
                if (!faces.Add(UyaCollisionReader.Canonicalize(face)))
                {
                    throw new ArgumentException(
                        $"UYA collision addition '{addition.Id}' duplicates another added face.",
                        nameof(additions));
                }
            }
        }
    }

    private static void ValidateFace(string additionId, UyaCollisionSolidFace face)
    {
        if ((face.A.X64 & 3) != 0 || (face.A.Y64 & 3) != 0
            || (face.B.X64 & 3) != 0 || (face.B.Y64 & 3) != 0
            || (face.C.X64 & 3) != 0 || (face.C.Y64 & 3) != 0
            || face.IsQuad && ((face.D.X64 & 3) != 0 || (face.D.Y64 & 3) != 0))
        {
            throw new ArgumentException(
                $"UYA collision addition '{additionId}' X/Y coordinates require 1/16-unit precision.",
                nameof(face));
        }

        if (face.A == face.B || face.A == face.C || face.B == face.C
            || face.IsQuad && (face.D == face.A || face.D == face.B || face.D == face.C))
        {
            throw new ArgumentException(
                $"UYA collision addition '{additionId}' contains a face with repeated vertices.",
                nameof(face));
        }

        var ab = face.B.Position - face.A.Position;
        var ac = face.C.Position - face.A.Position;
        if (Vector3.Cross(ab, ac).LengthSquared() <= float.Epsilon)
        {
            throw new ArgumentException(
                $"UYA collision addition '{additionId}' contains a degenerate triangle.",
                nameof(face));
        }

        if (face.IsQuad)
        {
            var ad = face.D.Position - face.A.Position;
            if (Vector3.Cross(ac, ad).LengthSquared() <= float.Epsilon)
            {
                throw new ArgumentException(
                    $"UYA collision addition '{additionId}' contains a degenerate quad.",
                    nameof(face));
            }
        }
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
        var expectedFaces = expected.SolidPieces.SelectMany(piece => piece.Faces).ToArray();
        var actualFaces = actual.SolidPieces.SelectMany(piece => piece.Faces).ToArray();
        if (expectedFaces.Length != actualFaces.Length
            || !expectedFaces.ToHashSet().SetEquals(actualFaces))
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
