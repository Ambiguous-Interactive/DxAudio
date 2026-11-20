namespace WallstopStudios.DxAudio.Tooling
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using Microsoft.CodeAnalysis;
    using Microsoft.CodeAnalysis.CSharp;
    using Microsoft.CodeAnalysis.CSharp.Syntax;

    internal static class ContractSnapshot
    {
        public static int Print(string root)
        {
            List<string> rows = new();
            foreach (string directory in new[] { "Runtime", "Editor", "Tests", "Samples~" })
            {
                foreach (string path in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories))
                {
                    string relative = Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
                    if (Path.GetFileName(path) is "csc.rsp.meta" or "WarningsAsErrors.ruleset.meta")
                    {
                        continue;
                    }

                    if (path.EndsWith(".meta", StringComparison.Ordinal))
                    {
                        File.ReadLines(path).Where(line => line.StartsWith("guid: ", StringComparison.Ordinal)).ToList().ForEach(line => rows.Add($"{relative} {line}"));
                    }
                    else if (path.EndsWith(".cs", StringComparison.Ordinal) && (directory == "Runtime" || directory == "Editor"))
                    {
                        foreach (bool modern in new[] { false, true })
                        {
                            AddSource(rows, relative, File.ReadAllText(path), modern);
                        }
                    }
                }
            }

            foreach (string row in rows.OrderBy(row => row, StringComparer.Ordinal))
            {
                Console.WriteLine(row);
            }

            return 0;
        }

        private static void AddSource(List<string> rows, string path, string source, bool modern)
        {
            string[] symbols = modern ? new[] { "UNITY_EDITOR", "UNITY_5_3_OR_NEWER", "UNITY_2022_3_OR_NEWER" } : new[] { "UNITY_EDITOR", "UNITY_5_3_OR_NEWER" };
            SyntaxNode root = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols)).GetRoot();

            foreach (BaseTypeDeclarationSyntax type in root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                string name = string.Join(".", type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().Reverse().Select(item => item.Identifier.ValueText));
                string scope = string.Join(".", type.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().Select(item => item.Name.ToString()));
                string prefix = $"{path} {(modern ? "modern" : "floor")} {scope}.{name}";
                if (type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().All(item => Public(item.Modifiers)))
                {
                    int headerLength = type.OpenBraceToken.SpanStart - type.SpanStart;
                    rows.Add($"{prefix} type {Clean(type.ToString().Substring(0, headerLength))}");
                    if (type is EnumDeclarationSyntax enumType)
                    {
                        rows.Add($"{prefix} enum {Clean(enumType.Members.ToString())}");
                    }
                }

                if (type is not TypeDeclarationSyntax body)
                {
                    continue;
                }

                bool visibleType = type.AncestorsAndSelf().OfType<BaseTypeDeclarationSyntax>().All(item => Public(item.Modifiers));
                List<string> serialized = new();
                foreach (MemberDeclarationSyntax member in body.Members)
                {
                    if (visibleType && (Public(member.Modifiers) || (body is InterfaceDeclarationSyntax && !member.Modifiers.Any())))
                    {
                        string signature = Signature(member);
                        if (signature != null)
                        {
                            rows.Add($"{prefix} api {Clean(signature)}");
                        }
                    }

                    if (
                        member is FieldDeclarationSyntax field
                        && !field.Modifiers.Any(SyntaxKind.StaticKeyword)
                        && !field.Modifiers.Any(SyntaxKind.ConstKeyword)
                        && !field.Modifiers.Any(SyntaxKind.ReadOnlyKeyword)
                        && !field.AttributeLists.ToString().Contains("NonSerialized", StringComparison.Ordinal)
                        && (field.Modifiers.Any(SyntaxKind.PublicKeyword) || IsSerialized(field))
                    )
                    {
                        serialized.Add(Clean(field.ToString()));
                    }
                    else if (member is PropertyDeclarationSyntax property && IsSerialized(property))
                    {
                        serialized.Add(Clean(property.ToString()));
                    }
                }

                if (0 < serialized.Count)
                {
                    rows.Add($"{prefix} serialized {string.Join(" | ", serialized)}");
                }
            }
        }

        private static bool Public(SyntaxTokenList modifiers)
        {
            return modifiers.Any(SyntaxKind.PublicKeyword) || modifiers.Any(SyntaxKind.ProtectedKeyword);
        }

        private static bool IsSerialized(MemberDeclarationSyntax member)
        {
            string attributes = member.AttributeLists.ToString();
            return attributes.Contains("SerializeField", StringComparison.Ordinal) || attributes.Contains("SerializeReference", StringComparison.Ordinal);
        }

        private static string Clean(string text)
        {
            return string.Join(" ", SyntaxFactory.ParseTokens(text).Select(token => token.Text));
        }

        private static string Signature(MemberDeclarationSyntax member)
        {
            return member switch
            {
                BaseMethodDeclarationSyntax method => method.WithBody(null).WithExpressionBody(null).WithSemicolonToken(default).ToString(),
                PropertyDeclarationSyntax property => $"{property.AttributeLists} {property.Modifiers} {property.Type} {property.Identifier} {Accessors(property.AccessorList)}",
                IndexerDeclarationSyntax indexer => $"{indexer.AttributeLists} {indexer.Modifiers} {indexer.Type} this{indexer.ParameterList} {Accessors(indexer.AccessorList)}",
                EventDeclarationSyntax declaredEvent => $"{declaredEvent.AttributeLists} {declaredEvent.Modifiers} {declaredEvent.Type} {declaredEvent.Identifier}",
                FieldDeclarationSyntax field when !field.Modifiers.Any(SyntaxKind.ConstKeyword) =>
                    $"{field.AttributeLists} {field.Modifiers} {field.Declaration.Type} {string.Join(",", field.Declaration.Variables.Select(variable => variable.Identifier.ValueText))}",
                BaseFieldDeclarationSyntax field => field.ToString(),
                DelegateDeclarationSyntax declaration => declaration.ToString(),
                _ => null,
            };
        }

        private static string Accessors(AccessorListSyntax accessors)
        {
            return accessors == null ? "get" : string.Join(";", accessors.Accessors.Select(accessor => $"{accessor.Modifiers} {accessor.Keyword}".Trim()));
        }
    }
}
