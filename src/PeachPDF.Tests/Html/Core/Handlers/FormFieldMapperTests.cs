using System.Collections.Generic;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Handlers;

namespace PeachPDF.Tests.Html.Core.Handlers
{
    public class FormFieldMapperTests
    {
        static CssBoxFormField MakeInput(string? type = null, Dictionary<string, string>? attributes = null, string? pdfFormField = null)
        {
            var attrs = attributes ?? new Dictionary<string, string>();
            if (type != null) attrs["type"] = type;
            var box = new CssBoxFormField(null, new HtmlTag("input", true, attrs));
            if (pdfFormField != null) box.PdfFormField = pdfFormField;
            return box;
        }

        [Fact]
        public void Classify_NonFormFieldBox_ReturnsNone()
        {
            var box = new CssBox(null, new HtmlTag("div", false)) { PdfFormField = "text" };

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.None, classification.Kind);
        }

        [Fact]
        public void Classify_ExplicitNone_ReturnsNone()
        {
            var box = MakeInput(pdfFormField: "none");

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.None, classification.Kind);
        }

        [Theory]
        [InlineData(null, "Text")]
        [InlineData("text", "Text")]
        [InlineData("email", "Text")]
        [InlineData("password", "Text")]
        [InlineData("number", "Text")]
        [InlineData("checkbox", "Checkbox")]
        [InlineData("radio", "Radio")]
        [InlineData("hidden", "None")]
        [InlineData("submit", "None")]
        [InlineData("reset", "None")]
        [InlineData("button", "None")]
        [InlineData("image", "None")]
        [InlineData("file", "None")]
        public void Classify_Auto_InfersKindFromInputType(string? type, string expectedKindName)
        {
            var box = MakeInput(type);

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(expectedKindName, classification.Kind.ToString());
        }

        [Fact]
        public void Classify_Select_ReturnsSelectRegardlessOfDeclaration()
        {
            var box = new CssBoxFormField(null, new HtmlTag("select", false));

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.Select, classification.Kind);
        }

        [Fact]
        public void Classify_ExplicitOverride_ForcesKindRegardlessOfInputType()
        {
            var box = MakeInput("text", pdfFormField: "checkbox");

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.Checkbox, classification.Kind);
        }

        [Fact]
        public void Classify_Checkbox_ReadsNameValueAndChecked()
        {
            var box = MakeInput("checkbox", new Dictionary<string, string>
            {
                { "name", "agree" },
                { "value", "1" },
                { "checked", "checked" }
            });

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.Checkbox, classification.Kind);
            Assert.Equal("agree", classification.Name);
            Assert.Equal("1", classification.Value);
            Assert.True(classification.Checked);
        }

        [Fact]
        public void Classify_Radio_UncheckedByDefault()
        {
            var box = MakeInput("radio", new Dictionary<string, string> { { "name", "color" }, { "value", "red" } });

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.Radio, classification.Kind);
            Assert.False(classification.Checked);
        }

        [Fact]
        public void Classify_Text_ReadsSubSettingLonghands()
        {
            var box = MakeInput("text", new Dictionary<string, string> { { "name", "n" } });
            box.PdfFormFieldAutoFontSize = "auto";
            box.PdfFormFieldComb = "6";
            box.PdfFormFieldDoNotScroll = "auto";

            var classification = FormFieldMapper.Classify(box);

            Assert.Equal(FormFieldKind.Text, classification.Kind);
            Assert.True(classification.AutoFontSize);
            Assert.Equal(6, classification.Comb);
            Assert.True(classification.DoNotScroll);
        }

        [Fact]
        public void Classify_Select_ReadsOptionsAndDefaultSelection()
        {
            var select = new CssBoxFormField(null, new HtmlTag("select", false, new Dictionary<string, string> { { "name", "color" } }));
            var option1 = new CssBox(select, new HtmlTag("option", false, new Dictionary<string, string> { { "value", "r" } }));
            _ = new CssBox(option1, null) { Text = "Red" };
            var option2 = new CssBox(select, new HtmlTag("option", false, new Dictionary<string, string> { { "value", "g" }, { "selected", "selected" } }));
            _ = new CssBox(option2, null) { Text = "Green" };

            var classification = FormFieldMapper.Classify(select);

            Assert.Equal(FormFieldKind.Select, classification.Kind);
            Assert.Equal("color", classification.Name);
            Assert.Equal(2, classification.Options.Count);
            Assert.Equal("r", classification.Options[0].Value);
            Assert.Equal("Red", classification.Options[0].Label);
            Assert.False(classification.Options[0].Selected);
            Assert.Equal("g", classification.Options[1].Value);
            Assert.True(classification.Options[1].Selected);
        }
    }
}
