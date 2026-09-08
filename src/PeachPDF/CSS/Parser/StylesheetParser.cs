#nullable disable

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

// ReSharper disable UnusedMember.Global

namespace PeachPDF.CSS
{
    internal class StylesheetParser
    {
        internal static readonly StylesheetParser Default = new();

        public StylesheetParser(
            bool includeUnknownRules = false,
            bool includeUnknownDeclarations = false,
            bool tolerateInvalidSelectors = false,
            bool tolerateInvalidValues = false,
            bool tolerateInvalidConstraints = false,
            bool preserveComments = false,
            bool preserveDuplicateProperties = false
        )
        {
            Options = new ParserOptions
            {
                IncludeUnknownRules = includeUnknownRules,
                IncludeUnknownDeclarations = includeUnknownDeclarations,
                AllowInvalidSelectors = tolerateInvalidSelectors,
                AllowInvalidValues = tolerateInvalidValues,
                AllowInvalidConstraints = tolerateInvalidConstraints,
                PreserveComments = preserveComments,
                PreserveDuplicateProperties = preserveDuplicateProperties,
            };
        }

        internal ParserOptions Options { get; }

        public Stylesheet Parse(string content)
        {
            var source = new TextSource(content);
            return Parse(source);
        }

        public Stylesheet Parse(Stream content)
        {
            var source = new TextSource(CssStreamLoader.Load(content));
            return Parse(source);
        }

        public Task<Stylesheet> ParseAsync(string content)
        {
            return ParseAsync(content, CancellationToken.None);
        }

        public Task<Stylesheet> ParseAsync(string content, CancellationToken cancelToken)
        {
            // A string source is already fully decoded text - nothing to await, but this overload stays
            // Task-returning (rather than synchronous) to keep the existing async-call-site contract.
            var source = new TextSource(content);
            return Task.FromResult(Parse(source));
        }

        public Task<Stylesheet> ParseAsync(Stream content)
        {
            return ParseAsync(content, CancellationToken.None);
        }

        public async Task<Stylesheet> ParseAsync(Stream content, CancellationToken cancelToken)
        {
            var data = await CssStreamLoader.LoadAsync(content, null, cancelToken).ConfigureAwait(false);
            var source = new TextSource(data);
            return Parse(source);
        }

        public ISelector ParseSelector(string selectorText)
        {
            using var tokenizer = CreateTokenizer(selectorText);
            var token = tokenizer.Get();
            var creator = GetSelectorCreator();
            while (token.Type != TokenType.EndOfFile)
            {
                creator.Apply(token);
                token = tokenizer.Get();
            }

            var valid = creator.IsValid;
            var result = creator.ToPool();

            return valid || Options.AllowInvalidSelectors ? result : null;
        }

        internal KeyframeSelector ParseKeyframeSelector(string keyText)
        {
            return Parse(keyText, (b, t) => (b.CreateKeyframeSelector(ref t), t));
        }

        internal SelectorConstructor GetSelectorCreator()
        {
            var attributeSelector = AttributeSelectorFactory.Instance;
            var pseudoClassSelector = PseudoClassSelectorFactory.Instance;
            var pseudoElementSelector = new PseudoElementSelectorFactory(this);
            return Pool.NewSelectorConstructor(attributeSelector, pseudoClassSelector, pseudoElementSelector);
        }

        internal Stylesheet Parse(TextSource source)
        {
            // `source` is the caller's, not this method's: every rule/statement CreateRules produces
            // stashes it (via StylesheetComposer.CreateView -> StylesheetText) for a *lazy* `.Text` read
            // after this method returns, so it must outlive this call - TextSource itself owns nothing
            // that needs disposing (it's an already-decoded ReadOnlyMemory<char> cursor), so there is no
            // lifetime hazard here to guard against the way there was when it could be stream-backed.
            var sheet = new Stylesheet(this);
            var tokenizer = new Lexer(source);
            var start = tokenizer.GetCurrentPosition();
            var builder = new StylesheetComposer(tokenizer, this);
            var end = builder.CreateRules(sheet);
            var range = new TextRange(start, end);
            sheet.StylesheetText = new StylesheetText(range, source);
            return sheet;
        }

        internal TokenValue ParseValue(string valueText)
        {
            using var tokenizer = CreateTokenizer(valueText);
            var token = default(Token);
            var builder = new StylesheetComposer(tokenizer, this);
            var value = builder.CreateValue(ref token);
            return token.Type == TokenType.EndOfFile ? value : null;
        }

        internal Rule ParseRule(string ruleText)
        {
            return Parse(ruleText, (b, t) => b.CreateRule(t));
        }

        internal Property ParseDeclaration(string declarationText)
        {
            return Parse(declarationText, (b, t) => (b.CreateDeclaration(ref t), t));
        }

        internal List<Medium> ParseMediaList(string mediaText)
        {
            return Parse(mediaText, (b, t) => (b.CreateMedia(ref t), t));
        }

        internal IConditionFunction ParseCondition(string conditionText)
        {
            return Parse(conditionText, (b, t) => (b.CreateCondition(ref t), t));
        }

        internal List<DocumentFunction> ParseDocumentRules(string documentText)
        {
            return Parse(documentText, (b, t) => (b.CreateFunctions(ref t), t));
        }

        internal Medium ParseMedium(string mediumText)
        {
            return Parse(mediumText, (b, t) => (b.CreateMedium(ref t), t));
        }

        internal KeyframeRule ParseKeyframeRule(string ruleText)
        {
            return Parse(ruleText, (b, t) => b.CreateKeyframeRule(t));
        }

        internal void AppendDeclarations(StyleDeclaration style, string declarations)
        {
            using var tokenizer = CreateTokenizer(declarations);
            var builder = new StylesheetComposer(tokenizer, this);
            builder.FillDeclarations(style);
        }

        private T Parse<T>(string source, Func<StylesheetComposer, Token, T> create)
        {
            using var tokenizer = CreateTokenizer(source);
            var token = tokenizer.Get();
            var builder = new StylesheetComposer(tokenizer, this);
            var rule = create(builder, token);
            return tokenizer.Get().Type == TokenType.EndOfFile ? rule : default;
        }

        private T Parse<T>(string source, Func<StylesheetComposer, Token, (T Value, Token Token)> create)
        {
            using var tokenizer = CreateTokenizer(source);
            var token = tokenizer.Get();
            var builder = new StylesheetComposer(tokenizer, this);
            var pair = create(builder, token);
            return pair.Token.Type == TokenType.EndOfFile ? pair.Value : default;
        }

        private static Lexer CreateTokenizer(string sourceCode)
        {
            var source = new TextSource(sourceCode);
            return new Lexer(source);
        }
    }
}