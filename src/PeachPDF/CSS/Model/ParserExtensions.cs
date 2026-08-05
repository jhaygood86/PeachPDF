#nullable disable

using PeachPDF.CSS.Conditions;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal static class ParserExtensions
    {
        private static readonly FrozenDictionary<string, Func<string, DocumentFunction>> FunctionTypes =
            new Dictionary<string, Func<string, DocumentFunction>>(StringComparer.OrdinalIgnoreCase)
            {
                {FunctionNames.Url, url => new UrlFunction(url)},
                {FunctionNames.Domain, url => new DomainFunction(url)},
                {FunctionNames.UrlPrefix, url => new UrlPrefixFunction(url)}
            }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static readonly FrozenDictionary<string, Func<IEnumerable<IConditionFunction>, IConditionFunction>>
            GroupCreators =
                new Dictionary<string, Func<IEnumerable<IConditionFunction>, IConditionFunction>>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    {Keywords.And, CreateAndCondition},
                    {Keywords.Or, CreateOrCondition}
                }.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);

        private static IConditionFunction CreateAndCondition(IEnumerable<IConditionFunction> conditions)
        {
            var andCondition = new AndCondition();
            foreach (var condition in conditions)
            {
                andCondition.AppendChild(condition);
            }
            return andCondition;
        }

        private static IConditionFunction CreateOrCondition(IEnumerable<IConditionFunction> conditions)
        {
            var orCondition = new OrCondition();
            foreach (var condition in conditions)
            {
                orCondition.AppendChild(condition);
            }
            return orCondition;
        }

        public static TokenType GetTypeFromName(this string functionName)
        {
            return FunctionTypes.TryGetValue(functionName, out _)
                ? TokenType.Url
                : TokenType.Function;
        }

        public static Func<IEnumerable<IConditionFunction>, IConditionFunction> GetCreator(this string conjunction)
        {
            GroupCreators.TryGetValue(conjunction, out var creator);
            return creator;
        }

        public static int GetCode(this ParseError code)
        {
            return (int)code;
        }

        public static bool Is(this Token token, TokenType a)
        {
            var type = token.Type;
            return type == a;
        }

        public static bool Is(this Token token, TokenType a, TokenType b)
        {
            var type = token.Type;
            return type == a || type == b;
        }

        public static bool IsNot(this Token token, TokenType a, TokenType b)
        {
            var type = token.Type;
            return type != a && type != b;
        }

        public static bool IsNot(this Token token, TokenType a, TokenType b, TokenType c)
        {
            var type = token.Type;
            return type != a && type != b && type != c;
        }

        public static bool IsDeclarationName(this Token token)
        {
            return token.Type != TokenType.EndOfFile &&
                   token.Type != TokenType.Colon &&
                   token.Type != TokenType.Whitespace &&
                   token.Type != TokenType.Comment &&
                   token.Type != TokenType.CurlyBracketOpen &&
                   token.Type != TokenType.Semicolon;
        }

        public static DocumentFunction ToDocumentFunction(this Token token)
        {
            switch (token.Type)
            {
                case TokenType.Url:
                    {
                        var functionName = ((UrlToken)token).FunctionName;
                        FunctionTypes.TryGetValue(functionName, out var creator);
                        return creator(token.Data);
                    }
                case TokenType.Function when token.Data.Isi(FunctionNames.Regexp):
                    {
                        var css = ((FunctionToken)token).ArgumentTokens.ToCssString();
                        if (css != null)
                        {
                            return new RegexpFunction(css);
                        }

                        break;
                    }
            }

            return null;
        }

        // RuleType.Unknown/RegionStyle/FontFeatureValues/CounterStyle have no Rule subtype and fall
        // through to the null default, same as before this was a lookup.
        private static readonly FrozenDictionary<RuleType, Func<StylesheetParser, Rule>> RuleFactories =
            new Dictionary<RuleType, Func<StylesheetParser, Rule>>
            {
                [RuleType.Charset] = parser => new CharsetRule(parser),
                [RuleType.Document] = parser => new DocumentRule(parser),
                [RuleType.FontFace] = parser => new FontFaceRule(parser),
                [RuleType.Import] = parser => new ImportRule(parser),
                [RuleType.Keyframe] = parser => new KeyframeRule(parser),
                [RuleType.Keyframes] = parser => new KeyframesRule(parser),
                [RuleType.Media] = parser => new MediaRule(parser),
                [RuleType.Container] = parser => new ContainerRule(parser),
                [RuleType.Namespace] = parser => new NamespaceRule(parser),
                [RuleType.Page] = parser => new PageRule(parser),
                [RuleType.Style] = parser => new StyleRule(parser),
                [RuleType.Supports] = parser => new SupportsRule(parser),
                [RuleType.Viewport] = parser => new ViewportRule(parser),
            }.ToFrozenDictionary();

        public static Rule CreateRule(this StylesheetParser parser, RuleType type) =>
            RuleFactories.TryGetValue(type, out var factory) ? factory(parser) : null;
    }
}