using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Dynamic;
using System.IO;
using System.Text;
using System.Linq;
using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualBasic.CompilerServices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DumpCommitData
{
    class Pipeline
    {

        public static IEnumerable<SyntaxNode> GetNodesByLineSpan(SyntaxNode astNode, SourceText sourceText, int startLine, int endLine)
        {
            //var nodeVisitor = new LineContiguousNodeFinder(startLine, endLine);
            //nodeVisitor.Visit(astNode);

            //return nodeVisitor.Nodes;

            for (var lineId = startLine;
                lineId <= endLine;
                lineId++)
            {
                var lineSource = sourceText.Lines[lineId];
                if (string.IsNullOrWhiteSpace(lineSource.ToString()))
                    continue;

                var lineSpan = lineSource.Span;
                if (lineSpan.IsEmpty)
                    continue;

                var node = astNode.FindNode(lineSpan);
                yield return node;
            }
        }

        public static IEnumerable<ChangeSample> GetChangesBetweenAsts(SyntaxTree previousFileAst, SyntaxTree updatedFileAst)
        {
            var changesWithContext = DiffInfo.GetChangesWithContext(previousFileAst, updatedFileAst);
            return changesWithContext;
        }

        public static IEnumerable<object> ProcessSingleRevision(string jsonLine, JsonSyntaxTreeHelper jsonSyntaxTreeHelper)
        {
            var entry = JObject.Parse(jsonLine);

            var previousFile = entry["prev_file"].ToString();
            var updatedFile = entry["updated_file"].ToString();

            // Console.WriteLine($"Processing {entry["id"]}");

            // File.WriteAllText("a.original.cs", previousFile);
            // File.WriteAllText("b.original.cs", updatedFile);

            var previousFileAst = CSharpSyntaxTree.ParseText(previousFile);
            var updatedFileAst = CSharpSyntaxTree.ParseText(updatedFile);

            (SyntaxNode canonicalPrevFileAst, Dictionary<string, string> prevFileVariableNameMap) = Canonicalization.CanonicalizeSyntaxNode(previousFileAst.GetRoot(), extractAllVariablesFirst:true);
            (SyntaxNode canonicalUpdatedFileAst, Dictionary<string, string> updatedFileVariableNameMap) = Canonicalization.CanonicalizeSyntaxNode(updatedFileAst.GetRoot(), prevFileVariableNameMap);

            var prevCodeFile = canonicalPrevFileAst.GetText();
            var updatedCodeFile = canonicalUpdatedFileAst.GetText();

            var prevFileTokens = canonicalPrevFileAst.DescendantTokens().ToList();
            var updatedFileTokens = canonicalUpdatedFileAst.DescendantTokens().ToList();

            var changesInRevision = GetChangesBetweenAsts(canonicalPrevFileAst.SyntaxTree, canonicalUpdatedFileAst.SyntaxTree);

            // File.WriteAllText("a.canonical.cs", canonicalPrevFileAst.GetText().ToString());
            // File.WriteAllText("b.canonical.cs", canonicalUpdatedFileAst.GetText().ToString());

            var prevTokenIndex = new TokenIndex(prevFileTokens);
            var updatedTokenIndex = new TokenIndex(updatedFileTokens);

            var changeId = 0;
            foreach (var change in changesInRevision)
            {
                var prevCodeChunkLineSpan = canonicalPrevFileAst.SyntaxTree.GetLineSpan(change.BeforeSpan.ChangeSpan);
                var updatedCodeChunkLineSpan = canonicalUpdatedFileAst.SyntaxTree.GetLineSpan(change.AfterSpan.ChangeSpan);

                var prevCodeChunkLineSpanStart = prevCodeFile.Lines[prevCodeChunkLineSpan.StartLinePosition.Line].Span.Start;
                var prevCodeChunkSpanEnd = prevCodeFile.Lines[prevCodeChunkLineSpan.EndLinePosition.Line].Span.End;

                var updatedCodeChunkLineSpanStart = updatedCodeFile.Lines[updatedCodeChunkLineSpan.StartLinePosition.Line].Span.Start;
                var updatedCodeChunkSpanEnd = updatedCodeFile.Lines[updatedCodeChunkLineSpan.EndLinePosition.Line].Span.End;

                // only consider changes of equal number of lines
                if (prevCodeChunkLineSpan.EndLinePosition.Line - prevCodeChunkLineSpan.StartLinePosition.Line 
                    != updatedCodeChunkLineSpan.EndLinePosition.Line - updatedCodeChunkLineSpan.StartLinePosition.Line)
                    continue;

                // TODO: remove trivial change

                // only consider SyntaxKind in allowedSytaxKinds
                var prevCodeChunkNodes = GetNodesByLineSpan(canonicalPrevFileAst, prevCodeFile, 
                    prevCodeChunkLineSpan.StartLinePosition.Line, prevCodeChunkLineSpan.EndLinePosition.Line);
                if (prevCodeChunkNodes.Any(node => !allowedSytaxKinds.Contains(node.Kind())))
                    continue;

                var updatedCodeChunkNodes = GetNodesByLineSpan(canonicalUpdatedFileAst, updatedCodeFile, 
                    updatedCodeChunkLineSpan.StartLinePosition.Line, updatedCodeChunkLineSpan.EndLinePosition.Line);
                if (updatedCodeChunkNodes.Any(node => !allowedSytaxKinds.Contains(node.Kind())))
                    continue;

                var previousCodeChunkTokens = prevTokenIndex
                    .GetTokensInSpan(prevCodeChunkLineSpanStart, prevCodeChunkSpanEnd)
                    .Select(token => token.ValueText)
                    .Where(token => !string.IsNullOrWhiteSpace(token) && !string.IsNullOrEmpty(token))
                    .ToArray();

                var updatedsCodeChunkTokens = updatedTokenIndex
                    .GetTokensInSpan(updatedCodeChunkLineSpanStart, updatedCodeChunkSpanEnd)
                    .Select(token => token.ValueText)
                    .Where(token => !string.IsNullOrWhiteSpace(token) && !string.IsNullOrEmpty(token))
                    .ToArray();

                if (previousCodeChunkTokens.Length > 0 && updatedsCodeChunkTokens.Length > 0 &&
                    IsValidCodeChunkTokens(previousCodeChunkTokens) && IsValidCodeChunkTokens(updatedsCodeChunkTokens) &&
                    !previousCodeChunkTokens.SequenceEqual(updatedsCodeChunkTokens))
                {
                    var changeSha = entry["id"] + "_" + changeId;

                    var prevCodeChunkBlockStmt = SyntaxFactory.Block(prevCodeChunkNodes.Select(node => (StatementSyntax)node));
                    var updatedCodeChunkBlockStmt = SyntaxFactory.Block(updatedCodeChunkNodes.Select(node => (StatementSyntax)node));

                    IDictionary<string, string> zeroIndexedVariableNameMap;
                     (prevCodeChunkBlockStmt, updatedCodeChunkBlockStmt, zeroIndexedVariableNameMap) =
                        zeroIndexVariableNames(prevCodeChunkBlockStmt, updatedCodeChunkBlockStmt);

                    var prevCodeChunkBlockStmtTokens = prevCodeChunkBlockStmt.DescendantTokens().Skip(1).SkipLast(1).ToArray();
                    var prevCodeChunkBlackStmtTokensIndex = new TokenIndex(prevCodeChunkBlockStmtTokens).InitInvertedIndex();

                    var updatedCodeChunkBlockStmtTokens = updatedCodeChunkBlockStmt.DescendantTokens().Skip(1).SkipLast(1).ToArray();
                    var updatedCodeChunkBlockStmtTokensIndex = new TokenIndex(updatedCodeChunkBlockStmtTokens).InitInvertedIndex();

                    var prevCodeBlockJObject = jsonSyntaxTreeHelper.GetJObjectForSyntaxNode(prevCodeChunkBlockStmt, prevCodeChunkBlackStmtTokensIndex);
                    var updatedCodeBlockJObject = jsonSyntaxTreeHelper.GetJObjectForSyntaxNode(updatedCodeChunkBlockStmt, updatedCodeChunkBlockStmtTokensIndex);

                    var precedingContextTokens = prevTokenIndex.GetTokensInSpan(change.BeforeSpan.SpanOfPrecedingContext);
                    var succeedingContextTokens = updatedTokenIndex.GetTokensInSpan(change.BeforeSpan.SpanOfSucceedingContext);

                    precedingContextTokens = zeroIndexVariableNames(precedingContextTokens, zeroIndexedVariableNameMap);
                    succeedingContextTokens = zeroIndexVariableNames(succeedingContextTokens, zeroIndexedVariableNameMap);

                    var prevCodeChunkBlockStmtTextTokens =
                        prevCodeChunkBlockStmtTokens.Select(token => token.ValueText).ToArray();
                    var updatedCodeChunkBlockStmtTextTokens =
                        updatedCodeChunkBlockStmtTokens.Select(token => token.ValueText).ToArray();

                    var prevCodeTextChunk = Utils.ExtractCodeTextFromBraces(prevCodeChunkBlockStmt.GetText().ToString());
                    prevCodeTextChunk = Utils.RemoveLeadingWhiteSpace(prevCodeTextChunk, naive:true);

                    var updatedCodeTextChunk = Utils.ExtractCodeTextFromBraces(updatedCodeChunkBlockStmt.GetText().ToString());
                    updatedCodeTextChunk = Utils.RemoveLeadingWhiteSpace(updatedCodeTextChunk, naive:true);

                    var precedingContextTextTokens = precedingContextTokens.Select(token => token.ValueText).ToArray();
                    var succeedingContextTextTokens = succeedingContextTokens.Select(token => token.ValueText).ToArray();

                    var result = new
                    {
                        Id = changeSha,
                        PrevCodeChunk = prevCodeTextChunk,
                        UpdatedCodeChunk = updatedCodeTextChunk,
                        PrevCodeChunkTokens = prevCodeChunkBlockStmtTextTokens,
                        UpdatedCodeChunkTokens = updatedCodeChunkBlockStmtTextTokens,
                        PrevCodeAST = prevCodeBlockJObject,
                        UpdatedCodeAST = updatedCodeBlockJObject,
                        PrecedingContext = precedingContextTextTokens,
                        SucceedingContext = succeedingContextTextTokens,
                        CommitMessage = entry["message"]
                    };

                    changeId += 1;

                    yield return result;
                }
            }
        }

        private static string changeEntryDatumToJsonString(dynamic entry, bool withCommitMessage=false)
        {
            var jsonObj = new JObject();
            jsonObj["Id"] = entry.Id;
            jsonObj["PrevCodeChunk"] = entry.PrevCodeChunk;
            jsonObj["UpdatedCodeChunk"] = entry.UpdatedCodeChunk;

            jsonObj["PrevCodeChunkTokens"] = new JArray(entry.PrevCodeChunkTokens);
            jsonObj["UpdatedCodeChunkTokens"] = new JArray(entry.UpdatedCodeChunkTokens);

            jsonObj["PrevCodeAST"] = entry.PrevCodeAST;
            jsonObj["UpdatedCodeAST"] = entry.UpdatedCodeAST;

            jsonObj["PrecedingContext"] = new JArray(entry.PrecedingContext);
            jsonObj["SucceedingContext"] = new JArray(entry.SucceedingContext);

            if (withCommitMessage)
                jsonObj["CommitMessage"] = entry.CommitMessage;

            var json = JsonConvert.SerializeObject(jsonObj, Formatting.None);

            return json;
        }

        private static IEnumerable<SyntaxToken> zeroIndexVariableNames(
            IEnumerable<SyntaxToken> tokens, IDictionary<string, string> varNameMap)
        {
            SyntaxToken generateNewTokenName(SyntaxToken token)
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    var tokenName = token.ValueText;
                    if (tokenName.StartsWith("VAR"))
                    {
                        string newTokenName;
                        if (varNameMap.ContainsKey(tokenName))
                            newTokenName = varNameMap[tokenName];
                        else
                        {
                            newTokenName = "VAR" + varNameMap.Count;
                            varNameMap[tokenName] = newTokenName;
                        }

                        return SyntaxFactory.Identifier(newTokenName);
                    }
                }

                return token;
            }

            var renamedTokens = tokens.Select(generateNewTokenName);

            return renamedTokens;
        }

        private static (BlockSyntax, BlockSyntax, IDictionary<string, string>) zeroIndexVariableNames(SyntaxNode prevCodeNode, SyntaxNode updatedCodeNode)
        {
            var varNameMap = new Dictionary<string, string>();

            SyntaxToken generateNewTokenName(SyntaxToken token)
            {
                if (token.IsKind(SyntaxKind.IdentifierToken))
                {
                    var tokenName = token.ValueText;
                    if (tokenName.StartsWith("VAR"))
                    {
                        string newTokenName;
                        if (varNameMap.ContainsKey(tokenName))
                            newTokenName = varNameMap[tokenName];
                        else
                        {
                            newTokenName = "VAR" + varNameMap.Count;
                            varNameMap[tokenName] = newTokenName;
                        }

                        return SyntaxFactory.Identifier(newTokenName);
                    }
                }
                
                return token;
            }

            var newPrevCodeNode = prevCodeNode.ReplaceTokens(prevCodeNode.DescendantTokens(), (token, _) => generateNewTokenName(token));
            var newUpdatedCodeNode = updatedCodeNode.ReplaceTokens(updatedCodeNode.DescendantTokens(), (token, _) => generateNewTokenName(token));

            return ((BlockSyntax)newPrevCodeNode, (BlockSyntax)newUpdatedCodeNode, varNameMap);
        }

        public static void DumpRevisionDataForNeuralTraining(string revisionDataFilePath, string outputFilePath, string grammarPath)
        {
            int entryProcessed = 0;
            var syntaxHelper = new JsonSyntaxTreeHelper(grammarPath);

            using(var fs = File.Open(outputFilePath, FileMode.Create))
            using(var sw = new StreamWriter(fs, Encoding.UTF8))
            {
                Stopwatch stopwatch = new Stopwatch();
                stopwatch.Start();
                foreach (var changeStrs in ReadRevisionData(revisionDataFilePath).AsParallel().Select(x =>
                    ProcessSingleRevision(x, syntaxHelper).Select(t => changeEntryDatumToJsonString(t)).ToArray()))
                {
                    entryProcessed++;
                    foreach (var changeStr in changeStrs)
                    {
                        try
                        {
                            sw.WriteLine(changeStr);
                        }
                        catch (Exception e) { }
                    }

                    if (entryProcessed % 10 == 0)
                        Console.Write($"\rEntry processed: {entryProcessed}");
                }

                stopwatch.Stop();
                Console.WriteLine();
                Console.WriteLine("Time elapsed: {0}", stopwatch.Elapsed);
            }
        }

        public static IEnumerable<string> ReadRevisionData(string revisionDataFilePath)
        {
            using (var sr = new StreamReader(revisionDataFilePath))
            {
                while (!sr.EndOfStream)
                {
                    var line = sr.ReadLine();

                    yield return line;
                }
            }
        }

        static readonly HashSet<SyntaxKind> allowedSytaxKinds = new HashSet<SyntaxKind>()
        {
            SyntaxKind.LocalDeclarationStatement,
            SyntaxKind.ExpressionStatement
        };

        private static readonly HashSet<string> keywords =
            new HashSet<string>() {"VAR0", "int", "long", "string", "float", "LITERAL", "var"};

        private static bool IsValidCodeChunkTokens(IEnumerable<string> tokens)
        {
            var validTokenCount = tokens.Count(token => !keywords.Contains(token) && !token.All(char.IsPunctuation));

            return validTokenCount > 0;
        }
    }

    public class TokenIndex
    {
        private IList<SyntaxToken> tokens;
        public Dictionary<TextSpan, (SyntaxToken SyntaxToken, int Position)> SpanToTokenIndex;

        public TokenIndex(IEnumerable<SyntaxToken> tokens)
        {
            this.tokens = new List<SyntaxToken>(tokens);
        }

        public IEnumerable<SyntaxToken> GetTokensInSpan(TextSpan querySpan)
        {
            var querySpanStart = querySpan.Start;
            var querySpanEnd = querySpan.End;

            return GetTokensInSpan(querySpanStart, querySpanEnd);
        }

        public TokenIndex WithVariableNameMap(IDictionary<string, string> variableNameMap)
        {
            var retainedTokens = tokens.Where(token => variableNameMap.ContainsKey(token.ValueText))
                .Select(token => SyntaxFactory.Token(token.LeadingTrivia, token.Kind(), token.Text, variableNameMap[token.ValueText], token.TrailingTrivia));

            return new TokenIndex(retainedTokens);
        }

        public TokenIndex InitInvertedIndex()
        {
            this.SpanToTokenIndex = new Dictionary<TextSpan, (SyntaxToken SyntaxToken, int Position)>();

            for (int i = 0; i < this.tokens.Count; i++)
            {
                var curToken = this.tokens[i];
                var key = curToken.Span;
                SpanToTokenIndex[key] = (curToken, i);
            }

            return this;
        }

        public IEnumerable<SyntaxToken> GetTokensInSpan(int start, int end)
        {
            var queryTokens = tokens.Where(token => token.SpanStart >= start).TakeWhile(token => token.Span.End <= end);

            return queryTokens;
        }

        public (SyntaxToken? SyntaxToken, int Position) GetTokenAndPositionBySpan(TextSpan span)
        {
            if (this.SpanToTokenIndex.ContainsKey(span))
            {
                return this.SpanToTokenIndex[span];
            }

            return (null, -1);
        }
    }
}
