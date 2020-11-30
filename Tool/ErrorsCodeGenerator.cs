// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Microsoft.Docs.Tools
{
    [Generator]
    public class ErrorsCodeGenerator : ISourceGenerator
    {
        private static readonly Regex s_parameterReg = new Regex(@"{(.+?)}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public void Execute(GeneratorExecutionContext context)
        {
            var deserializer = new DeserializerBuilder()
                        .WithNamingConvention(UnderscoredNamingConvention.Instance)
                        .Build();

            foreach (AdditionalText file in context.AdditionalFiles)
            {
                if (Path.GetExtension(file.Path).Equals(".yml", StringComparison.OrdinalIgnoreCase))
                {
                    var sourceBuilder = new StringBuilder(@"
                        using System;
                        namespace Microsoft.Docs.Build
                        {
                            public static partial class Errors
                            {
                    ");
                    var errorList = deserializer.Deserialize<Dictionary<string, Dictionary<string, Error>>>(File.ReadAllText(file.Path));
                    foreach (var category in errorList)
                    {
                        sourceBuilder.AppendLine($@"public static class {category.Key} {{");
                        foreach (var error in category.Value)
                        {
                            var methodName = string.Join("", error.Key.Split('-').Select(e => e.First().ToString().ToUpperInvariant() + e.Substring(1)));
                            var parameters = ExtractParameters(error.Value.Message);
                            sourceBuilder.AppendLine($@"///{error.Value.Comment}");
                            sourceBuilder.AppendLine($@"public static Error {methodName} ({parameters})");
                            sourceBuilder.AppendLine($@"=> new Error(""{error.Value.Level}"", ""{error.Key}"", ""{error.Value.Message}"");");
                        }
                        sourceBuilder.AppendLine($@"}}");
                    }
                    sourceBuilder.Append(@"
                        }
                    }");

                    // inject the created source into the users compilation
                    context.AddSource(Path.GetFileNameWithoutExtension(file.Path), SourceText.From(sourceBuilder.ToString(), Encoding.UTF8));
                }
            }
        }

        public void Initialize(GeneratorInitializationContext context)
        {
            // No initialization required for this one
        }

        private static string ExtractParameters(string message)
        {
            return string.Join(", ", s_parameterReg.Matches(message).Cast<Match>().Select(g => $"object {g.Groups[1].Value}"));
        }

        private class Error
        {
            public string Level { get; set; } = string.Empty;

            public string Message { get; set; } = string.Empty;

            public string Comment { get; set; } = string.Empty;
        }
    }
}
