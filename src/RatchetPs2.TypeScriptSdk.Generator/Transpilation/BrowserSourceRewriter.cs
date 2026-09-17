using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace RatchetPs2.TypeScriptSdk.Generator;

// Rewrites C# syntax to the browser binding surface. JavaScript implementations
// belong in Runtime/*.ts, not Roslyn source strings or Transpose templates.
internal sealed class BrowserSourceRewriter(SemanticModel model, HashSet<Microsoft.CodeAnalysis.Text.TextSpan> unusedImports) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitQualifiedName(QualifiedNameSyntax node) =>
        BrowserIoType(model.GetSymbolInfo(node).Symbol) is { } name
            ? SyntaxFactory.ParseName(name).WithTriviaFrom(node)
            : base.VisitQualifiedName(node);

    public override SyntaxNode? VisitIdentifierName(IdentifierNameSyntax node) =>
        node.Parent is not QualifiedNameSyntax && BrowserIoType(model.GetSymbolInfo(node).Symbol) is { } name
            ? SyntaxFactory.ParseName(name).WithTriviaFrom(node)
            : base.VisitIdentifierName(node);

    public override SyntaxNode? VisitGenericName(GenericNameSyntax node)
    {
        if (model.GetTypeInfo(node).Type is INamedTypeSymbol type &&
            type.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.IReadOnlySet<T>")
            return SyntaxFactory.ParseName("global::System.Collections.Generic.ISet<" +
                type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">").WithTriviaFrom(node);
        return base.VisitGenericName(node);
    }

    public override SyntaxNode? VisitCheckedExpression(CheckedExpressionSyntax node) =>
        node.Keyword.IsKind(SyntaxKind.CheckedKeyword)
            ? SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)Visit(node.Expression)!).WithTriviaFrom(node)
            : base.VisitCheckedExpression(node);

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node)
    {
        var symbol = model.GetDeclaredSymbol(node);
        if (symbol?.ContainingType.ToDisplayString() == "RatchetPs2.Core.Textures.TextureConverter" &&
            symbol.Name is "Crc32" or "Adler32")
        {
            var checksumMethod = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            var arguments = symbol.Name == "Crc32" ? "chunkType, data, s_crcTable" : "data";
            return checksumMethod.WithBody((BlockSyntax)SyntaxFactory.ParseStatement(
                    $"{{ return global::RatchetPs2.JavaScript.ByteChecksums.{symbol.Name}({arguments}); }}"))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }
        if (symbol?.ContainingType.ToDisplayString() == "RatchetPs2.Core.Textures.TextureConverter" &&
            symbol.Name == "DecodeIndexed8")
        {
            var method = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            return method.WithBody((BlockSyntax)SyntaxFactory.ParseStatement("""
                {
                    ValidateDimensions(width, height);
                    global::RatchetPs2.JavaScript.Guards.ThrowIfNull(paletteData, "paletteData");
                    var pixelCount = global::System.CheckedArithmetic.MultiplyInt32(width, height);
                    if (pixelData.Length < pixelCount)
                        throw new global::System.ArgumentException("Indexed8 pixel data is smaller than the target image size.", "pixelData");
                    var palette = ReadPalette(paletteData, options);
                    var rgba = global::RatchetPs2.JavaScript.ByteTextures.DecodeIndexed8(
                        pixelData, palette, width, pixelCount, ShouldSwizzle(width, height, options), options?.DecodePaletteIndexes ?? true);
                    return new global::RatchetPs2.Core.Textures.Png.Rgba32Image(width, height, rgba);
                }
                """))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }
        if (symbol?.ContainingType.ToDisplayString() == "RatchetPs2.Core.Textures.TextureConverter" &&
            symbol.Name == "AnalyzeAlpha")
        {
            var method = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            return method.WithBody((BlockSyntax)SyntaxFactory.ParseStatement("""
                {
                    global::RatchetPs2.JavaScript.Guards.ThrowIfNull(image, "image");
                    var alpha = global::RatchetPs2.JavaScript.ByteTextures.AnalyzeAlpha(image.PixelData);
                    return new global::RatchetPs2.Core.Textures.Png.TextureAlphaInfo(alpha[0], alpha[1], alpha[2] != 0);
                }
                """))
                .WithExpressionBody(null)
                .WithSemicolonToken(default);
        }
        if (symbol?.ContainingType.ToDisplayString() == "RatchetPs2.Core.Wad.WadDecompressor" &&
            symbol.Name == "Decompress" && symbol.Parameters is [{ Type: IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte } }])
        {
            var method = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
            return method.WithBody(null)
                .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression(
                    "global::RatchetPs2.JavaScript.BrowserWad.Decompress(source)")))
                .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken));
        }
        var attribute = symbol?.GetAttributes()
            .FirstOrDefault(value => value.AttributeClass?.ToDisplayString() == "System.Text.RegularExpressions.GeneratedRegexAttribute");
        if (attribute is null) return base.VisitMethodDeclaration(node);
        var pattern = (string)attribute.ConstructorArguments[0].Value!;
        var result = (MethodDeclarationSyntax)base.VisitMethodDeclaration(node)!;
        return result.WithAttributeLists(default)
            .WithModifiers(SyntaxFactory.TokenList(result.Modifiers.Where(token => !token.IsKind(SyntaxKind.PartialKeyword))))
            .WithBody(null)
            .WithExpressionBody(SyntaxFactory.ArrowExpressionClause(SyntaxFactory.ParseExpression(
                "new global::System.Text.RegularExpressions.Regex(" + SymbolDisplay.FormatLiteral(pattern, true) + ")")))
            .WithSemicolonToken(SyntaxFactory.Token(SyntaxKind.SemicolonToken)).WithTriviaFrom(node);
    }

    public override SyntaxNode? VisitBinaryExpression(BinaryExpressionSyntax node)
    {
        if (node.IsKind(SyntaxKind.CoalesceExpression) && node.Right is CollectionExpressionSyntax { Elements.Count: 0 } &&
            node.Left is ConditionalAccessExpressionSyntax conditional &&
            model.GetOperation(conditional.WhenNotNull) is IInvocationOperation { TargetMethod: { Name: "OfType", TypeArguments: [var element] } } &&
            element.ToDisplayString() == "System.Text.Json.Nodes.JsonObject")
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.JsonNodes.OfTypeObjects"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                    (ExpressionSyntax)Visit(conditional.Expression)!)))).WithTriviaFrom(node);
        if (model.GetConstantValue(node) is { HasValue: true } constant)
        {
            if (constant.Value is float floatValue)
                return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(floatValue)).WithTriviaFrom(node);
            if (constant.Value is int intValue)
                return SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(intValue)).WithTriviaFrom(node);
        }
        if (model.GetOperation(node) is IBinaryOperation operation && operation.Type is { } type)
        {
            var floatMethod = type.SpecialType == SpecialType.System_Single ? operation.OperatorKind switch
            {
                BinaryOperatorKind.Add => "Add",
                BinaryOperatorKind.Subtract => "Subtract",
                BinaryOperatorKind.Multiply => "Multiply",
                BinaryOperatorKind.Divide => "Divide",
                BinaryOperatorKind.Remainder => "Remainder",
                _ => null
            } : null;
            if (floatMethod is not null)
                return CompatibilityCall("FloatMath", floatMethod, node.Left, node.Right).WithTriviaFrom(node);
            var method = (operation.IsChecked, operation.OperatorKind, type.SpecialType) switch
            {
                (true, BinaryOperatorKind.Add, SpecialType.System_Int32) => "AddInt32",
                (true, BinaryOperatorKind.Subtract, SpecialType.System_Int32) => "SubtractInt32",
                (true, BinaryOperatorKind.Multiply, SpecialType.System_Int32) => "MultiplyInt32",
                (_, BinaryOperatorKind.Divide, SpecialType.System_Int32) => "DivideInt32",
                (true, BinaryOperatorKind.Add, SpecialType.System_UInt32) => "AddUInt32",
                (true, BinaryOperatorKind.Subtract, SpecialType.System_UInt32) => "SubtractUInt32",
                (true, BinaryOperatorKind.Multiply, SpecialType.System_UInt32) => "MultiplyUInt32",
                (true, BinaryOperatorKind.Add, SpecialType.System_Int64) => "AddInt64",
                (true, BinaryOperatorKind.Subtract, SpecialType.System_Int64) => "SubtractInt64",
                (true, BinaryOperatorKind.Multiply, SpecialType.System_Int64) => "MultiplyInt64",
                (true, BinaryOperatorKind.Add, SpecialType.System_UInt64) => "AddUInt64",
                (true, BinaryOperatorKind.Subtract, SpecialType.System_UInt64) => "SubtractUInt64",
                (true, BinaryOperatorKind.Multiply, SpecialType.System_UInt64) => "MultiplyUInt64",
                _ => null
            };
            if (method is not null)
                return CompatibilityCall("CheckedArithmetic", method, node.Left, node.Right).WithTriviaFrom(node);
        }
        return base.VisitBinaryExpression(node);
    }

    public override SyntaxNode? VisitCastExpression(CastExpressionSyntax node)
    {
        if (model.GetOperation(node) is IConversionOperation { IsChecked: true, Type: { } target, Operand.Type: { } source })
        {
            var method = target.SpecialType switch
            {
                SpecialType.System_Byte => "ToByte",
                SpecialType.System_SByte => "ToSByte",
                SpecialType.System_Int16 => "ToInt16",
                SpecialType.System_UInt16 => "ToUInt16",
                SpecialType.System_Int32 => "ToInt32",
                SpecialType.System_UInt32 => "ToUInt32",
                _ => null
            };
            if (method is not null && source.SpecialType is SpecialType.System_SByte or SpecialType.System_Byte or
                SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or
                SpecialType.System_Int64 or SpecialType.System_Single)
                return CompatibilityCall("CheckedArithmetic", method, node.Expression).WithTriviaFrom(node);
        }
        return base.VisitCastExpression(node);
    }

    private ExpressionSyntax CompatibilityCall(string type, string method, params ExpressionSyntax[] arguments) =>
        SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System." + type + "." + method),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(argument =>
                SyntaxFactory.Argument((ExpressionSyntax)Visit(argument)!)))));

    public override SyntaxNode? Visit(SyntaxNode? node)
    {
        var result = base.Visit(node);
        if (node is ExpressionSyntax expression && model.GetOperation(expression) is not null &&
            model.GetTypeInfo(expression) is { Type: { } type, ConvertedType: { } converted } &&
            IsByteSpan(converted) && !IsStreamReadExactlyArgument(expression))
        {
            var method = IsByteArray(type) ? converted.Name == "Span" ? "AsSpan" : "AsReadOnly" :
                IsByteSpan(type) && type.Name == "Span" && converted.Name == "ReadOnlySpan" ? "AsReadOnly" : null;
            if (method is not null)
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions." + method),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument((ExpressionSyntax)result!))));
        }
        return result;
    }

    public override SyntaxNode? VisitUsingDirective(UsingDirectiveSyntax node) =>
        unusedImports.Contains(node.Span) || node.Name?.ToString() is
            "System.IO.Compression" or
            "System.Security.Cryptography" or
            "System.Text.Json" or
            "System.Text.Json.Nodes" or
            "System.Text.Json.Serialization"
            ? null
            : node.WithGlobalKeyword(default);

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node)
    {
        if (node.Expression is ConditionalAccessExpressionSyntax conditional &&
            conditional.Expression is IdentifierNameSyntax &&
            conditional.WhenNotNull is InvocationExpressionSyntax invocation &&
            model.GetOperation(invocation) is IInvocationOperation call && call.TargetMethod.Name == "CopyTo" &&
            IsByteArray(model.GetTypeInfo(conditional.Expression).Type))
        {
            var arguments = invocation.ArgumentList.Arguments.Select(argument => (ArgumentSyntax)Visit(argument)!).ToList();
            arguments.Insert(0, SyntaxFactory.Argument((ExpressionSyntax)Visit(conditional.Expression)!));
            var copy = SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.CopyTo"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments)));
            return SyntaxFactory.IfStatement(
                SyntaxFactory.BinaryExpression(SyntaxKind.NotEqualsExpression, (ExpressionSyntax)Visit(conditional.Expression)!,
                    SyntaxFactory.LiteralExpression(SyntaxKind.NullLiteralExpression)),
                SyntaxFactory.ExpressionStatement(copy)).WithTriviaFrom(node);
        }
        return base.VisitExpressionStatement(node);
    }

    public override SyntaxNode? VisitSizeOfExpression(SizeOfExpressionSyntax node) =>
        model.GetConstantValue(node) is { HasValue: true, Value: int size }
            ? SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(size)).WithTriviaFrom(node)
            : base.VisitSizeOfExpression(node);

    public override SyntaxNode? VisitPostfixUnaryExpression(PostfixUnaryExpressionSyntax node) =>
        node.IsKind(SyntaxKind.SuppressNullableWarningExpression)
            ? Visit(node.Operand)!.WithTriviaFrom(node)
            : base.VisitPostfixUnaryExpression(node);

    public override SyntaxNode? VisitLiteralExpression(LiteralExpressionSyntax node) =>
        node.IsKind(SyntaxKind.Utf8StringLiteralExpression)
            ? SyntaxFactory.ParseExpression("new byte[] { " + string.Join(", ", System.Text.Encoding.UTF8.GetBytes(node.Token.ValueText)) + " }").WithTriviaFrom(node)
            : node.IsKind(SyntaxKind.DefaultLiteralExpression) && IsByteSpan(model.GetTypeInfo(node).ConvertedType)
            ? Empty(model.GetTypeInfo(node).ConvertedType!).WithTriviaFrom(node)
            : base.VisitLiteralExpression(node);

    public override SyntaxNode? VisitDefaultExpression(DefaultExpressionSyntax node) =>
        IsByteSpan(model.GetTypeInfo(node).Type) ? Empty(model.GetTypeInfo(node).Type!).WithTriviaFrom(node) : base.VisitDefaultExpression(node);

    public override SyntaxNode? VisitStackAllocArrayCreationExpression(StackAllocArrayCreationExpressionSyntax node) =>
        SyntaxFactory.ArrayCreationExpression((ArrayTypeSyntax)base.Visit(node.Type)!, (InitializerExpressionSyntax?)Visit(node.Initializer))
            .NormalizeWhitespace().WithTriviaFrom(node);

    public override SyntaxNode? VisitArrayCreationExpression(ArrayCreationExpressionSyntax node)
    {
        if (node.Initializer is null && IsByteArray(model.GetTypeInfo(node).Type) &&
            node.Type.RankSpecifiers is [{ Sizes: [var length] }])
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.ByteArrays.New"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                    (ExpressionSyntax)Visit(length)!)))).WithTriviaFrom(node);
        return base.VisitArrayCreationExpression(node);
    }

    public override SyntaxNode? VisitVariableDeclaration(VariableDeclarationSyntax node)
    {
        var result = (VariableDeclarationSyntax)base.VisitVariableDeclaration(node)!;
        if (model.GetTypeInfo(node.Type).Type is INamedTypeSymbol { Name: "Span", TypeArguments: [var element] } &&
            node.Variables.Count > 0 && node.Variables.All(variable =>
                variable.Initializer?.Value is StackAllocArrayCreationExpressionSyntax or CollectionExpressionSyntax))
            return result.WithType(SyntaxFactory.ParseTypeName(element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "[]")
                .WithTriviaFrom(node.Type));
        return result;
    }

    public override SyntaxNode? VisitForEachStatement(ForEachStatementSyntax node)
    {
        if (IsByteSpan(model.GetTypeInfo(node.Expression).Type))
            return node.WithExpression(SyntaxFactory.InvocationExpression(
                SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.CopyToArray"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                    (ExpressionSyntax)Visit(node.Expression)!)))))
                .WithStatement((StatementSyntax)Visit(node.Statement)!);
        return base.VisitForEachStatement(node);
    }

    public override SyntaxNode? VisitInterpolation(InterpolationSyntax node)
    {
        var result = (InterpolationSyntax)base.VisitInterpolation(node)!;
        return result.Expression.ToString().Contains("global::", StringComparison.Ordinal)
            ? result.WithExpression(SyntaxFactory.ParenthesizedExpression(result.Expression.WithoutTrivia()).WithTriviaFrom(result.Expression))
            : result;
    }

    public override SyntaxNode? VisitInvocationExpression(InvocationExpressionSyntax node)
    {
        var operation = model.GetOperation(node) as IInvocationOperation;
        if (operation is { } call)
        {
            var method = call.TargetMethod;
            if (method.ContainingType.ToDisplayString() == "System.Buffers.Binary.BinaryPrimitives" &&
                method.Name is "ReadInt16LittleEndian" or "ReadUInt16LittleEndian" or
                    "ReadInt32LittleEndian" or "ReadUInt32LittleEndian" &&
                node.ArgumentList.Arguments is [var binaryArgument] &&
                binaryArgument.Expression is InvocationExpressionSyntax asSpan &&
                asSpan.Expression is MemberAccessExpressionSyntax
                {
                    Expression: var binarySource,
                    Name.Identifier.ValueText: "AsSpan"
                } &&
                IsByteArray(model.GetTypeInfo(binarySource).Type))
            {
                var binaryStart = asSpan.ArgumentList.Arguments.FirstOrDefault() is { } startArgument
                    ? startArgument.Expression
                    : SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(0));
                return CompatibilityCall("BitConverterCompatibility", "To" + method.Name[4..^12], binarySource, binaryStart)
                    .WithTriviaFrom(node);
            }
            if (method.ContainingType.ToDisplayString() == "System.Text.Json.JsonSerializer" &&
                method.Name == "Deserialize" && method.IsGenericMethod &&
                method.TypeArguments is [INamedTypeSymbol { Name: "Dictionary", TypeArguments: [var key, var value] }] &&
                key.SpecialType == SpecialType.System_String && value.SpecialType == SpecialType.System_Object &&
                node.ArgumentList.Arguments is [var argument] && argument.Expression is InvocationExpressionSyntax serialized &&
                model.GetOperation(serialized) is IInvocationOperation { TargetMethod.Name: "Serialize" } &&
                serialized.ArgumentList.Arguments is [var source])
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.JsonSerializer.CloneDictionary"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                        SyntaxFactory.Argument((ExpressionSyntax)Visit(source.Expression)!)))).WithTriviaFrom(node);
            if (method.ContainingType.ToDisplayString() == "System.MathF" && method.ReturnType.SpecialType == SpecialType.System_Single)
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.FloatMath.Round"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        (ExpressionSyntax)base.VisitInvocationExpression(node)!)))).WithTriviaFrom(node);
            var extension = method.ContainingType.ToDisplayString() == "System.MemoryExtensions";
            var receiver = call.Instance ?? (extension ? call.Arguments.FirstOrDefault(a => a.Parameter?.Ordinal == 0)?.Value : null);
            if (receiver is not null && IsByteBuffer(receiver.Type) &&
                (extension && method.Name is "AsSpan" or "SequenceEqual" or "CopyTo" or "TryCopyTo" or "IndexOf" ||
                 IsByteSpan(receiver.Type) && method.Name is "Slice" or "ToArray" or "CopyTo" or "TryCopyTo" or "Clear" or "Fill" or "IndexOf"))
            {
                // Keep source argument order (including named arguments) and evaluate receivers once.
                var arguments = node.ArgumentList.Arguments.Select(a => (ArgumentSyntax)Visit(a)!).ToList();
                if (node.Expression is MemberAccessExpressionSyntax member &&
                    (call.Instance is not null || method.IsExtensionMethod && model.GetSymbolInfo(member.Expression).Symbol is not INamedTypeSymbol))
                    arguments.Insert(0, SyntaxFactory.Argument((ExpressionSyntax)Visit(member.Expression)!));
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(
                    "global::System.ByteSpanExtensions." + (method.Name == "ToArray" ? "CopyToArray" : method.Name)),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))).WithTriviaFrom(node);
            }

            if (receiver is not null && IsSpan(receiver.Type, SpecialType.System_Int32) && method.Name == "Clear")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.IntSpanExtensions.Clear"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument((ExpressionSyntax)Visit(((MemberAccessExpressionSyntax)node.Expression).Expression)!)))).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Array" && method.Name == "Fill")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ArrayCompatibility.Fill<" +
                    method.TypeArguments.Single().ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.GC" && method.Name == "AllocateUninitializedArray" &&
                method.TypeArguments is [var arrayElement])
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ArrayCompatibility.NewUninitialized<" +
                    arrayElement.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.MemoryExtensions" && method.Name == "Sort" &&
                receiver is not null && IsSpan(receiver.Type, SpecialType.System_UInt32))
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ArrayCompatibility.Sort"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        (ExpressionSyntax)Visit(((MemberAccessExpressionSyntax)node.Expression).Expression)!)))).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Buffer" && method.Name == "BlockCopy")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.BufferCompatibility.BlockCopy"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.IO.Path" && method.Name is "Combine" or "GetFileName" or "GetFileNameWithoutExtension" or "GetExtension" or "ChangeExtension")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.PathCompatibility." + method.Name),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.SpecialType == SpecialType.System_String && method.Name == "Contains" && method.Parameters.Length == 2)
                return CompatibilityCall("StringCompatibility", "Contains", ((MemberAccessExpressionSyntax)node.Expression).Expression,
                    node.ArgumentList.Arguments[0].Expression, node.ArgumentList.Arguments[1].Expression).WithTriviaFrom(node);

            if (method.ContainingType.SpecialType == SpecialType.System_String && method.Name == "Replace" && method.Parameters.Length == 3)
                return CompatibilityCall("StringCompatibility", "Replace", ((MemberAccessExpressionSyntax)node.Expression).Expression,
                    node.ArgumentList.Arguments[0].Expression, node.ArgumentList.Arguments[1].Expression,
                    node.ArgumentList.Arguments[2].Expression).WithTriviaFrom(node);

            if (method.ContainingType.SpecialType == SpecialType.System_String && method.Name == "IndexOf" && method.Parameters.Length == 2 &&
                method.Parameters[0].Type.SpecialType == SpecialType.System_Char)
                return CompatibilityCall("StringCompatibility", "IndexOf", ((MemberAccessExpressionSyntax)node.Expression).Expression,
                    node.ArgumentList.Arguments[0].Expression, node.ArgumentList.Arguments[1].Expression).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Diagnostics.Stopwatch" && method.Name == "GetElapsedTime")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.Diagnostics.StopwatchCompatibility.GetElapsedTime"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Security.Cryptography.SHA256" && method.Name == "HashData")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.BrowserHash.Sha256"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Convert" && method.Name == "ToBase64String" &&
                node.ArgumentList.Arguments is [var base64] && IsByteSpan(model.GetTypeInfo(base64.Expression).Type))
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.Convert.ToBase64String"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.CopyToArray"),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                                (ExpressionSyntax)Visit(base64.Expression)!)))))))).WithTriviaFrom(node);

            if (method.ContainingType.ToDisplayString() == "System.Text.Encoding" && method.Name == "GetString" &&
                node.ArgumentList.Arguments is [var encoded] && IsByteSpan(model.GetTypeInfo(encoded.Expression).Type))
                return SyntaxFactory.InvocationExpression(
                    SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression,
                        (ExpressionSyntax)Visit(((MemberAccessExpressionSyntax)node.Expression).Expression)!,
                        SyntaxFactory.IdentifierName("GetString")),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                        SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.CopyToArray"),
                            SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                                (ExpressionSyntax)Visit(encoded.Expression)!)))))))).WithTriviaFrom(node);

            if (method.ContainingType.SpecialType == SpecialType.System_Int32 && method.Name == "TryParse" &&
                node.ArgumentList.Arguments is [var text, _, _, var output] && text.Expression is InvocationExpressionSyntax slice &&
                model.GetOperation(slice) is IInvocationOperation { TargetMethod.Name: "AsSpan" } &&
                slice.Expression is MemberAccessExpressionSyntax spanMember && slice.ArgumentList.Arguments is [var start])
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.IntCompatibility.TryParse"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument((ExpressionSyntax)Visit(spanMember.Expression)!),
                        SyntaxFactory.Argument((ExpressionSyntax)Visit(start.Expression)!),
                        (ArgumentSyntax)Visit(output)!
                    }))).WithTriviaFrom(node);

            if (method.ContainingType.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.Queue<T>" && method.Name == "TryDequeue")
            {
                var arguments = node.ArgumentList.Arguments.Select(argument => (ArgumentSyntax)Visit(argument)!).ToList();
                arguments.Insert(0, SyntaxFactory.Argument((ExpressionSyntax)Visit(((MemberAccessExpressionSyntax)node.Expression).Expression)!));
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.Collections.Generic.QueueCompatibility.TryDequeue<" +
                    method.ContainingType.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments))).WithTriviaFrom(node);
            }

            if (method.ContainingType.SpecialType == SpecialType.System_Char && method.Name == "ToLowerInvariant")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.CharCompatibility.ToLowerInvariant"),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);

            if (method.Name == "ReadExactly" && node.ArgumentList.Arguments is [var readBuffer] &&
                IsByteSpan(model.GetTypeInfo(readBuffer.Expression).Type))
            {
                var stream = call.Instance is not null
                    ? ((MemberAccessExpressionSyntax)node.Expression).Expression
                    : ((ArgumentSyntax)call.Arguments.First(argument => argument.Parameter?.Ordinal == 0).Syntax).Expression;
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.ReadExactly"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                    {
                        SyntaxFactory.Argument((ExpressionSyntax)Visit(stream)!),
                        SyntaxFactory.Argument((ExpressionSyntax)Visit(readBuffer.Expression)!)
                    }))).WithTriviaFrom(node);
            }

            if (method.ContainingType.ToDisplayString() == "System.BitConverter" &&
                (method.Name is "ToInt16" or "ToUInt16" or "ToInt32" or "ToUInt32" or "ToSingle" or
                    "Int32BitsToSingle" or "SingleToInt32Bits" ||
                 method.Name == "GetBytes" && method.Parameters[0].Type.SpecialType is SpecialType.System_Int16 or
                     SpecialType.System_UInt16 or SpecialType.System_Int32 or SpecialType.System_UInt32 or SpecialType.System_Single))
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.BitConverterCompatibility." + method.Name),
                    (ArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);
        }

        if (operation is not null &&
            operation.TargetMethod.ContainingType.ToDisplayString() == "System.ArgumentNullException" && operation.TargetMethod.Name == "ThrowIfNull")
        {
            var value = operation.Arguments.Single(a => a.Parameter!.Ordinal == 0);
            var name = operation.Arguments.Single(a => a.Parameter!.Ordinal == 1);
            var parameterName = name.IsImplicit
                ? SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression, SyntaxFactory.Literal((string?)name.Value.ConstantValue.Value ?? ""))
                : (ExpressionSyntax)Visit(((ArgumentSyntax)name.Syntax).Expression)!;
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.Guards.ThrowIfNull"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                {
                    SyntaxFactory.Argument((ExpressionSyntax)Visit(((ArgumentSyntax)value.Syntax).Expression)!),
                    SyntaxFactory.Argument(parameterName)
                }))).WithTriviaFrom(node);
        }
        if (operation is not null &&
            operation.TargetMethod.ContainingType.ToDisplayString() == "System.ArgumentException" &&
            operation.TargetMethod.Name == "ThrowIfNullOrWhiteSpace")
        {
            var value = operation.Arguments.Single(a => a.Parameter!.Ordinal == 0);
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::RatchetPs2.JavaScript.Guards.ThrowIfNullOrWhiteSpace"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                    (ExpressionSyntax)Visit(((ArgumentSyntax)value.Syntax).Expression)!)))).WithTriviaFrom(node);
        }
        return base.VisitInvocationExpression(node);
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
    {
        if (model.GetOperation(node) is IObjectCreationOperation { Constructor.ContainingType: INamedTypeSymbol type } &&
            type.ConstructedFrom.ToDisplayString() == "System.Collections.Generic.Dictionary<TKey, TValue>" &&
            node.ArgumentList?.Arguments is [var source] &&
            model.GetTypeInfo(source.Expression).Type?.OriginalDefinition.ToDisplayString() ==
                "System.Collections.Generic.IReadOnlyDictionary<TKey, TValue>")
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(
                    "global::System.Collections.Generic.DictionaryCompatibility.Copy<" +
                    string.Join(", ", type.TypeArguments.Select(argument => argument.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ">"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(
                    (ExpressionSyntax)Visit(source.Expression)!)))).WithTriviaFrom(node);
        return base.VisitObjectCreationExpression(node);
    }

    public override SyntaxNode? VisitElementAccessExpression(ElementAccessExpressionSyntax node)
    {
        var rewritten = RewriteElementAccess(node);
        // Transpose's untyped interface accessor boxes array elements. Generic
        // IReadOnlyList<T> reads must retain T for enum patterns and strict equality.
        if (model.GetTypeInfo(node.Expression).Type is INamedTypeSymbol type &&
            type.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IReadOnlyList<T>" &&
            type.TypeArguments[0].IsValueType)
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(
                    "global::System.Collections.Generic.ReadOnlyListCompatibility.Value<" +
                    type.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ">"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(
                    SyntaxFactory.Argument((ExpressionSyntax)rewritten!)))).WithTriviaFrom(node);
        return rewritten;
    }

    private SyntaxNode? RewriteElementAccess(ElementAccessExpressionSyntax node)
    {
        if (node.Parent is ArgumentSyntax { RefKindKeyword.RawKind: not 0 } &&
            IsByteSpan(model.GetTypeInfo(node.Expression).Type))
            return SyntaxFactory.ElementAccessExpression(
                SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.Array"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument((ExpressionSyntax)Visit(node.Expression)!)))),
                (BracketedArgumentListSyntax)Visit(node.ArgumentList)!).WithTriviaFrom(node);
        if (IsByteSpan(model.GetTypeInfo(node.Expression).Type) && node.ArgumentList.Arguments is [var argument] &&
            argument.Expression is RangeExpressionSyntax range)
        {
            var arguments = new List<ExpressionSyntax> { (ExpressionSyntax)Visit(node.Expression)! };
            foreach (var (bound, end) in new[] { (range.LeftOperand, false), (range.RightOperand, true) })
            {
                var fromEnd = bound is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.IndexExpression);
                var value = fromEnd ? ((PrefixUnaryExpressionSyntax)bound!).Operand : bound;
                arguments.Add(value is null ? SyntaxFactory.ParseExpression("0") : (ExpressionSyntax)Visit(value)!);
                arguments.Add(SyntaxFactory.ParseExpression(fromEnd || bound is null && end ? "true" : "false"));
            }
            return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.Range"),
                SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(SyntaxFactory.Argument)))).WithTriviaFrom(node);
        }
        if (IsByteSpan(model.GetTypeInfo(node.Expression).Type) && node.ArgumentList.Arguments is [var rangeArgument] &&
            model.GetTypeInfo(rangeArgument.Expression).Type?.ToDisplayString() == "System.Range")
            return BufferCall("Range", node, rangeArgument.Expression);
        if (IsByteSpan(model.GetTypeInfo(node.Expression).Type) && node.ArgumentList.Arguments is [var index])
            return BufferElementCall("Get", node, index.Expression);
        if (node.ArgumentList.Arguments is [var fromEndArgument] &&
            fromEndArgument.Expression is PrefixUnaryExpressionSyntax fromEndIndex &&
            fromEndIndex.IsKind(SyntaxKind.IndexExpression))
        {
            var expression = (ExpressionSyntax)Visit(node.Expression)!;
            var count = model.GetTypeInfo(node.Expression).Type is IArrayTypeSymbol ? "Length" : "Count";
            var offset = SyntaxFactory.BinaryExpression(SyntaxKind.SubtractExpression,
                SyntaxFactory.MemberAccessExpression(SyntaxKind.SimpleMemberAccessExpression, expression, SyntaxFactory.IdentifierName(count)),
                (ExpressionSyntax)Visit(fromEndIndex.Operand)!);
            return SyntaxFactory.ElementAccessExpression(expression,
                SyntaxFactory.BracketedArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument(offset))))
                .WithTriviaFrom(node);
        }
        return base.VisitElementAccessExpression(node);
    }

    public override SyntaxNode? VisitAssignmentExpression(AssignmentExpressionSyntax node)
    {
        if (node.Left is ElementAccessExpressionSyntax intElement &&
            IsSpan(model.GetTypeInfo(intElement.Expression).Type, SpecialType.System_Int32) &&
            intElement.ArgumentList.Arguments is [var intIndex])
        {
            var right = (ExpressionSyntax)Visit(node.Right)!;
            ExpressionSyntax? value = node.IsKind(SyntaxKind.SimpleAssignmentExpression) ? right : node.Kind() switch
            {
                // This tree is serialized and reparsed: retain the RHS grouping of += / -=.
                SyntaxKind.AddAssignmentExpression => SyntaxFactory.BinaryExpression(
                    SyntaxKind.AddExpression, (ExpressionSyntax)Visit(intElement)!, SyntaxFactory.ParenthesizedExpression(right)),
                SyntaxKind.SubtractAssignmentExpression => SyntaxFactory.BinaryExpression(
                    SyntaxKind.SubtractExpression, (ExpressionSyntax)Visit(intElement)!, SyntaxFactory.ParenthesizedExpression(right)),
                _ => null
            };
            if (value is not null)
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.IntSpanExtensions.Set"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[]
                    {
                        (ExpressionSyntax)Visit(intElement.Expression)!,
                        (ExpressionSyntax)Visit(intIndex.Expression)!,
                        value
                    }.Select(SyntaxFactory.Argument))))
                    .WithTriviaFrom(node);
        }
        if (node.Left is IdentifierNameSyntax &&
            model.GetOperation(node) is ICompoundAssignmentOperation { Type.SpecialType: SpecialType.System_Boolean })
        {
            var method = node.Kind() switch
            {
                SyntaxKind.AndAssignmentExpression => "And",
                SyntaxKind.OrAssignmentExpression => "Or",
                SyntaxKind.ExclusiveOrAssignmentExpression => "Xor",
                _ => null
            };
            if (method is not null)
                return SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                    (ExpressionSyntax)Visit(node.Left)!, CompatibilityCall("BooleanCompatibility", method, node.Left, node.Right))
                    .WithTriviaFrom(node);
        }
        if (model.GetOperation(node) is ICompoundAssignmentOperation { OperatorMethod.ContainingNamespace: { } numericNamespace } &&
            numericNamespace.ToDisplayString() == "System.Numerics")
        {
            var binaryKind = node.Kind() switch
            {
                SyntaxKind.AddAssignmentExpression => SyntaxKind.AddExpression,
                SyntaxKind.SubtractAssignmentExpression => SyntaxKind.SubtractExpression,
                SyntaxKind.MultiplyAssignmentExpression => SyntaxKind.MultiplyExpression,
                SyntaxKind.DivideAssignmentExpression => SyntaxKind.DivideExpression,
                _ => SyntaxKind.None
            };
            if (binaryKind != SyntaxKind.None)
            {
                var left = (ExpressionSyntax)Visit(node.Left)!;
                return SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression, left,
                    SyntaxFactory.BinaryExpression(binaryKind, left,
                        SyntaxFactory.ParenthesizedExpression((ExpressionSyntax)Visit(node.Right)!))).WithTriviaFrom(node);
            }
        }
        if (node.Left is ElementAccessExpressionSyntax element && IsByteSpan(model.GetTypeInfo(element.Expression).Type) &&
            element.ArgumentList.Arguments is [var index])
        {
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
                return BufferElementCall("Set", element, index.Expression, node.Right).WithTriviaFrom(node);
            var operation = node.Kind() switch
            {
                SyntaxKind.AddAssignmentExpression => 0,
                SyntaxKind.SubtractAssignmentExpression => 1,
                SyntaxKind.MultiplyAssignmentExpression => 2,
                SyntaxKind.DivideAssignmentExpression => 3,
                SyntaxKind.ModuloAssignmentExpression => 4,
                SyntaxKind.AndAssignmentExpression => 5,
                SyntaxKind.OrAssignmentExpression => 6,
                SyntaxKind.ExclusiveOrAssignmentExpression => 7,
                SyntaxKind.LeftShiftAssignmentExpression => 8,
                SyntaxKind.RightShiftAssignmentExpression => 9,
                _ => -1
            };
            if (operation >= 0) return BufferElementCall("Compound", element, index.Expression, node.Right, operation).WithTriviaFrom(node);
        }
        return base.VisitAssignmentExpression(node);
    }

    private ExpressionSyntax BufferElementCall(string method, ElementAccessExpressionSyntax element, ExpressionSyntax index,
        ExpressionSyntax? value = null, int? operation = null)
    {
        var fromEnd = index is PrefixUnaryExpressionSyntax prefix && prefix.IsKind(SyntaxKind.IndexExpression);
        var indexType = model.GetTypeInfo(index).Type?.ToDisplayString();
        var arguments = indexType == "System.Index" && !fromEnd
            ? new List<ExpressionSyntax> { (ExpressionSyntax)Visit(element.Expression)!, (ExpressionSyntax)Visit(index)! }
            : new List<ExpressionSyntax> { (ExpressionSyntax)Visit(element.Expression)!,
                (ExpressionSyntax)Visit(fromEnd ? ((PrefixUnaryExpressionSyntax)index).Operand : index)!,
                SyntaxFactory.ParseExpression(fromEnd ? "true" : "false") };
        if (value is not null) arguments.Add((ExpressionSyntax)Visit(value)!);
        if (operation is not null) arguments.Add(SyntaxFactory.LiteralExpression(SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(operation.Value)));
        return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions." + method),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(arguments.Select(SyntaxFactory.Argument)))).WithTriviaFrom(element);
    }

    private ExpressionSyntax BufferCall(string method, ElementAccessExpressionSyntax element, ExpressionSyntax value) =>
        SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions." + method),
            SyntaxFactory.ArgumentList(SyntaxFactory.SeparatedList(new[] { element.Expression, value }.Select(expression =>
                SyntaxFactory.Argument((ExpressionSyntax)Visit(expression)!))))).WithTriviaFrom(element);

    private static ExpressionSyntax Empty(ITypeSymbol type) => SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression(
        "global::System.ByteSpanExtensions." + (type.Name == "ReadOnlySpan" ? "EmptyReadOnly" : "Empty")));

    public override SyntaxNode? VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
    {
        if (model.GetSymbolInfo(node).Symbol is IPropertySymbol property && IsByteSpan(property.ContainingType))
        {
            if (property.IsStatic && property.Name == "Empty") return Empty(property.ContainingType).WithTriviaFrom(node);
            if (!property.IsStatic && property.Name == "IsEmpty")
                return SyntaxFactory.InvocationExpression(SyntaxFactory.ParseExpression("global::System.ByteSpanExtensions.IsEmpty"),
                    SyntaxFactory.ArgumentList(SyntaxFactory.SingletonSeparatedList(SyntaxFactory.Argument((ExpressionSyntax)Visit(node.Expression)!)))).WithTriviaFrom(node);
        }
        return base.VisitMemberAccessExpression(node);
    }

    private static bool IsSpan(ITypeSymbol? type, SpecialType element) => type is INamedTypeSymbol named &&
        named.ContainingNamespace.ToDisplayString() == "System" && named.Name is "Span" or "ReadOnlySpan" &&
        named.TypeArguments is [{ SpecialType: var special }] && special == element;

    private static bool IsByteSpan(ITypeSymbol? type) => IsSpan(type, SpecialType.System_Byte);

    private static bool IsByteArray(ITypeSymbol? type) => type is IArrayTypeSymbol { Rank: 1, ElementType.SpecialType: SpecialType.System_Byte };

    private static bool IsByteBuffer(ITypeSymbol? type) => IsByteSpan(type) || IsByteArray(type);

    private bool IsStreamReadExactlyArgument(ExpressionSyntax expression) =>
        expression.Parent is ArgumentSyntax { Parent.Parent: InvocationExpressionSyntax invocation } &&
        model.GetOperation(invocation) is IInvocationOperation call &&
        call.TargetMethod.ContainingType.ToDisplayString() == "System.IO.Stream" && call.TargetMethod.Name == "ReadExactly";

    private static string? BrowserIoType(ISymbol? symbol)
    {
        if (symbol is IMethodSymbol { MethodKind: MethodKind.Constructor } constructor) symbol = constructor.ContainingType;
        return symbol is INamedTypeSymbol type ? type.ToDisplayString() switch
        {
            "System.IO.BinaryReader" => "global::RatchetPs2.JavaScript.BinaryReader",
            "System.IO.BinaryWriter" => "global::RatchetPs2.JavaScript.BinaryWriter",
            "System.IO.Compression.CompressionMode" => "global::RatchetPs2.JavaScript.CompressionMode",
            "System.IO.Compression.ZLibStream" => "global::RatchetPs2.JavaScript.ZLibStream",
            "System.IO.Compression.ZipArchive" => "global::RatchetPs2.JavaScript.ZipArchive",
            "System.IO.Compression.ZipArchiveEntry" => "global::RatchetPs2.JavaScript.ZipArchiveEntry",
            "System.IO.Compression.ZipArchiveMode" => "global::RatchetPs2.JavaScript.ZipArchiveMode",
            "System.Text.Json.JsonDocument" => "global::RatchetPs2.JavaScript.JsonDocument",
            "System.Text.Json.JsonElement" => "global::RatchetPs2.JavaScript.JsonElement",
            "System.Text.Json.JsonProperty" => "global::RatchetPs2.JavaScript.JsonProperty",
            "System.Text.Json.JsonValueKind" => "global::RatchetPs2.JavaScript.JsonValueKind",
            "System.Text.Json.JsonSerializer" => "global::RatchetPs2.JavaScript.JsonSerializer",
            "System.Text.Json.JsonSerializerOptions" => "global::RatchetPs2.JavaScript.JsonSerializerOptions",
            "System.Text.Json.Nodes.JsonArray" => "global::RatchetPs2.JavaScript.JsonArray",
            "System.Text.Json.Nodes.JsonNode" => "global::RatchetPs2.JavaScript.JsonNode",
            "System.Text.Json.Nodes.JsonObject" => "global::RatchetPs2.JavaScript.JsonObject",
            "System.Text.Json.Serialization.JsonIgnoreAttribute" => "global::RatchetPs2.JavaScript.JsonIgnoreAttribute",
            "System.Text.Json.Serialization.JsonStringEnumConverter" => "global::RatchetPs2.JavaScript.JsonStringEnumConverter",
            _ => null
        } : null;
    }
}
