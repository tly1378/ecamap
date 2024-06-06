using System.Reflection;
using ECA.Tool;
using CommandLine;

internal class Program
{
    private class Options
    {
        [Option('o', "output", Required = true, HelpText = "Output file path.")]
        public string OutputFilePath { get; set; }

        [Option('p', "project", Required = true, HelpText = "Unity project assembly path.")]
        public string UnityProjectAssemblyPath { get; set; }

        [Option('e', "engine", Required = true, HelpText = "Unity engine assembly path.")]
        public string UnityEngineAssembliesPath { get; set; }

        [Option('t', "eca", Required = false, HelpText = "ECA path.")]
        public string ECAPath { get; set; }
    }

    private static void RunWithOptions(Options options)
    {
        Console.WriteLine($"Output File Path: {options.OutputFilePath}");
        Console.WriteLine($"Unity Project Assembly Path: {options.UnityProjectAssemblyPath}");

        string outputPath = options.OutputFilePath;
        string csprojPath = options.UnityProjectAssemblyPath;
        csprojPath = Path.GetFullPath(csprojPath);
        List<string> dllPaths = null;
        dllPaths = new List<string>() { csprojPath };
        dllPaths.AddRange(Directory.GetFiles(options.UnityEngineAssembliesPath, "*.dll"));
        dllPaths.Add(options.ECAPath);

        ECATool.sDebugLog = Console.WriteLine;

        Assembly[] serverAssemblies = new Assembly[dllPaths.Count];
        for (int i = 0; i < dllPaths.Count; i++)
        {
            string dllPath = dllPaths[i];

            AssemblyName assemblyName = AssemblyName.GetAssemblyName(dllPath);
            Console.WriteLine($"全名: {assemblyName.FullName}");
            Console.WriteLine($"版本: {assemblyName.Version}");
            Console.WriteLine($"文化信息: {(string.IsNullOrEmpty(assemblyName.CultureInfo?.Name) ? "neutral" : assemblyName.CultureInfo.Name)}");
            Console.WriteLine($"公钥标记: {BitConverter.ToString(assemblyName.GetPublicKeyToken()).Replace("-", "").ToLower()}");
            Console.WriteLine($"路径: {dllPath}\n");

            serverAssemblies[i] = Assembly.LoadFrom(dllPath);
        }
        ECATool.GenerateECAMap(serverAssemblies, outputPath);
    }

    private static void HandleParseError(IEnumerable<Error> errs)
    {
        foreach (var error in errs)
        {
            Console.WriteLine($"Error: {error.Tag}");
        }
    }

    private static void Main(string[] args)
    {
        Parser.Default.ParseArguments<Options>(args)
              .WithParsed(RunWithOptions)
              .WithNotParsed(HandleParseError);
    }
}