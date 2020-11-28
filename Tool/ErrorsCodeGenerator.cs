using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.Docs.Tools
{
    [Generator]
    public class ErrorsCodeGenerator : ISourceGenerator
    {
        public void Execute(GeneratorExecutionContext context)
        {
            // begin creating the source we'll inject into the users compilation
            var sourceBuilder = new StringBuilder(@"
                using System;
                namespace Microsoft.Docs.Build
                {
                    public static class Errors
                    {
            ");

            var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance) // see height_in_inches in sample yml 
                        .Build();

            foreach (AdditionalText file in context.AdditionalFiles)
            {
                if (Path.GetExtension(file.Path).Equals(".yml", StringComparison.OrdinalIgnoreCase))
                {
                    var p = deserializer.Deserialize<Dictionary<string, Dictionary<string, Error>>>(File.ReadAllText(file.Path));
                    foreach (var error in p)
                    {
                        sourceBuilder.AppendLine($@"public static class {error.Key} {{");
                        foreach (var e in error.Value)
                        {
                            var methodName = string.Join("", e.Key.Split('-').Select(e => e.First().ToString().ToUpper() + e.Substring(1)));
                            sourceBuilder.AppendLine($@"///{e.Value.Comment}");
                            sourceBuilder.AppendLine($@"public static Error {methodName} ()");
                            sourceBuilder.AppendLine($@"=> new Error(""{e.Value.Level}"", ""{e.Key}"", ""{e.Value.Message}"");");
                        }
                        sourceBuilder.AppendLine($@"}}");
                    }
                }
            }

            sourceBuilder.Append(@"
                }
            }");

            // inject the created source into the users compilation
            context.AddSource("helloWorldGenerator", SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required for this one
        }

        private class Error
        {
            public string Level { get; set; } = string.Empty;

            public string Message { get; set; } = string.Empty;

            public string Comment { get; set; } = string.Empty;
        }
    }
}
