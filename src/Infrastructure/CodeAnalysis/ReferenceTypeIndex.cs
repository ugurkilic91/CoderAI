using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace DevAgents.Infrastructure.CodeAnalysis;

/// <summary>
/// Bir projenin başvurduğu derlenmiş kütüphanelerdeki public tipler ve extension metotları: NuGet paketleri
/// (restore'un yazdığı project.assets.json'dan) ve .NET çerçeveleri (Microsoft.NETCore.App, Microsoft.AspNetCore.App).
/// Kütüphaneler yüklenmez ve çalıştırılmaz; System.Reflection.Metadata ile yalnızca okunur. Her kütüphanenin dizini
/// bir kez çıkarılır ve önbellekte tutulur (paketler sürümleriyle değişmez).
/// </summary>
internal sealed class ReferenceTypeIndex
{
    private const string ProjectAssetsFile = "project.assets.json";

    private static readonly ConcurrentDictionary<string, AssemblyTypes> Assemblies = new(StringComparer.Ordinal);

    private readonly IReadOnlyList<AssemblyTypes> _assemblies;

    private ReferenceTypeIndex(IReadOnlyList<AssemblyTypes> assemblies) => _assemblies = assemblies;

    public static ReferenceTypeIndex Empty { get; } = new([]);

    /// <summary>Restore'un yazdığı project.assets.json (bulunamazsa null; örn. restore hiç çalışmadıysa).</summary>
    public string? AssetsFile { get; private init; }

    public IEnumerable<string> NamespacesOf(TypeKey key) =>
        _assemblies.SelectMany(assembly => assembly.Types.GetValueOrDefault(key) ?? []).Distinct(StringComparer.Ordinal);

    public IEnumerable<string> ExtensionNamespacesOf(string method) =>
        _assemblies.SelectMany(assembly => assembly.ExtensionMethods.GetValueOrDefault(method) ?? []).Distinct(StringComparer.Ordinal);

    /// <summary>
    /// Projenin (.csproj) başvurduğu kütüphaneler. Docker sandbox'ta paket klasörü konteyner yoluyla yazılır
    /// (/home/sandbox/.nuget/packages); <paramref name="toLocalPath"/> onu bu makinedeki karşılığına çevirir.
    /// </summary>
    public static ReferenceTypeIndex ForProject(string projectFile, string projectRoot, Func<string, string> toLocalPath)
    {
        var assetsFile = FindAssetsFile(projectFile, projectRoot);
        if (assetsFile is null)
            return Empty;

        var paths = new List<string>();
        using (var document = JsonDocument.Parse(File.ReadAllBytes(assetsFile)))
        {
            var root = document.RootElement;
            paths.AddRange(PackageAssemblies(root, toLocalPath));
            paths.AddRange(FrameworkAssemblies(root));
        }

        var assemblies = paths.Distinct(StringComparer.Ordinal).Select(path => Assemblies.GetOrAdd(path, AssemblyTypes.Read)).ToList();
        return new ReferenceTypeIndex(assemblies) { AssetsFile = assetsFile };
    }

    // <proje>/obj/project.assets.json; ArtifactsPath kullanan çözümlerde (örn. Clean Architecture şablonu)
    // <üst klasörlerden biri>/artifacts/obj/<Proje>/project.assets.json.
    private static string? FindAssetsFile(string projectFile, string projectRoot)
    {
        var projectDirectory = Path.GetDirectoryName(projectFile)!;
        var candidate = Path.Combine(projectDirectory, "obj", ProjectAssetsFile);
        if (File.Exists(candidate))
            return candidate;

        var name = Path.GetFileNameWithoutExtension(projectFile);
        for (var directory = projectDirectory; directory is not null && SourceTypeIndex.IsUnder(directory, projectRoot); directory = Path.GetDirectoryName(directory))
        {
            candidate = Path.Combine(directory, "artifacts", "obj", name, ProjectAssetsFile);
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }

    // "targets" altındaki her paketin derleme zamanı kütüphaneleri ("compile"); "_._" yer tutucuları atlanır.
    private static IEnumerable<string> PackageAssemblies(JsonElement root, Func<string, string> toLocalPath)
    {
        if (!root.TryGetProperty("packageFolders", out var folders) || !root.TryGetProperty("libraries", out var libraries)
            || !root.TryGetProperty("targets", out var targets))
            yield break;

        var packageFolders = folders.EnumerateObject().Select(folder => toLocalPath(folder.Name)).ToList();
        foreach (var target in targets.EnumerateObject())
        {
            foreach (var library in target.Value.EnumerateObject())
            {
                if (!library.Value.TryGetProperty("type", out var type) || type.GetString() != "package"
                    || !library.Value.TryGetProperty("compile", out var compile)
                    || !libraries.TryGetProperty(library.Name, out var info) || !info.TryGetProperty("path", out var packagePath))
                    continue;

                foreach (var asset in compile.EnumerateObject())
                {
                    if (!asset.Name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    var path = packageFolders
                        .Select(folder => Path.Combine(folder, packagePath.GetString()!, asset.Name.Replace('/', Path.DirectorySeparatorChar)))
                        .FirstOrDefault(File.Exists);
                    if (path is not null)
                        yield return path;
                }
            }
        }
    }

    // Projenin çerçeveleri (frameworkReferences; Microsoft.NETCore.App her zaman). Kütüphaneleri, DevAgents'i çalıştıran
    // .NET kurulumundaki "shared" klasöründen okuyoruz: .../shared/Microsoft.AspNetCore.App/10.0.x/*.dll
    private static IEnumerable<string> FrameworkAssemblies(JsonElement root)
    {
        var frameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Microsoft.NETCore.App" };
        if (root.TryGetProperty("project", out var project) && project.TryGetProperty("frameworks", out var targetFrameworks))
        {
            foreach (var targetFramework in targetFrameworks.EnumerateObject())
            {
                if (targetFramework.Value.TryGetProperty("frameworkReferences", out var references))
                    frameworks.UnionWith(references.EnumerateObject().Select(reference => reference.Name));
            }
        }

        foreach (var framework in frameworks)
        {
            if (FrameworkDirectory(framework) is { } directory)
            {
                foreach (var path in Directory.EnumerateFiles(directory, "*.dll"))
                    yield return path;
            }
        }
    }

    private static string? FrameworkDirectory(string framework)
    {
        // Örn. /usr/lib/dotnet/shared/Microsoft.NETCore.App/10.0.12/ → /usr/lib/dotnet/shared/<framework>/<sürüm>
        var runtime = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var shared = runtime.Parent?.Parent;
        var directory = shared is null ? null : new DirectoryInfo(Path.Combine(shared.FullName, framework));
        if (directory is null || !directory.Exists)
            return null;

        // Çalışan sürümle aynısı varsa o, yoksa en yeni sürüm.
        var versions = directory.GetDirectories();
        var match = versions.FirstOrDefault(version => version.Name == runtime.Name)
                    ?? versions.Where(version => Version.TryParse(version.Name.Split('-')[0], out _))
                        .MaxBy(version => Version.Parse(version.Name.Split('-')[0]));
        return match?.FullName;
    }

    /// <summary>Tek bir kütüphanenin public tipleri ve extension metotları (ad → namespace'ler).</summary>
    private sealed class AssemblyTypes
    {
        private const string ExtensionAttributeNamespace = "System.Runtime.CompilerServices";
        private const string ExtensionAttributeName = "ExtensionAttribute";

        public Dictionary<TypeKey, HashSet<string>> Types { get; } = [];

        public Dictionary<string, HashSet<string>> ExtensionMethods { get; } = new(StringComparer.Ordinal);

        public static AssemblyTypes Read(string path)
        {
            var result = new AssemblyTypes();
            try
            {
                using var stream = File.OpenRead(path);
                using var peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return result; // yerel (native) kütüphane

                var reader = peReader.GetMetadataReader();
                foreach (var handle in reader.TypeDefinitions)
                {
                    var type = reader.GetTypeDefinition(handle);
                    // Yalnızca public, iç içe olmayan, bir namespace'te tanımlı tipler using ile görünür olur.
                    if ((type.Attributes & TypeAttributes.VisibilityMask) != TypeAttributes.Public || !type.GetDeclaringType().IsNil)
                        continue;
                    var ns = reader.GetString(type.Namespace);
                    var name = reader.GetString(type.Name);
                    if (ns.Length == 0 || name.Contains('<', StringComparison.Ordinal))
                        continue;

                    result.Add(ns, name);
                    // Statik sınıf (abstract + sealed) ve [Extension] işaretli: extension metotlarını topluyoruz.
                    const TypeAttributes staticClass = TypeAttributes.Abstract | TypeAttributes.Sealed;
                    if ((type.Attributes & staticClass) == staticClass && HasExtensionAttribute(reader, type.GetCustomAttributes()))
                        result.AddExtensionMethods(reader, type, ns);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or InvalidOperationException)
            {
                // Okunamayan kütüphane dizine girmez; tip bulunamazsa hatayı düzeltme ajanı çözer.
            }
            return result;
        }

        // Metadata'da generic tip adları "Ok`1" biçimindedir.
        private void Add(string ns, string metadataName)
        {
            var tick = metadataName.IndexOf('`', StringComparison.Ordinal);
            var key = tick < 0
                ? new TypeKey(metadataName, 0)
                : new TypeKey(metadataName[..tick], int.TryParse(metadataName[(tick + 1)..], out var arity) ? arity : 0);
            if (!Types.TryGetValue(key, out var namespaces))
                Types[key] = namespaces = new HashSet<string>(StringComparer.Ordinal);
            namespaces.Add(ns);
        }

        private void AddExtensionMethods(MetadataReader reader, TypeDefinition type, string ns)
        {
            foreach (var handle in type.GetMethods())
            {
                var method = reader.GetMethodDefinition(handle);
                if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                    || (method.Attributes & MethodAttributes.Static) == 0
                    || !HasExtensionAttribute(reader, method.GetCustomAttributes()))
                    continue;
                var name = reader.GetString(method.Name);
                if (!ExtensionMethods.TryGetValue(name, out var namespaces))
                    ExtensionMethods[name] = namespaces = new HashSet<string>(StringComparer.Ordinal);
                namespaces.Add(ns);
            }
        }

        private static bool HasExtensionAttribute(MetadataReader reader, CustomAttributeHandleCollection attributes)
        {
            foreach (var handle in attributes)
            {
                var constructor = reader.GetCustomAttribute(handle).Constructor;
                StringHandle name, ns;
                if (constructor.Kind == HandleKind.MemberReference)
                {
                    var parent = reader.GetMemberReference((MemberReferenceHandle)constructor).Parent;
                    if (parent.Kind != HandleKind.TypeReference)
                        continue;
                    var typeReference = reader.GetTypeReference((TypeReferenceHandle)parent);
                    (name, ns) = (typeReference.Name, typeReference.Namespace);
                }
                else if (constructor.Kind == HandleKind.MethodDefinition)
                {
                    // Özniteliği kendisi tanımlayan kütüphane (System.Private.CoreLib).
                    var declaringType = reader.GetTypeDefinition(reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
                    (name, ns) = (declaringType.Name, declaringType.Namespace);
                }
                else
                {
                    continue;
                }

                if (reader.StringComparer.Equals(name, ExtensionAttributeName) && reader.StringComparer.Equals(ns, ExtensionAttributeNamespace))
                    return true;
            }
            return false;
        }
    }
}
