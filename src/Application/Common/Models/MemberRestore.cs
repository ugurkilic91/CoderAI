namespace DevAgents.Application.Common.Models;

/// <summary>Derleme hatasında aranan bir üye: tip adı (namespace'siz) ve üye adı.</summary>
public sealed record MissingMember(string TypeName, string MemberName);

/// <summary>Silinen üyeler geri eklendikten sonra dosyanın içeriği ve geri eklenenler ("Tip.Üye").</summary>
public sealed record MemberRestore(string Content, IReadOnlyList<string> RestoredMembers);
