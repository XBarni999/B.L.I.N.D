using System;
using System.Reflection;
class P {
    static void Main() {
        var asm = Assembly.LoadFrom(@"F:\Games\Nuclear.Option.v0.34.1\NuclearOption_Data\Managed\Assembly-CSharp.dll");
        var t = asm.GetType("TargetCam");
        if (t != null) {
            foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)) {
                Console.WriteLine($"{m.MemberType}: {m.Name}");
            }
        } else {
            Console.WriteLine("TargetCam not found in root namespace");
            foreach (var type in asm.GetTypes()) {
                if (type.Name.Contains("TargetCam") || type.Name.Contains("MFD") || type.Name.Contains("Cockpit")) {
                    Console.WriteLine(type.FullName);
                }
            }
        }
    }
}
