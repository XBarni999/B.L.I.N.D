using System;
using System.IO;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;
class Program {
 static void Main(string[] args) {
  var d = new CSharpDecompiler(args[0], new DecompilerSettings { ThrowOnAssemblyResolveErrors = false });
  foreach (var name in args[1].Split(',')) {
   if (name == "LIST") { foreach (var t in d.TypeSystem.MainModule.TypeDefinitions) Console.WriteLine(t.FullName); }
   else Console.WriteLine(d.DecompileTypeAsString(new FullTypeName(name)));
  }
 }
}
