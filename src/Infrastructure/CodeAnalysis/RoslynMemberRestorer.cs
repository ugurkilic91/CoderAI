using DevAgents.Application.Common.Interfaces;
using DevAgents.Application.Common.Models;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DevAgents.Infrastructure.CodeAnalysis;

/// <summary>
/// Silinen üyeleri Roslyn ile geri ekler: dosyanın eski hâlinde tipte bulunan, yeni hâlinde bulunmayan ve derleme hatasının
/// aradığı üyeler (örn. IApplicationDbContext.TodoLists), tipin kapanış parantezinin önüne olduğu gibi (yorumlarıyla) eklenir.
/// Yalnızca hatanın gösterdiği üyelere dokunulur; ajanın bilerek değiştirdiği diğer üyeler olduğu gibi kalır.
/// </summary>
internal sealed class RoslynMemberRestorer : ICodeMemberRestorer
{
    public MemberRestore? Restore(string original, string current, IReadOnlyCollection<MissingMember> missing)
    {
        var originalRoot = CSharpSyntaxTree.ParseText(original, RoslynSyntaxChecker.ParseOptions).GetCompilationUnitRoot();
        var currentRoot = CSharpSyntaxTree.ParseText(current, RoslynSyntaxChecker.ParseOptions).GetCompilationUnitRoot();

        var newLine = current.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var insertions = new List<(int Position, string Text)>();
        var restored = new List<string>();
        foreach (var type in missing.GroupBy(member => member.TypeName, StringComparer.Ordinal))
        {
            var currentType = TypeNamed(currentRoot, type.Key);
            var originalType = TypeNamed(originalRoot, type.Key);
            if (currentType is null || originalType is null)
                continue;

            var text = new System.Text.StringBuilder();
            foreach (var memberName in type.Select(member => member.MemberName).Distinct(StringComparer.Ordinal))
            {
                // Üye hâlâ varsa sorun silinme değildir (örn. imza değişti): o hatayı düzeltme ajanı çözer.
                if (MembersNamed(currentType, memberName).Any())
                    continue;
                var lost = MembersNamed(originalType, memberName).ToList();
                if (lost.Count == 0)
                    continue;
                foreach (var member in lost)
                    text.Append(WithLineEnd(member.ToFullString(), newLine));
                restored.Add($"{type.Key}.{memberName}");
            }
            if (text.Length > 0)
                insertions.Add((currentType.CloseBraceToken.FullSpan.Start, text.ToString()));
        }

        if (insertions.Count == 0)
            return null;

        // Sondan başa ekliyoruz ki önceki konumlar kaymasın.
        var content = current;
        foreach (var (position, text) in insertions.OrderByDescending(insertion => insertion.Position))
            content = content.Insert(position, text);
        return new MemberRestore(content, restored);
    }

    private static TypeDeclarationSyntax? TypeNamed(CompilationUnitSyntax root, string name) =>
        root.DescendantNodes().OfType<TypeDeclarationSyntax>().FirstOrDefault(type => type.Identifier.ValueText == name);

    private static IEnumerable<MemberDeclarationSyntax> MembersNamed(TypeDeclarationSyntax type, string name) =>
        type.Members.Where(member => member switch
        {
            PropertyDeclarationSyntax property => property.Identifier.ValueText == name,
            MethodDeclarationSyntax method => method.Identifier.ValueText == name,
            EventDeclarationSyntax @event => @event.Identifier.ValueText == name,
            BaseFieldDeclarationSyntax field => field.Declaration.Variables.Any(variable => variable.Identifier.ValueText == name),
            _ => false
        });

    // Üyenin son satırı satır sonuyla bitmiyorsa (dosyanın son satırı gibi) ekliyoruz ki kapanış parantezi ayrı satırda kalsın.
    private static string WithLineEnd(string member, string newLine) => member.EndsWith('\n') ? member : member + newLine;
}
