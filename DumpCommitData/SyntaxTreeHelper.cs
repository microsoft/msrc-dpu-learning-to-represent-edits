using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Newtonsoft.Json.Linq;
using System.IO;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DumpCommitData
{
    class JsonSyntaxTreeHelper
    {
        private Dictionary<string, List<(string FieldName, string FieldType)>> grammar;

        public JsonSyntaxTreeHelper(string grammarFilePath)
        {
            var grammarJson = JArray.Parse(File.ReadAllText(grammarFilePath));

            grammar = new Dictionary<string, List<(string FieldName, string FieldType)>>();
            foreach (var entry in grammarJson)
            {
                var constructorName = entry["constructor"].ToString();
                var fields = entry["fields"].Select(x => (x["name"].ToString(), x["type"].ToString())).ToList();

                grammar[constructorName] = fields;
            }
        }

        public SyntaxNode GetSyntaxNodeFromJToken(JToken syntaxNodeJToken)
        {
            var constructorName = syntaxNodeJToken["Constructor"].Value<string>();

            var fields = syntaxNodeJToken["Fields"] as JObject;

            var fieldNamesAndValues = new List<(string Name, object Value)>();

            foreach (var field in fields)
            {
                var fieldName = field.Key;
                var fieldJToken = field.Value;
                if (fieldJToken.Type == JTokenType.Array)
                {
                    var fieldValue = new List<object>();
                    foreach (var childJToken in fieldJToken)
                    {
                        var childSyntaxNode = GetSyntaxNodeFromJToken(childJToken);
                        fieldValue.Add(childSyntaxNode);
                    }
                }
                else
                {
                    var fieldSyntaxNode = GetSyntaxNodeFromJToken(fieldJToken);
                    fieldNamesAndValues.Add((fieldName, fieldSyntaxNode));
                }
            }

            var fieldValues = fieldNamesAndValues.Select(x => x.Value).ToArray();

            var factoryMethodName = constructorName.Substring(0, constructorName.Length - "Syntax".Length);
            var syntaxNode = typeof(SyntaxFactory).GetMethod(factoryMethodName).Invoke(null, fieldValues);
            var result = syntaxNode as SyntaxNode;

            return result;
        }

        public JToken GetSyntaxNodeJObject(dynamic syntaxNode, TokenIndex syntaxTokenIndex = null)
        {
            if (syntaxNode == null)
                return null;

            var nodeType = syntaxNode.GetType().Name;
            if (nodeType == "SyntaxToken")
            {
                dynamic syntaxTokenJObj = new JObject();

                syntaxTokenJObj.Constructor = nodeType;
                syntaxTokenJObj.Value = syntaxNode.ValueText;

                var pos = -1;
                if (syntaxTokenIndex != null)
                    pos = syntaxTokenIndex.GetTokenAndPositionBySpan(((SyntaxToken) syntaxNode).Span).Position;

                syntaxTokenJObj.Position = pos;

                return syntaxTokenJObj;
            }

            dynamic node = Convert.ChangeType(syntaxNode, syntaxNode.GetType());

            var fields = (List<(string FieldName, string FieldType)>)grammar[nodeType];
            var fieldJObjects = new JObject();
            foreach (var field in fields)
            {
                dynamic fieldValue = syntaxNode.GetType().GetProperty(field.FieldName).GetValue(node, null);
                // dynamic typedFieldValue = Convert.ChangeType(fieldValue, Type.GetType(field.FieldType));

                if (field.FieldType.Contains("SyntaxList"))
                {
                    var fieldJObj = new JArray();
                    foreach (var fieldChildEntry in fieldValue)
                    {
                        var fieldChildEntryJObject = GetSyntaxNodeJObject(fieldChildEntry, syntaxTokenIndex);
                        fieldJObj.Add(fieldChildEntryJObject);
                    }

                    fieldJObjects[field.FieldName] = fieldJObj;
                }
                else
                {
                    var fieldJObj = GetSyntaxNodeJObject(fieldValue, syntaxTokenIndex);
                    fieldJObjects[field.FieldName] = fieldJObj;
                }
            }

            dynamic nodeJObj = new JObject();
            nodeJObj.Constructor = nodeType;
            nodeJObj.Fields = JToken.FromObject(fieldJObjects);

            return nodeJObj;
        }

        public JToken GetBlockSyntaxJObjectForSyntaxNodes(IEnumerable<StatementSyntax> statements)
        {
            var blockNode = SyntaxFactory.Block(statements);
            var jtoken = GetSyntaxNodeJObject(blockNode);

            return jtoken;
        }

        public JToken GetJObjectForSyntaxNode(SyntaxNode syntaxNode, TokenIndex syntaxTokenIndex)
        {
            var jtoken = GetSyntaxNodeJObject(syntaxNode, syntaxTokenIndex);

            return jtoken;
        }
    }

    class SyntaxTreeHelper
    {
        static readonly string codeTemplate = @"
class Hello
{
    void Main(){
        {CODE}                             
    }
}";

        public static IEnumerable<string> GetSyntaxTokenStrings(SyntaxNode rootNode)
        {
            return rootNode.DescendantTokens().Select(token => token.ValueText);
        }

        public static IEnumerable<string> GetSyntaxTokenStringsFromSourceCode(string sourceCode)
        {
            var syntaxTree = CSharpSyntaxTree.ParseText(sourceCode);
            var tokens = GetSyntaxTokenStrings(syntaxTree.GetRoot());
            return tokens;
        }

        public static BlockSyntax GetBlockSyntaxNodeForLinesOfCode(IEnumerable<string> codeLines)
        {
            var code = codeTemplate.Replace("{CODE}", string.Join("\n", codeLines));
            var sytnaxTree = CSharpSyntaxTree.ParseText(code);
            var node =  sytnaxTree.GetRoot();
            var body = ((MethodDeclarationSyntax)node.ChildNodes().First().ChildNodes().First()).Body;

            return body;
        }
    }
}
