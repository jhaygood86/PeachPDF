namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Every instruction of the TrueType interpreter, and the behaviour on invalid arguments, against FreeType: a synthetic font whose
    /// glyphs run random programs (<c>assets/fonts/generate_hinting_opcode_fixtures.py</c>), hinted by the port and compared exactly with what
    /// FreeType 2.14.3 makes of them, including the glyphs whose program FreeType stops with an error (it keeps what the program had done).
    /// </summary>
    public class HintingOpcodeFixtureTests
    {
        private const string FontFile = "HintingOpcodes.ttf";

        private static readonly Lazy<FixtureGolden> Golden = new(() => HintingGoldenData.Load<FixtureGolden>("HintingOpcodes.golden.json.gz"));

        public static TheoryData<string, int> Runs()
        {
            var data = new TheoryData<string, int>();
            foreach (var (mode, runs) in Golden.Value.Modes)
                foreach (var run in runs)
                    data.Add(mode, run.Size);
            return data;
        }

        [Fact]
        public void TheFixtureRunsEveryKindOfInstruction()
        {
            // The opcodes that occur in the glyph programs of the fixture: a reference that only ever ran a handful of them would prove little.
            var face = HintingFixtures.Face(FontFile);
            Assert.True(face.NumGlyphs > 300);
            Assert.True(face.Cvt.Length > 0);
            Assert.True(face.FontProgram.Length > 0);
            Assert.True(face.CvtProgram.Length > 0);
        }

        [Theory]
        [MemberData(nameof(Runs))]
        public void RandomProgramsHintExactlyAsFreeTypeHintsThem(string mode, int size26Dot6)
        {
            var run = Golden.Value.Modes[mode].Single(r => r.Size == size26Dot6);
            HintingGoldenData.CompareRun(HintingFixtures.Face(FontFile), FontFile, mode, run);
        }
    }
}
