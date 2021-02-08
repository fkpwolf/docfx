using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.Docs.Build.Analyzer;

namespace Microsoft.Docs.Build.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public class BuildErrorsAnalyzer : DiagnosticAnalyzer
    {
        public const string DiagnosticId = "BuildErrorsAnalyzer";

        // You can change these strings in the Resources.resx file. If you do not want your analyzer to be localize-able, you can use regular strings for Title and MessageFormat.
        // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Localizing%20Analyzers.md for more on localization
        private const string Category = "Naming";
        private readonly LocalizableString _description = new LocalizableResourceString(nameof(Resources.AnalyzerDescription), Resources.ResourceManager, typeof(Resources));

        private static readonly LocalizableString ShouldBeInterpolatedStringTitle = new LocalizableResourceString(nameof(Resources.ShouldBeInterpolatedStringTitle), Resources.ResourceManager, typeof(Resources));
        public static readonly DiagnosticDescriptor ShouldBeInterpolatedStringRule = new DiagnosticDescriptor(DiagnosticId, ShouldBeInterpolatedStringTitle, ShouldBeInterpolatedStringTitle, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: _description);

        private static readonly LocalizableString ShouldBeMemberAccessExpressionTitle = new LocalizableResourceString(nameof(Resources.ShouldBeMemberAccessExpressionTitle), Resources.ResourceManager, typeof(Resources));
        public static readonly DiagnosticDescriptor ShouldBeMemberAccessExpressionRule = new DiagnosticDescriptor(DiagnosticId, ShouldBeMemberAccessExpressionTitle, ShouldBeMemberAccessExpressionTitle, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: _description);

        private static readonly LocalizableString ShouldBePlainStringTitle = new LocalizableResourceString(nameof(Resources.ShouldBePlainStringTitle), Resources.ResourceManager, typeof(Resources));
        public static readonly DiagnosticDescriptor ShouldBePlainStringRule = new DiagnosticDescriptor(DiagnosticId, ShouldBePlainStringTitle, ShouldBePlainStringTitle, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true, description: _description);


        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get { return ImmutableArray.Create(ShouldBeInterpolatedStringRule, ShouldBeMemberAccessExpressionRule, ShouldBePlainStringRule); } }

        public override void Initialize(AnalysisContext context)
        {
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
            context.EnableConcurrentExecution();

            // TODO: Consider registering other actions that act on syntax instead of or in addition to symbols
            // See https://github.com/dotnet/roslyn/blob/master/docs/analyzers/Analyzer%20Actions%20Semantics.md for more information
            context.RegisterSyntaxTreeAction(AnalyzeTree);
        }

        private void AnalyzeTree(SyntaxTreeAnalysisContext context)
        {
            // TODO only apply on Errors.cs
            var root = context.Tree.GetRoot();
            var errorClasses = from c in root.DescendantNodes().OfType<ClassDeclarationSyntax>()
                               where c.Identifier.ValueText != "Errors" // exclude root class
                               select c;
            foreach (var errorClass in errorClasses)
            {
                var classDict = new Dictionary<string, Dictionary<string, string>>();
                var newErrors = from newError in errorClass.DescendantNodes().OfType<ObjectCreationExpressionSyntax>()
                                where newError.Type.ToString() == "Error"
                                select newError;
                foreach (var error in newErrors)
                {
                    if (error.ArgumentList.Arguments[0].Expression is not MemberAccessExpressionSyntax level)
                    {
                        var diagnostic = Diagnostic.Create(ShouldBeMemberAccessExpressionRule, error.ArgumentList.Arguments[0].Expression.GetLocation());

                        context.ReportDiagnostic(diagnostic);
                    }

                    if (error.ArgumentList.Arguments[1].Expression is not LiteralExpressionSyntax code)
                    {
                        var diagnostic = Diagnostic.Create(ShouldBePlainStringRule, error.ArgumentList.Arguments[1].Expression.GetLocation());

                        context.ReportDiagnostic(diagnostic);
                    }

                    if (error.ArgumentList.Arguments[2].Expression is not InterpolatedStringExpressionSyntax msg)
                    {
                        var diagnostic = Diagnostic.Create(ShouldBeInterpolatedStringRule, error.ArgumentList.Arguments[2].Expression.GetLocation());

                        context.ReportDiagnostic(diagnostic);
                    }
                }
            }
        }
    }
}
