using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DumpCommitData
{
    public enum ChangeType
    {
        NONE = 1,
        ADD,
        DELETE,
        MODIFY
    }

    public struct ChangeAndContextSpan
    {
        public readonly TextSpan ChangeSpan;
        public readonly TextSpan ContextSpan;

        public ChangeAndContextSpan(TextSpan changeSpan, TextSpan contextSpan)
        {
            ChangeSpan = changeSpan;
            ContextSpan = contextSpan;
        }

        public TextSpan SpanOfPrecedingContext => new TextSpan(ContextSpan.Start, ChangeSpan.Start - ContextSpan.Start);
        public TextSpan SpanOfSucceedingContext => new TextSpan(ChangeSpan.End, ContextSpan.End - ChangeSpan.End);

        public ChangeAndContextSpan Merge(ChangeAndContextSpan other)
        {
            Debug.Assert(ContextSpan.OverlapsWith(other.ContextSpan));
            return new ChangeAndContextSpan(
                    new TextSpan(Math.Min(ChangeSpan.Start, other.ChangeSpan.Start),
                                Math.Max(ChangeSpan.End, other.ChangeSpan.End)),
                    new TextSpan(Math.Min(ContextSpan.Start, other.ContextSpan.Start),
                                Math.Max(ContextSpan.End, other.ContextSpan.End))
                );
        }

        public override string ToString()
        {
            return $"Changed Span: ({ChangeSpan.Start}, {ChangeSpan.End}) in context ({ContextSpan.Start}, {ContextSpan.End})";
        }
    }

    public struct ChangeSample
    {
        public readonly ChangeAndContextSpan BeforeSpan;
        public readonly ChangeAndContextSpan AfterSpan;

        public static ChangeSample EMPTY = new ChangeSample(
            new ChangeAndContextSpan(new TextSpan(0, 0), new TextSpan(0, 0)),
            new ChangeAndContextSpan(new TextSpan(0, 0), new TextSpan(0, 0)));

        public ChangeSample(ChangeAndContextSpan before, ChangeAndContextSpan after)
        {
            BeforeSpan = before;
            AfterSpan = after;
        }

        public ChangeSample Merge(ChangeSample other)
        {
            return new ChangeSample(
                    BeforeSpan.Merge(other.BeforeSpan),
                    AfterSpan.Merge(other.AfterSpan));
        }

        public override string ToString()
        {
            return $"From {BeforeSpan} to {AfterSpan}";
        }
    }

    public class SpanMap
    {
        private List<(TextSpan beforeSpan, TextSpan afterSpan, ChangeType changeType)> _spanMap;

        private static TextSpan GetSpan(int from, int to) => new TextSpan(from, to - from);

        public SpanMap(SyntaxTree beforeTree, SyntaxTree afterTree)
        {
            var changes = afterTree.GetChanges(beforeTree);
            _spanMap = new List<(TextSpan beforeSpan, TextSpan afterSpan, ChangeType changeType)>();
            int beforeSpanPos = 0;
            int afterSpanPos = 0;

            foreach (var change in changes.OrderBy(c => c.Span))
            {
                // Is there any unchanged span since the last change?
                if (change.Span.Start > beforeSpanPos)
                {
                    var beforeSpan = GetSpan(beforeSpanPos, change.Span.Start);
                    var afterSpan = new TextSpan(afterSpanPos, beforeSpan.Length);
                    _spanMap.Add((beforeSpan, afterSpan, ChangeType.NONE));
                    beforeSpanPos = change.Span.Start;
                    afterSpanPos = afterSpanPos + beforeSpan.Length;
                }
                // Now add the change...
                ChangeType changeType;
                if (change.Span.Length == 0) changeType = ChangeType.ADD;
                else if (change.NewText.Length == 0) changeType = ChangeType.DELETE;
                else changeType = ChangeType.MODIFY;
                _spanMap.Add((change.Span, new TextSpan(afterSpanPos, change.NewText.Length), changeType));
                beforeSpanPos = change.Span.End;
                afterSpanPos += change.NewText.Length;
            }
            // Add the final span, if any
            if (beforeTree.Length != beforeSpanPos || afterSpanPos != afterTree.Length)
            {
                _spanMap.Add((GetSpan(beforeSpanPos, beforeTree.Length), GetSpan(afterSpanPos, afterTree.Length), ChangeType.NONE));
            }
        }

        private static bool OverlapsWithChangedSpans(ISet<TextSpan> targetSpans, IEnumerable<TextSpan> otherSpans)
        {
            return otherSpans.Any(s => targetSpans.Any(t => t.OverlapsWith(s)));
        }

        public IEnumerable<(TextSpan Before, TextSpan After)> GetAllSimpleChangeSpans()
        {
            return _spanMap.Where(s => s.changeType != ChangeType.NONE).Select(s => (s.beforeSpan, s.afterSpan));
        }

        public bool ContextOverlapsWithChange(ChangeSample changeSample)
        {
            return OverlapsWithChangedSpans(
                    new HashSet<TextSpan> { changeSample.BeforeSpan.SpanOfPrecedingContext, changeSample.BeforeSpan.SpanOfSucceedingContext },
                    _spanMap.Where(ch => ch.changeType != ChangeType.NONE).Select(ch => ch.beforeSpan)) ||
                OverlapsWithChangedSpans(
                    new HashSet<TextSpan> { changeSample.AfterSpan.SpanOfPrecedingContext, changeSample.AfterSpan.SpanOfSucceedingContext },
                _spanMap.Where(ch => ch.changeType != ChangeType.NONE).Select(ch => ch.afterSpan));
        }

        internal IEnumerable<(TextSpan Before, TextSpan After)> GetExpandedChanges(
            TextSpan beforeSpan, TextSpan afterSpan, Func<TextSpan, TextSpan, bool> expandFurther)
        {
            // Find index of change
            int i = 0;
            for (; i < _spanMap.Count; i++)
            {
                if (_spanMap[i].beforeSpan == beforeSpan && _spanMap[i].afterSpan == afterSpan)
                {
                    break;
                }
            }
            if (i == _spanMap.Count)
            {
                throw new ApplicationException("Input spans not in span map.");
            }

            var yieldedChanges = new HashSet<(int FromIdx, int ToIdx)>();
            var changesToExpand = new Stack<(int FromIdx, int ToIdx)>();
            changesToExpand.Push((i, i));

            int? NextChangeLocation(int j)
            {
                if (j >= _spanMap.Count - 1)
                {
                    return null;
                }
                foreach (var pos in Enumerable.Range(j + 1, _spanMap.Count - j - 1).Where(k => _spanMap[k].changeType != ChangeType.NONE)) {
                    return pos;
                }
                return null;
            };
            int? PreviousChangeLocation(int j)
            {
                if (j <= 0) return null;
                foreach (var pos in Enumerable.Range(0, j).Reverse().Where(k => _spanMap[k].changeType != ChangeType.NONE))
                {
                    return pos;
                }
                return null;
            }

            while (changesToExpand.Count > 0)
            {
                var currentChange = changesToExpand.Pop();
                if (yieldedChanges.Contains(currentChange)) continue;

                var currentBeforeSpan = new TextSpan(_spanMap[currentChange.FromIdx].beforeSpan.Start,
                                              _spanMap[currentChange.ToIdx].beforeSpan.End - _spanMap[currentChange.FromIdx].beforeSpan.Start);
                var currentAfterSpan = new TextSpan(_spanMap[currentChange.FromIdx].afterSpan.Start,
                                              _spanMap[currentChange.ToIdx].afterSpan.End - _spanMap[currentChange.FromIdx].afterSpan.Start);
                if (!expandFurther(currentBeforeSpan, currentAfterSpan)) continue;

                yield return (currentBeforeSpan, currentAfterSpan);
                yieldedChanges.Add(currentChange);

                var previousChangeLocation = PreviousChangeLocation(currentChange.FromIdx);
                if (previousChangeLocation.HasValue) changesToExpand.Push((previousChangeLocation.Value, currentChange.ToIdx));

                var nextChangeLocation = NextChangeLocation(currentChange.ToIdx);
                if (nextChangeLocation.HasValue) changesToExpand.Push((currentChange.FromIdx, nextChangeLocation.Value));
            }
        }
    }

    class DiffInfo
    {
        private const int MAX_NUM_LINES_CHANGED = 5;
        private const int NUM_LINES_UNCHANGED_CONTEXT = 2;

        private static bool IsChangeTooBig(TextSpan changedSpan, SyntaxTree tree)
        {
            var lineSpan = tree.GetLineSpan(changedSpan);
            return lineSpan.EndLinePosition.Line - lineSpan.StartLinePosition.Line > MAX_NUM_LINES_CHANGED;
        }

        private static TextSpan GetContextFor(TextSpan changedSpan, SyntaxTree tree)
        {
            var changeLineSpan = tree.GetLineSpan(changedSpan);
            var treeText = tree.GetText();
            var startSpan = treeText.Lines[Math.Max(0, changeLineSpan.StartLinePosition.Line - NUM_LINES_UNCHANGED_CONTEXT)].Start;
            var endSpan = treeText.Lines[Math.Min(treeText.Lines.Count - 1, changeLineSpan.EndLinePosition.Line + NUM_LINES_UNCHANGED_CONTEXT)].End;
            return new TextSpan(startSpan, endSpan - startSpan);
        }


        public static IEnumerable<ChangeSample> GetChangesWithContext(SyntaxTree beforeTree, SyntaxTree afterTree)
        {
            var spanMap = new SpanMap(beforeTree, afterTree);
            var seenSpans = new HashSet<(TextSpan Before, TextSpan After)>();

            foreach (var (beforeSpan, afterSpan) in spanMap.GetAllSimpleChangeSpans())
            {
                foreach (var expanedChange in spanMap.GetExpandedChanges(beforeSpan, afterSpan,
                    (b, a) => !IsChangeTooBig(b, beforeTree) && !IsChangeTooBig(a, afterTree)))
                {
                    if (seenSpans.Contains(expanedChange)) continue;

                    var baseChangeSample = new ChangeSample(
                        new ChangeAndContextSpan(expanedChange.Before, GetContextFor(expanedChange.Before, beforeTree)),
                        new ChangeAndContextSpan(expanedChange.After, GetContextFor(expanedChange.After, afterTree))
                    );

                    if (!spanMap.ContextOverlapsWithChange(baseChangeSample))
                    {
                        yield return baseChangeSample;
                        seenSpans.Add(expanedChange);
                        break;
                    }
                }
            }
        }

        public static void Main(string[] args)
        {
            var beforeAst = CSharpSyntaxTree.ParseText(File.ReadAllText(args[0]));
            var afterAst = CSharpSyntaxTree.ParseText(File.ReadAllText(args[1]));

            foreach (var change in GetChangesWithContext(beforeAst, afterAst))
            {
                Console.WriteLine("=====================");
                Console.WriteLine(change);
                Console.WriteLine($"From {beforeAst.GetMappedLineSpan(change.BeforeSpan.ChangeSpan)} to {beforeAst.GetMappedLineSpan(change.AfterSpan.ChangeSpan)}");
            }
        }
    }
}