using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Options;

namespace DumpCommitData
{
    class Canonicalization
    {
        public static (SyntaxNode CanonicalSyntaxNode, Dictionary<string, string> VariableNameMap) CanonicalizeSyntaxNode(SyntaxNode rootNode, Dictionary<string, string> initVariableNameMap = null, bool extractAllVariablesFirst = false)
        {
            (SyntaxNode newRootNode, Dictionary<string, string> variableNameMap) = _canonicalizeSyntaxNode(rootNode, initVariableNameMap);

            if (extractAllVariablesFirst)
                (newRootNode, variableNameMap) = _canonicalizeSyntaxNode(rootNode, variableNameMap);

            newRootNode = RemoveUsingStatements(newRootNode);
            newRootNode = UnifyFormat(newRootNode);

            return (newRootNode, variableNameMap);
        }

        public class SyntaxNodeCanonicalizer : CSharpSyntaxRewriter
        {
            public Dictionary<string, string> VariableNameMap = new Dictionary<string, string>();

            public SyntaxNodeCanonicalizer(Dictionary<string, string> variableNameMap = null)
            {
                if (variableNameMap != null)
                    VariableNameMap = new Dictionary<string, string>(variableNameMap);
            }

            private string RegisterNewReanmedVariable(string identifierName)
            {
                if (VariableNameMap.ContainsKey(identifierName))
                    return VariableNameMap[identifierName];

                var newVarName = "VAR" + VariableNameMap.Count;
                VariableNameMap[identifierName] = newVarName;

                return newVarName;
            }

            private bool TryGetRenamedVariable(string identifierName, out string newName)
            {
                return VariableNameMap.TryGetValue(identifierName, out newName);
            }

            public override SyntaxNode VisitMethodDeclaration(MethodDeclarationSyntax node)
            {
                var paramList = node.ParameterList;
                var newParamList = SyntaxFactory.ParameterList();
                foreach (var param in paramList.Parameters)
                {
                    var newName = RegisterNewReanmedVariable(param.Identifier.ValueText);

                    var newParam = param.WithIdentifier(SyntaxFactory.Identifier(newName));
                    newParamList = newParamList.AddParameters(newParam);
                }

                return base.VisitMethodDeclaration(node.WithParameterList(newParamList));
            }

            public override SyntaxNode VisitForEachStatement(ForEachStatementSyntax node)
            {
                var newIdName = RegisterNewReanmedVariable(node.Identifier.ValueText);
                node = node.WithIdentifier(SyntaxFactory.Identifier(newIdName));

                return base.VisitForEachStatement(node);
            }

            public override SyntaxNode VisitCatchDeclaration(CatchDeclarationSyntax node)
            {
                if (node.Identifier.ValueText != null)
                {
                    var newIdName = RegisterNewReanmedVariable(node.Identifier.ValueText);
                    node = node.WithIdentifier(SyntaxFactory.Identifier(newIdName));
                }
                
                return base.VisitCatchDeclaration(node);
            }

            public override SyntaxNode VisitSimpleLambdaExpression(SimpleLambdaExpressionSyntax node)
            {
                var param = node.Parameter;
                var newName = RegisterNewReanmedVariable(param.Identifier.ValueText);
                var newParam = param.WithIdentifier(SyntaxFactory.Identifier(newName));
                node = node.WithParameter(newParam);

                return base.VisitSimpleLambdaExpression(node);
            }

            public override SyntaxNode VisitParenthesizedLambdaExpression(ParenthesizedLambdaExpressionSyntax node)
            {
                var paramList = node.ParameterList;
                var newParamList = SyntaxFactory.ParameterList();
                foreach (var param in paramList.Parameters)
                {
                    var newName = RegisterNewReanmedVariable(param.Identifier.ValueText);

                    var newParam = param.WithIdentifier(SyntaxFactory.Identifier(newName));
                    newParamList = newParamList.AddParameters(newParam);
                }

                return base.VisitParenthesizedLambdaExpression(node.WithParameterList(newParamList));
            }

            public override SyntaxNode VisitConstructorDeclaration(ConstructorDeclarationSyntax node)
            {
                var paramList = node.ParameterList;
                var newParamList = SyntaxFactory.ParameterList();
                foreach (var param in paramList.Parameters)
                {
                    var newName = RegisterNewReanmedVariable(param.Identifier.ValueText);

                    var newParam = param.WithIdentifier(SyntaxFactory.Identifier(newName));
                    newParamList = newParamList.AddParameters(newParam);
                }

                return base.VisitConstructorDeclaration(node.WithParameterList(newParamList));
            }

            private bool IsFieldLikeDeclaration(MemberDeclarationSyntax n)
            {
                return n is BaseFieldDeclarationSyntax ||
                    n is BasePropertyDeclarationSyntax ||
                    n is DelegateDeclarationSyntax;
            }

            public override SyntaxNode VisitClassDeclaration(ClassDeclarationSyntax node)
            {
                var rewrittenMembers = new List<MemberDeclarationSyntax>();
                foreach(var field in node.Members.Where(IsFieldLikeDeclaration))
                {
                    rewrittenMembers.Add((MemberDeclarationSyntax)base.Visit(field));
                }

                foreach(var member in node.Members.Where(n=>!IsFieldLikeDeclaration(n)))
                {
                    rewrittenMembers.Add((MemberDeclarationSyntax)base.Visit(member));
                }
                return node.WithMembers(SyntaxFactory.List(rewrittenMembers));
            }

            public override SyntaxNode VisitIdentifierName(IdentifierNameSyntax node)
            {
                if (TryGetRenamedVariable(node.Identifier.ValueText, out var newName)) {
                    return node.WithIdentifier(SyntaxFactory.Identifier(newName));
                }
                return base.VisitIdentifierName(node);
            }
  
            public override SyntaxNode VisitLiteralExpression(LiteralExpressionSyntax node)
            {
                var newNode = node;
                if (node.IsKind(SyntaxKind.StringLiteralExpression) || 
                    node.IsKind(SyntaxKind.CharacterLiteralExpression) || 
                    node.IsKind(SyntaxKind.NumericLiteralExpression))
                    // even if the node is a Numerical Literal token, we replace it as a string literal
                    if (node.Token.ValueText != "0" && node.Token.ValueText != "1")
                        newNode = node.WithToken(SyntaxFactory.Token(node.Token.LeadingTrivia, SyntaxKind.StringLiteralToken, "LITERAL",
                            "LITERAL", node.Token.TrailingTrivia));
                return base.VisitLiteralExpression(newNode);
            }

            public override SyntaxNode VisitInterpolatedStringExpression(InterpolatedStringExpressionSyntax node)
            {
                var newNode = SyntaxFactory.LiteralExpression(SyntaxKind.StringLiteralExpression,
                    SyntaxFactory.Token(node.GetLeadingTrivia(), SyntaxKind.StringLiteralToken, "LITERAL",
                        "LITERAL", node.GetTrailingTrivia()));
                return base.VisitLiteralExpression(newNode);
            }

            public override SyntaxNode VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
            {
                var newNode = node;
                // if it's accessing an identifier name, check if it's a local one (defined in the preceeding context)
                if (node.Expression.IsKind(SyntaxKind.IdentifierName))
                {
                    var idNode = (node.Expression as IdentifierNameSyntax);
                    if (TryGetRenamedVariable(idNode.Identifier.ValueText, out var newName))
                    {
                        newNode = node.WithExpression(SyntaxFactory.IdentifierName(SyntaxFactory.Identifier(newName)));
                    }
                }

                return base.VisitMemberAccessExpression(newNode);
            }

            public override SyntaxNode VisitVariableDeclaration(VariableDeclarationSyntax node)
            {
                // anonymize variable names in variable declaration
                // var varList = new SeparatedSyntaxList<VariableDeclaratorSyntax>();
                var varList = new List<VariableDeclaratorSyntax>();
                foreach(var varNode in node.Variables)
                {
                    var varName = varNode.Identifier.ValueText;
                    var newName = RegisterNewReanmedVariable(varName);
                    varList.Add(varNode.WithIdentifier(SyntaxFactory.Identifier(newName)));
                }
                var newNode = node.WithVariables(SyntaxFactory.SeparatedList(varList));
                
                return base.VisitVariableDeclaration(newNode);
            }

            public override SyntaxNode VisitPropertyDeclaration(PropertyDeclarationSyntax node)
            {
                var newName = RegisterNewReanmedVariable(node.Identifier.ValueText);
                var newProperty = node.WithIdentifier(SyntaxFactory.Identifier(newName));

                return base.VisitPropertyDeclaration(newProperty);
            }
        }

        public static (SyntaxNode CanonicalizedSyntaxNode, Dictionary<string, string> VariableNameMap) _canonicalizeSyntaxNode(SyntaxNode rootNode, Dictionary<string, string> variableRenameMap = null)
        {
            var rewriter = new SyntaxNodeCanonicalizer(variableRenameMap);
            var canonicalizedSyntaxNode = rewriter.Visit(rootNode);

            return (canonicalizedSyntaxNode, rewriter.VariableNameMap);
        }


        public static SyntaxNode UnifyFormat(SyntaxNode rootNode)
        {
            var allComments = rootNode.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
                                                                     t.IsKind(SyntaxKind.MultiLineCommentTrivia) ||
                                                                     t.IsKind(SyntaxKind
                                                                         .MultiLineDocumentationCommentTrivia) ||
                                                                     t.IsKind(SyntaxKind
                                                                         .SingleLineDocumentationCommentTrivia) || 
                                                                     t.IsKind(SyntaxKind.DocumentationCommentExteriorTrivia) || 
                                                                     t.IsKind(SyntaxKind.EndOfDocumentationCommentToken));

            rootNode = rootNode.ReplaceTrivia(allComments, (t1, t2) => SyntaxFactory.CarriageReturn);

            var allAttributates = rootNode.DescendantNodes().Where(t => t.IsKind(SyntaxKind.AttributeList));
            rootNode = rootNode.RemoveNodes(allAttributates, SyntaxRemoveOptions.KeepNoTrivia);

            try
            {
                using (var workspace = new AdhocWorkspace())
                {
                    var optionSet = workspace.Options;
                    optionSet = optionSet.WithChangedOption(CSharpFormattingOptions.WrappingKeepStatementsOnSingleLine, true);
                    //def = def.WithChangedOption(CSharpFormattingOptions.newline, true);
                    return Formatter.Format(rootNode, workspace);
                }
            }
            catch (Exception e)
            {
                return rootNode;
            }
        }

        public static SyntaxNode UnifyFormat_fullcode(SyntaxNode rootNode)
        {
            var text = string.Join(' ', rootNode.DescendantTokens()
                .Select(t => t.ToString() ==";"?";\n":t.ToString()).ToArray());
            var ast = CSharpSyntaxTree.ParseText(text);

            using (var workspace = new AdhocWorkspace())
            {
                var optionSet = workspace.Options;
                //def = def.WithChangedOption(CSharpFormattingOptions.IndentBlock, true);
                //def = def.WithChangedOption(CSharpFormattingOptions.newline, true);
                return Formatter.Format(ast.GetRoot(), workspace);
            }                
        }

        public static SyntaxNode RemoveUsingStatements(SyntaxNode rootNode)
        {
            var usingStmts = rootNode.DescendantNodes().Where(node => node.IsKind(SyntaxKind.UsingDirective));
            return rootNode.RemoveNodes(usingStmts, SyntaxRemoveOptions.KeepNoTrivia);
        }

        public static void Main(string[] args)
        {
            var code1 = @"using System;

                        class Hello
                                {
                                    public static readonly void Main(){
                                        foreach(var h in asdf){
                                             h.Write(test);
                                        }
    
                                        var foo = new Bar(1,2,3,4,5,SomeProperty,""MyString"");
                                        var foo1 = sdf(x => x.sdf);
                                        var foo1 = sdf((x,y,z) => x.sdf);
                                        var foo2 = sdf();
                                        Assert.Equal(SomeInt, new int[] {1,2,3,4});

                                        Main();
                                    }

                                    public static int SomeInt = 2;
                                    public static string SomeProperty {get; set;};

                                    
                                }

                     class Hello2 {
                        public static int sdf = 3;
                     }
";

            var syntaxTree = CSharpSyntaxTree.ParseText(code1);
            
            var nodes = syntaxTree.GetRoot().DescendantNodes().ToList();
            Console.WriteLine(Canonicalization.CanonicalizeSyntaxNode(syntaxTree.GetRoot(), extractAllVariablesFirst:true).CanonicalSyntaxNode.GetText());

            Console.ReadLine();
        }
    }
}
