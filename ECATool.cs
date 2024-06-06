using System.Reflection;
using System.Text;
using System.Xml.Linq;

namespace ECA.Tool
{
    public class ECATool
    {
        private const string HEADER = 
            "using System;\n" +
            "using System.Collections.Generic;\n" +
            "using System.Reflection;\n\n" +
            "namespace ECA\n" +
            "{\n" +
                "\tpublic static class ECAMap\n" +
                "\t{\n" +
                    "\t\tpublic static readonly Dictionary<string, MethodInfo> sMethods = new Dictionary<string, MethodInfo>()\n" +
                    "\t\t{\n";

        private const string FOOTER = 
                    "\t\t};\n" +
                "\t}" +
            "\n}";

        private const string IKEY_NAME = "Key";

        private static readonly string[] TARGET_ATTRIBUTE = new string[] { "ActionAttribute", "CheckerAttribute" };

        public static Action<string> sDebugLog;

        public static void GenerateECAMap(Assembly[] assemblies, string outputPath)
        {
            var builder = new StringBuilder();
            builder.Append($"// 生成时间：{DateTime.Now}\n");
            builder.Append(HEADER);
            int steps = assemblies.Length;
            float stepSize = 1.0f / steps;
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch //(Exception exception)
                {
                    // PS: 部分程序集依赖了未载入的第三方库类，如果这些程序集并不是ECA的目标程序集，则不必为其载入这些第三方库类，避免无谓的开销。
                    //sDebugLog?.Invoke(string.Format("[ECA] 无法从程序集{0}中获取类， 错误信息：{1}", assembly.FullName, exception));
                    continue;
                }
                for (int j = 0; j < types.Length; j++)
                {
                    Type type = types[j];

                    try
                    {
                        BuildType(type, builder);
                    }
                    catch (TypeLoadException typeException)
                    {
                        //sDebugLog?.Invoke(string.Format("[GenerateECAMap] {0}无法解析，这一般是正常的：{1}", type.Name, typeException));
                    }
                    catch (FileNotFoundException fileException)
                    {
                        //sDebugLog?.Invoke(string.Format("[GenerateECAMap] {0}无法解析，请确认其是否在需要解析的域内：{1}", type.Name, fileException));
                    }
                    catch (Exception exception)
                    {
                        sDebugLog?.Invoke(string.Format("[GenerateECAMap] {0}无法解析，错误信息：{1}", type.Name, exception));
                    }
                }
            }

            builder.Append(FOOTER);
            var streamWriter = new StreamWriter(outputPath);
            streamWriter.Write(builder);
            streamWriter.Dispose();
        }

        private static void BuildType(Type type, StringBuilder builder)
        {
            // 遍历类的方法
            MethodInfo[] methods;
            try
            {
                methods = type.GetMethods();
            }
            catch (Exception exception)
            {
                sDebugLog?.Invoke(string.Format("[ECA] 无法从类{0}中获取方法，错误信息：{1}", type.Name, exception));
                return;
            }

            foreach (MethodInfo method in methods)
            {
                IEnumerable<Attribute> attributes;
                try
                {
                    attributes = method.GetCustomAttributes();
                }
                catch (Exception exception)
                {
                    // PS: 部分函数的属性来自第三方库，这些函数往往不是ECA的目标函数，但也有极少的例外，可以稍微检查一下。
                    sDebugLog?.Invoke(string.Format("[ECA] 无法从{0}的方法{1}中获取属性，错误信息：{2}", type.Name, method.Name, exception));
                    continue;
                }

                // 遍历类的方法的特性
                Attribute targetAttribute = null;
                foreach (var attribute in attributes)
                {
                    foreach (var attributeName in TARGET_ATTRIBUTE)
                    {
                        if (attribute.GetType().Name == attributeName)
                        {
                            targetAttribute = attribute;
                            goto end;
                        }
                    }
                }
            end:
                if (targetAttribute != null)
                {
                    string className = type.FullName;
                    string functionName = method.Name;
                    string path = string.Concat(className, '.', functionName);
                    string key = path;
                    var targetType = targetAttribute.GetType();
                    var propertyInfo = targetType.GetProperty(IKEY_NAME);
                    if (propertyInfo != null && propertyInfo.PropertyType == typeof(string))
                    {
                        var keyValue = propertyInfo.GetValue(targetAttribute) as string;
                        if (!string.IsNullOrEmpty(keyValue))
                        {
                            key = keyValue;
                        }
                    }
                    var parameters = method.GetParameters();
                    string paramList;
                    if (parameters.Length == 0)
                    {
                        paramList = "Array.Empty<Type>()";
                    }
                    else
                    {
                        string[] paramTypes = new string[parameters.Length];
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            paramTypes[i] = string.Concat("typeof(", GetFriendlyTypeName(parameters[i].ParameterType), ")");
                        }
                        paramList = string.Join(",", paramTypes);
                        paramList = string.Concat("new Type[] {", paramList, "}");
                    }

                    var line = string.Concat("\t\t\t{", string.Format("\"{0}\", typeof({1}).GetMethod(nameof({2}), {3})", key, className, path, paramList), "},\n");
                    builder.Append(line);
                }
            }
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (!type.IsGenericType)
            {
                return type.FullName;
            }

            int index = type.FullName.IndexOf('`');
            string name = index >= 0 ? type.FullName.Substring(0, index) : type.FullName;
            string genericTypes = string.Join(", ", type.GetGenericArguments().Select(t => GetFriendlyTypeName(t)));
            return $"{name}<{genericTypes}>";
        }

        public static List<string> FindDllsInProjectAndReferences(string csprojPath)
        {
            var processedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var uniqueDllNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var dllPaths = new List<string>();
            FindDllsInProjectAndReferencesRecursive(csprojPath, processedProjects, uniqueDllNames, dllPaths);
            return dllPaths;
        }

        private static void FindDllsInProjectAndReferencesRecursive(string csprojPath, HashSet<string> processedProjects, HashSet<string> uniqueDllNames, List<string> dllPaths)
        {
            if (processedProjects.Contains(csprojPath))
            {
                return;
            }

            processedProjects.Add(csprojPath);
            var projectDirectory = Path.GetDirectoryName(csprojPath);
            var dllsInDirectory = FindDllsInProjectDirectory(projectDirectory);

            foreach (var dllPath in dllsInDirectory)
            {
                var dllName = Path.GetFileName(dllPath);
                if (!uniqueDllNames.Contains(dllName))
                {
                    uniqueDllNames.Add(dllName);
                    dllPaths.Add(dllPath);
                }
            }

            var referencedProjects = ExtractProjectReferences(csprojPath);
            foreach (var referencedProject in referencedProjects)
            {
                FindDllsInProjectAndReferencesRecursive(referencedProject, processedProjects, uniqueDllNames, dllPaths);
            }
        }

        private static IEnumerable<string> ExtractProjectReferences(string csprojPath)
        {
            var document = XDocument.Load(csprojPath);
            var projectReferences = new List<string>();

            foreach (var pr in document.Descendants("ProjectReference"))
            {
                string referencePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csprojPath), pr.Attribute("Include").Value));
                projectReferences.Add(referencePath);
            }

            return projectReferences;
        }

        private static IEnumerable<string> FindDllsInProjectDirectory(string projectDirectory)
        {
            var dirsToScan = new[] { "bin" };
            var dllPaths = new List<string>();

            foreach (var dir in dirsToScan)
            {
                var fullPath = Path.Combine(projectDirectory, dir);
                if (Directory.Exists(fullPath))
                {
                    dllPaths.AddRange(Directory.GetFiles(fullPath, "*.dll", SearchOption.AllDirectories));
                }
            }

            return dllPaths;
        }
    }
}
