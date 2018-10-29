using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace DumpCommitData
{
    class Utils
    {
        public static string RemoveLeadingWhiteSpace(string code, bool naive=false)
        {
            var lines = code.Split('\n');

            IEnumerable<string> newLines;
            if (naive)
            {
                newLines = lines.Select(line => line.Substring(line.TakeWhile(c => c == ' ').Count()));
            }
            else
            {
                var numSpacesToRemove = lines.Select(line => line.TakeWhile(c => c == ' ').Count()).Min();
                newLines = lines.Select(line => line.Substring(numSpacesToRemove));
            }
            
            return string.Join('\n', newLines);
        }

        public static (string[], string[], string[], string[]) ZeroIndexAnonymizedVariables(
            IList<string> prevCodeTokens,
            IList<string> updatedCodeTokens, IList<string> precedingContextTokens, IList<string> succeedingContextTokens)
        {
            var varNameMap = new Dictionary<string, string>();

            string ProcessToken(string token)
            {
                if (token.StartsWith("VAR"))
                {
                    if (varNameMap.ContainsKey(token))
                        return varNameMap[token];
                    else
                    {
                        var newTokenName = "VAR" + varNameMap.Count;
                        varNameMap[token] = newTokenName;
                        return newTokenName;
                    }
                }

                return token;
            }

            var newPrecedingContextTokens = precedingContextTokens.Select(ProcessToken).ToArray();
            var newPrevCodeTokens = prevCodeTokens.Select(ProcessToken).ToArray();
            var newUpdatedCodeTokens = updatedCodeTokens.Select(ProcessToken).ToArray();
            var newSucceedingContextTokens = succeedingContextTokens.Select(ProcessToken).ToArray();

            return (newPrevCodeTokens, newUpdatedCodeTokens, newPrecedingContextTokens, newSucceedingContextTokens);

        }

        internal static string ExtractCodeTextFromBraces(string blockStmtCode)
        {
            if (blockStmtCode[0] != '{')
                throw new Exception("block stmt must start with '{'");

            if (blockStmtCode[blockStmtCode.Length - 1] != '}')
                throw new Exception("block stmt must end with '}'");

            blockStmtCode = blockStmtCode.Substring(1, blockStmtCode.Length - 2);

            int beginPtr = 0;
            while (blockStmtCode[beginPtr] == '\r' || blockStmtCode[beginPtr] == '\n')
                beginPtr++;

            blockStmtCode = blockStmtCode.Substring(beginPtr);
            blockStmtCode = blockStmtCode.TrimEnd();

            return blockStmtCode;
        }
    }
}
