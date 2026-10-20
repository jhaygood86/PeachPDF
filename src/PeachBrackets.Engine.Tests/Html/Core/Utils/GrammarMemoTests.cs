using PeachBrackets.Html.Utils;

namespace PeachBrackets.Engine.Tests.Html.Core.Utils
{
    public class GrammarMemoTests
    {
        private readonly GrammarMemo _memo = new(50);

        [Fact]
        public void AnswersOnce_ThenRemembers()
        {
            var calls = 0;
            Func<string, bool> grammar = v => { calls++; return v.EndsWith("apple"); };

            Assert.True(_memo.Check("memo-apple", grammar));
            Assert.True(_memo.Check("memo-apple", grammar));
            Assert.False(_memo.Check("memo-pear", grammar));
            Assert.False(_memo.Check("memo-pear", grammar));

            Assert.Equal(2, calls);
        }

        [Fact]
        public void TheSameValueUnderADifferentGrammar_IsAnsweredSeparately()
        {
            Assert.True(_memo.Check("memo-shared", static _ => true));
            Assert.False(_memo.Check("memo-shared", static _ => false));
        }

        [Fact]
        public void LongValues_AreEvaluatedEachTime()
        {
            var calls = 0;
            Func<string, bool> grammar = _ => { calls++; return true; };
            var value = new string('x', 600);

            _memo.Check(value, grammar);
            _memo.Check(value, grammar);

            Assert.Equal(2, calls);
        }

        [Fact]
        public void PastTheBound_StillAnswersCorrectly()
        {
            Func<string, bool> grammar = static v => v.Length % 2 == 0;

            for (var i = 0; i < 200; i++)
                Assert.Equal(("fill-" + i).Length % 2 == 0, _memo.Check("fill-" + i, grammar));

            Assert.Equal(50, _memo.Count);
        }

        [Fact]
        public void TheSharedInstance_AnswersLikeTheGrammar()
        {
            Assert.True(GrammarMemo.Accepts("shared-ok", static v => v.EndsWith("ok")));
            Assert.False(GrammarMemo.Accepts("shared-no", static v => v.EndsWith("ok")));
        }
    }
}
