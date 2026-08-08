// Metadata-only dump of ScriptHookVDotNet3.dll API surface.
// Never executes game code — safe to run outside the game.
using System.Reflection;

var dll = (args.Length > 0 && args[0] != "--list") ? args[0] : Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "lib", "ScriptHookVDotNet3.dll");
dll = Path.GetFullPath(dll);
var runtimeDir = System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
var coreAssemblies = Directory.GetFiles(runtimeDir, "*.dll").ToList();
// SHVDN references System.Windows.Forms (Keys) — add desktop ref pack
var packs = Path.Combine(System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "packs");
if (Directory.Exists(packs))
    foreach (var dir in Directory.GetDirectories(packs, "Microsoft.WindowsDesktop.App.Ref"))
        coreAssemblies.AddRange(Directory.GetFiles(dir, "*.dll", SearchOption.AllDirectories));
var resolver = new PathAssemblyResolver(coreAssemblies.Append(dll));
using var mlc = new MetadataLoadContext(resolver);
var asm = mlc.LoadFromAssemblyPath(dll);
Console.WriteLine($"== {asm.GetName().Name} {asm.GetName().Version} ==");

if (args.Contains("--list"))
{
    foreach (var t in asm.GetTypes().Where(t => t.IsPublic || t.IsNestedPublic).OrderBy(t => t.FullName))
        Console.WriteLine(t.FullName);
    return;
}

string[] wantTypes = args.Length > 1
    ? args.Skip(1).ToArray()
    : new[] {
        "GTA.Script", "GTA.KeyEventArgs", "GTA.Game", "GTA.GameplayCamera",
        "GTA.World", "GTA.RaycastResult", "GTA.Player", "GTA.Ped", "GTA.Vehicle",
        "GTA.Prop", "GTA.Entities.Entity", "GTA.ParticleEffectsAsset", "GTA.ParticleEffect",
        "GTA.UI.Notification", "GTA.UI.Text", "GTA.UI.Sprite", "GTA.UI.Rectangle",
        "GTA.UI.Screen", "GTA.Control", "GTA.Hash", "GTA.Math.Vector3"
    };

foreach (var typeName in wantTypes)
{
    var t = asm.GetTypes().FirstOrDefault(x => x.FullName == typeName);
    if (t == null) { Console.WriteLine($"\n### {typeName}: NOT FOUND"); continue; }
    Console.WriteLine($"\n### {t.FullName} : {t.Attributes.ToString().Split(',').First()}");
    try
    {
        foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .OrderBy(m => m.MemberType).ThenBy(m => m.Name))
        {
            if (m.MemberType == MemberTypes.Method && ((MethodInfo)m).IsSpecialName) continue;
            try { Console.WriteLine($"  {Describe(m)}"); }
            catch { Console.WriteLine($"  ? {m.Name} (unresolvable)"); }
        }
    }
    catch (Exception ex) { Console.WriteLine($"  (dump failed: {ex.GetType().Name})"); }
}

static string Describe(MemberInfo m) => m switch
{
    MethodInfo mi => $"M {TypeName(mi.ReturnType)} {mi.Name}({string.Join(", ", mi.GetParameters().Select(p => TypeName(p.ParameterType) + " " + p.Name))})",
    PropertyInfo pi => $"P {TypeName(pi.PropertyType)} {pi.Name} {{ {(pi.CanRead ? "get; " : "")}{(pi.CanWrite ? "set; " : "")}}}",
    FieldInfo fi => $"F {TypeName(fi.FieldType)} {fi.Name}",
    EventInfo ei => $"E {TypeName(ei.EventHandlerType)} {ei.Name}",
    _ => m.ToString() ?? "?"
};

static string TypeName(Type t)
{
    if (t.IsGenericType)
        return $"{t.Name.Split('`')[0]}<{string.Join(",", t.GetGenericArguments().Select(TypeName))}>";
    return t.Name switch { "Void" => "void", "String" => "string", _ => t.Name };
}
