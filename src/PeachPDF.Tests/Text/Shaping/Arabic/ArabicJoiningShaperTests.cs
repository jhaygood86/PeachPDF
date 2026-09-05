using PeachPDF.Text.Shaping.Arabic;
using Xunit;

namespace PeachPDF.Tests.Text.Shaping.Arabic
{
    /// <summary>
    /// Coverage for <see cref="ArabicJoiningShaper"/>, the ported HarfBuzz <c>arabic_joining</c> state
    /// machine (issue #533). Expected joining forms below were derived by manually tracing
    /// <see cref="ArabicJoiningStateTable"/>'s transitions by hand against each example (documented
    /// inline per test) and cross-checked against real Arabic/Syriac typographic behavior - not asserted
    /// from a black-box reference shaper, since this port has none available in-repo.
    /// </summary>
    public class ArabicJoiningShaperTests
    {
        // ARABIC LETTER BEH/TEH/YEH/ALEF - all Dual-joining except ALEF (Right-joining).
        private const int Beh = 0x0628;
        private const int Teh = 0x062A;
        private const int Yeh = 0x064A;
        private const int Alef = 0x0627;
        private const int Fatha = 0x064E; // ARABIC FATHA - a Transparent combining mark (diacritic)

        [Fact]
        public void SingleDualJoiningLetter_IsIsolated()
        {
            var forms = ArabicJoiningShaper.Resolve([Beh]);

            Assert.Equal([ArabicJoiningForm.Isol], forms);
        }

        [Fact]
        public void ThreeDualJoiningLetters_InitMediFina()
        {
            // "بيت" (bayt, "house") - BEH YEH TEH, all Dual-joining: first takes Init, middle Medi,
            // last Fina. The state machine retroactively upgrades BEH's provisional Isol (assigned when
            // only it had been seen) to Init once YEH arrives willing to join backward with it, and
            // likewise upgrades YEH's Fina to Medi once TEH arrives.
            var forms = ArabicJoiningShaper.Resolve([Beh, Yeh, Teh]);

            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Medi, ArabicJoiningForm.Fina], forms);
        }

        [Fact]
        public void TwoDualJoiningLetters_InitFina()
        {
            var forms = ArabicJoiningShaper.Resolve([Beh, Teh]);

            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Fina], forms);
        }

        [Fact]
        public void RightJoiningLetter_DoesNotExtendJoiningForward()
        {
            // ALEF (Right-joining: joins with a PRECEDING character, never a following one) then BEH.
            // BEH must NOT become Init here - ALEF never offers a forward join, so BEH starts fresh as
            // if nothing preceded it, matching real Arabic typography ("اب" does not ligate).
            var forms = ArabicJoiningShaper.Resolve([Alef, Beh]);

            Assert.Equal([ArabicJoiningForm.Isol, ArabicJoiningForm.Isol], forms);
        }

        [Fact]
        public void DualJoiningThenRightJoining_JoinsNormally()
        {
            // "با" (BEH then ALEF) - ALEF's Right-joining nature means it DOES join backward with a
            // preceding letter willing to join forward, so this ligates normally: BEH=Init, ALEF=Fina.
            var forms = ArabicJoiningShaper.Resolve([Beh, Alef]);

            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Fina], forms);
        }

        [Fact]
        public void TransparentCombiningMark_DoesNotBreakJoining()
        {
            // BEH + FATHA (a diacritic, Transparent) + TEH - the diacritic must not interrupt the join
            // between BEH and TEH, and itself resolves to None (no positional feature requested).
            var forms = ArabicJoiningShaper.Resolve([Beh, Fatha, Teh]);

            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.None, ArabicJoiningForm.Fina], forms);
        }

        [Fact]
        public void NonJoiningScript_EveryCharacterResolvesToNone()
        {
            // Plain Latin text - Joining_Type U (Non_Joining) for every character, so nothing joins and
            // no positional feature is ever requested. "AB".
            var forms = ArabicJoiningShaper.Resolve([0x0041, 0x0042]);

            Assert.Equal([ArabicJoiningForm.None, ArabicJoiningForm.None], forms);
        }

        [Fact]
        public void Empty_ReturnsEmpty()
        {
            var forms = ArabicJoiningShaper.Resolve([]);

            Assert.Empty(forms);
        }

        // --- Syriac Joining_Group special cases (ALAPH / DALATH_RISH) ---

        private const int Alaph = 0x0710; // SYRIAC LETTER ALAPH - Joining_Type R, but its own Joining_Group column
        private const int Beth = 0x0712; // SYRIAC LETTER BETH - Dual-joining, an ordinary D-type letter
        private const int Dalath = 0x0715; // SYRIAC LETTER DALATH - Joining_Group DALATH_RISH

        [Fact]
        public void Alaph_Alone_IsIsolated()
        {
            var forms = ArabicJoiningShaper.Resolve([Alaph]);

            Assert.Equal([ArabicJoiningForm.Isol], forms);
        }

        [Fact]
        public void DualJoiningThenAlaph_JoinsNormally()
        {
            var forms = ArabicJoiningShaper.Resolve([Beth, Alaph]);

            Assert.Equal([ArabicJoiningForm.Init, ArabicJoiningForm.Fina], forms);
        }

        [Fact]
        public void AlaphThenAlaph_SecondTakesFin2_TheAlaphSpecificFinalForm()
        {
            // ALAPH's own state-4/5 transitions produce Fin2 rather than plain Fina - a real, Syriac-
            // specific behavior distinct from ordinary Arabic joining, per ArabicJoiningForm's own doc
            // comment on why Fin2/Fin3/Med2 can't collapse into Fina/Medi.
            var forms = ArabicJoiningShaper.Resolve([Alaph, Alaph]);

            Assert.Equal([ArabicJoiningForm.Isol, ArabicJoiningForm.Fin2], forms);
        }

        [Fact]
        public void DalathRish_NeverExtendsJoiningForward()
        {
            // DALATH is Joining_Group DALATH_RISH - like ALEF, it doesn't offer a forward join, so a
            // second DALATH after it stays Isol rather than becoming Medi/Fina.
            var forms = ArabicJoiningShaper.Resolve([Dalath, Dalath]);

            Assert.Equal([ArabicJoiningForm.Isol, ArabicJoiningForm.Isol], forms);
        }
    }
}
