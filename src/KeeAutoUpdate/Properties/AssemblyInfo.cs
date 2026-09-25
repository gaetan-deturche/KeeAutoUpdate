using System.Reflection;
using System.Runtime.InteropServices;

[assembly: AssemblyTitle("KeeAutoUpdate")]
[assembly: AssemblyDescription("Checks for and installs KeePass 2.x updates automatically.")]
[assembly: AssemblyCompany("")]
// KeePass's plugin loader (PluginManager.LoadPlugins) silently skips any Plugins-folder file
// whose file-version ProductName is set but doesn't equal exactly this string - no error, no
// log, it just never appears in Tools > Plugins. This is not documented anywhere; found by
// disassembling KeePass.exe's PluginManager.LoadPlugins with ildasm.
[assembly: AssemblyProduct("KeePass Plugin")]
[assembly: AssemblyCopyright("")]
[assembly: ComVisible(false)]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]
