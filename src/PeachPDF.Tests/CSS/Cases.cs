using PeachPDF.CSS;

namespace PeachPDF.Tests.CSS;

public class CssCasesTests : CssConstructionFunctions
{
    private static Stylesheet ParseSheet(string text)
    {
        return ParseStyleSheet(text, true, true, true, true, true);
    }

    [Fact]
    public void StyleSheetAtNamespace()
    {
        var sheet = ParseSheet(@"@namespace svg ""http://www.w3.org/2000/svg"";");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetCharsetLinebreak()
    {
        var sheet = ParseSheet(@"@charset
    ""UTF-8""
    ;");
        Assert.Equal(1, sheet.Rules.Length);

        foreach (var rule in sheet.Rules)
            Assert.Equal("UTF-8", ((CharsetRule)rule).CharacterSet);
    }

    [Fact]
    public void StyleSheetCharset()
    {
        var sheet = ParseSheet(@"@charset ""UTF-8"";       /* Set the encoding of the style sheet to Unicode UTF-8 */
@charset 'iso-8859-15'; /* Set the encoding of the style sheet to Latin-9 (Western European languages, with euro sign) */
");
        Assert.Equal(2, sheet.Rules.Length);
        Assert.Equal("UTF-8", ((CharsetRule)sheet.Rules[0]).CharacterSet);
        Assert.Equal("iso-8859-15", ((CharsetRule)sheet.Rules[1]).CharacterSet);
    }

    [Fact]
    public void StyleSheetColonSpace()
    {
        var sheet = ParseSheet(@"a {
    margin  : auto;
    padding : 0;
}");
        Assert.Equal(1, sheet.Rules.Length);

        foreach (var rule in sheet.Rules)
        {
            Assert.Equal("a", ((StyleRule)rule).SelectorText);
            Assert.Equal("auto", ((StyleRule)rule).Style["margin-top"]);
            Assert.Equal("auto", ((StyleRule)rule).Style["margin-right"]);
            Assert.Equal("auto", ((StyleRule)rule).Style["margin-bottom"]);
            Assert.Equal("auto", ((StyleRule)rule).Style["margin-left"]);
            Assert.Equal("0", ((StyleRule)rule).Style["padding-top"]);
            Assert.Equal("0", ((StyleRule)rule).Style["padding-right"]);
            Assert.Equal("0", ((StyleRule)rule).Style["padding-bottom"]);
            Assert.Equal("0", ((StyleRule)rule).Style["padding-left"]);
        }
    }

    [Fact]
    public void StyleSheetCommaAttribute()
    {
        var sheet = ParseSheet(@".foo[bar=""baz,quz""] {
  foobar: 123;
}

.bar,
#bar[baz=""qux,foo""],
#qux {
  foobar: 456;
}

.baz[qux="",foo""],
.baz[qux=""foo,""],
.baz[qux=""foo,bar,baz""],
.baz[qux="",foo,bar,baz,""],
.baz[qux="" , foo , bar , baz , ""] {
  foobar: 789;
}

.qux[foo='bar,baz'],
.qux[bar=""baz,foo""],
#qux[foo=""foobar""],
#qux[foo=',bar,baz, '] {
  foobar: 012;
}

#foo[foo=""""],
#foo[bar="" ""],
#foo[bar="",""],
#foo[bar="", ""],
#foo[bar="" ,""],
#foo[bar="" , ""],
#foo[baz=''],
#foo[qux=' '],
#foo[qux=','],
#foo[qux=', '],
#foo[qux=' ,'],
#foo[qux=' , '] {
  foobar: 345;
}");
        Assert.Equal(5, sheet.Rules.Length);

        Assert.Equal(@".foo[bar=""baz,quz""]", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal("123", ((StyleRule)sheet.Rules[0]).Style["foobar"]);

        Assert.Equal(@".bar,#bar[baz=""qux,foo""],#qux", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal("456", ((StyleRule)sheet.Rules[1]).Style["foobar"]);

        Assert.Equal(
            @".baz[qux="",foo""],.baz[qux=""foo,""],.baz[qux=""foo,bar,baz""],.baz[qux="",foo,bar,baz,""],.baz[qux="" , foo , bar , baz , ""]",
            ((StyleRule)sheet.Rules[2]).SelectorText);
        Assert.Equal("789", ((StyleRule)sheet.Rules[2]).Style["foobar"]);

        Assert.Equal(@".qux[foo=""bar,baz""],.qux[bar=""baz,foo""],#qux[foo=""foobar""],#qux[foo="",bar,baz, ""]",
            ((StyleRule)sheet.Rules[3]).SelectorText);
        Assert.Equal("012", ((StyleRule)sheet.Rules[3]).Style["foobar"]);

        Assert.Equal(
            @"#foo[foo=""""],#foo[bar="" ""],#foo[bar="",""],#foo[bar="", ""],#foo[bar="" ,""],#foo[bar="" , ""],#foo[baz=""""],#foo[qux="" ""],#foo[qux="",""],#foo[qux="", ""],#foo[qux="" ,""],#foo[qux="" , ""]",
            ((StyleRule)sheet.Rules[4]).SelectorText);
        Assert.Equal("345", ((StyleRule)sheet.Rules[4]).Style["foobar"]);
    }

    [Fact]
    public void StyleSheetCommaSelectorFunction()
    {
        var sheet = ParseSheet(@".foo:matches(.bar,.baz),
.foo:matches(.bar, .baz),
.foo:matches(.bar , .baz),
.foo:matches(.bar ,.baz) {
  prop: value;
}

.foo:matches(.bar,.baz,.foobar),
.foo:matches(.bar, .baz,),
.foo:matches(,.bar , .baz) {
  anotherprop: anothervalue;
}");
        Assert.Equal(2, sheet.Rules.Length);

        Assert.Equal(".foo:matches(.bar,.baz),.foo:matches(.bar,.baz),.foo:matches(.bar,.baz),.foo:matches(.bar,.baz)",
            ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal("value", ((StyleRule)sheet.Rules[0]).Style["prop"]);

        Assert.Equal(@".foo:matches(.bar,.baz,.foobar),
.foo:matches(.bar, .baz,),
.foo:matches(,.bar , .baz) ", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal("anothervalue", ((StyleRule)sheet.Rules[1]).Style["anotherprop"]);
    }

    [Fact]
    public void StyleSheetCommentIn()
    {
        var sheet = ParseSheet(@"a {
    color/**/: 12px;
    padding/*4815162342*/: 1px /**/ 2px /*13*/ 3px;
    border/*\**/: solid; border-top/*\**/: none\9;
}");
        Assert.Equal(1, sheet.Rules.Length);
        var rule = sheet.Rules[0];

        System.Diagnostics.Debug.WriteLine("border-top-style: " + ((StyleRule)rule).Style["border-top-style"]);
        System.Diagnostics.Debug.WriteLine("border-right-style: " + ((StyleRule)rule).Style["border-right-style"]);
        System.Diagnostics.Debug.WriteLine("border-bottom-style: " + ((StyleRule)rule).Style["border-bottom-style"]);
        System.Diagnostics.Debug.WriteLine("border-left-style: " + ((StyleRule)rule).Style["border-left-style"]);

        Assert.Equal("a", ((StyleRule)rule).SelectorText);
        Assert.Equal("12px", ((StyleRule)rule).Style["color"]);
        Assert.Equal("1px", ((StyleRule)rule).Style["padding-top"]);
        Assert.Equal("2px", ((StyleRule)rule).Style["padding-right"]);
        Assert.Equal("3px", ((StyleRule)rule).Style["padding-bottom"]);
        Assert.Equal("2px", ((StyleRule)rule).Style["padding-left"]);
        Assert.Equal("solid", ((StyleRule)rule).Style["border-top-style"]);
        Assert.Equal("solid", ((StyleRule)rule).Style["border-right-style"]);
        Assert.Equal("solid", ((StyleRule)rule).Style["border-bottom-style"]);
        Assert.Equal("solid", ((StyleRule)rule).Style["border-left-style"]);
        Assert.Equal("none\t", ((StyleRule)rule).Style["border-top"]);
    }

    [Fact]
    public void StyleSheetCommentUrl()
    {
        var sheet = ParseSheet(@"/* http://foo.com/bar/baz.html */
/**/

foo { /*/*/
  /* something */
  bar: baz; /* http://foo.com/bar/baz.html */
}");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("foo", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal("baz", ((StyleRule)sheet.Rules[0]).Style["bar"]);
    }

    [Fact]
    public void StyleSheetComment()
    {
        var sheet = ParseSheet(@"/* 1 */

head, /* footer, */body/*, nav */ { /* 2 */
  /* 3 */
  /**/foo: 'bar';
  /* 4 */
} /* 5 */

/* 6 */");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("head,body", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""bar""", ((StyleRule)sheet.Rules[0]).Style["foo"]);
    }

    [Fact]
    public void StyleSheetCustomMediaLinebreak()
    {
        var sheet = ParseSheet(@"@custom-media
    --test
    (min-width: 200px)
;");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetCustomMedia()
    {
        var sheet = ParseSheet(@"@custom-media --narrow-window (max-width: 30em);
@custom-media --wide-window screen and (min-width: 40em);
");
        Assert.Equal(2, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetDocumentLinebreak()
    {
        var sheet = ParseSheet(@"@document
    url-prefix()
    {

        .test {
            color: blue;
        }

    }");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetDocument()
    {
        var sheet = ParseSheet(@"@-moz-document url-prefix() {
  /* ui above */
  .ui-select .ui-btn select {
    /* ui inside */
    opacity:.0001
  }

  .icon-spin {
    height: .9em;
  }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetEmpty()
    {
        var sheet = ParseSheet("");
        Assert.Equal(0, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetEscapes()
    {
        var sheet = ParseSheet(@"/* tests compressed for easy testing */
/* http://mathiasbynens.be/notes/css-escapes */
/* will match elements with class="":`("" */
.\3A \`\({}
/* will match elements with class=""1a2b3c"" */
.\31 a2b3c{}
/* will match the element with id=""#fake-id"" */
#\#fake-id{}
/* will match the element with id=""---"" */
#\---{}
/* will match the element with id=""-a-b-c-"" */
#-a-b-c-{}
/* will match the element with id=""©"" */
#©{}
/* More tests from http://mathiasbynens.be/demo/html5-id */
html{font:1.2em/1.6 Arial;}
code{font-family:Consolas;}
li code{background:rgba(255, 255, 255, .5);padding:.3em;}
li{background:orange;}
#♥{background:lime;}
#©{background:lime;}
#“‘’”{background:lime;}
#☺☃{background:lime;}
#⌘⌥{background:lime;}
#𝄞♪♩♫♬{background:lime;}
#\?{background:lime;}
#\@{background:lime;}
#\.{background:lime;}
#\3A \){background:lime;}
#\3A \`\({background:lime;}
#\31 23{background:lime;}
#\31 a2b3c{background:lime;}
#\<p\>{background:lime;}
#\<\>\<\<\<\>\>\<\>{background:lime;}
#\+\+\+\+\+\+\+\+\+\+\[\>\+\+\+\+\+\+\+\>\+\+\+\+\+\+\+\+\+\+\>\+\+\+\>\+\<\<\<\<\-\]\>\+\+\.\>\+\.\+\+\+\+\+\+\+\.\.\+\+\+\.\>\+\+\.\<\<\+\+\+\+\+\+\+\+\+\+\+\+\+\+\+\.\>\.\+\+\+\.\-\-\-\-\-\-\.\-\-\-\-\-\-\-\-\.\>\+\.\>\.{background:lime;}
#\#{background:lime;}
#\#\#{background:lime;}
#\#\.\#\.\#{background:lime;}
#\_{background:lime;}
#\.fake\-class{background:lime;}
#foo\.bar{background:lime;}
#\3A hover{background:lime;}
#\3A hover\3A focus\3A active\3A focus\-visible\3A focus\-within{background:lime;}
#\[attr\=value\]{background:lime;}
#f\/o\/o{background:lime;}
#f\\o\\o{background:lime;}
#f\*o\*o{background:lime;}
#f\!o\!o{background:lime;}
#f\'o\'o{background:lime;}
#f\~o\~o{background:lime;}
#f\+o\+o{background:lime;}

/* css-parse does not yet pass this test */
/*#\{\}{background:lime;}*/");
        Assert.Equal(42, sheet.Rules.Length);

        Assert.Equal(".:`(", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(".1a2b3c", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal("##fake-id", ((StyleRule)sheet.Rules[2]).SelectorText);
        Assert.Equal("#---", ((StyleRule)sheet.Rules[3]).SelectorText);
        Assert.Equal("#-a-b-c-", ((StyleRule)sheet.Rules[4]).SelectorText);
        Assert.Equal(@"#©", ((StyleRule)sheet.Rules[5]).SelectorText);
        Assert.Equal("html", ((StyleRule)sheet.Rules[6]).SelectorText);
        Assert.Equal("Arial", ((StyleRule)sheet.Rules[6]).Style["font-family"]);
        Assert.Equal("1.2em", ((StyleRule)sheet.Rules[6]).Style["font-size"]);
        Assert.Equal("code", ((StyleRule)sheet.Rules[7]).SelectorText);
        Assert.Equal("Consolas", ((StyleRule)sheet.Rules[7]).Style["font-family"]);
        Assert.Equal("li code", ((StyleRule)sheet.Rules[8]).SelectorText);
        Assert.Equal("rgba(255, 255, 255, 0.5)", ((StyleRule)sheet.Rules[8]).Style["background-color"]);
        Assert.Equal("0.3em", ((StyleRule)sheet.Rules[8]).Style["padding-top"]);
        Assert.Equal("0.3em", ((StyleRule)sheet.Rules[8]).Style["padding-right"]);
        Assert.Equal("0.3em", ((StyleRule)sheet.Rules[8]).Style["padding-bottom"]);
        Assert.Equal("0.3em", ((StyleRule)sheet.Rules[8]).Style["padding-left"]);
        Assert.Equal("li", ((StyleRule)sheet.Rules[9]).SelectorText);
        Assert.Equal("rgb(255, 165, 0)", ((StyleRule)sheet.Rules[9]).Style["background-color"]);
        Assert.Equal(@"#♥", ((StyleRule)sheet.Rules[10]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[10]).Style["background-color"]);
        Assert.Equal(@"#©", ((StyleRule)sheet.Rules[11]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[11]).Style["background-color"]);
        Assert.Equal("#“‘’”", ((StyleRule)sheet.Rules[12]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[12]).Style["background-color"]);
        Assert.Equal(@"#☺☃", ((StyleRule)sheet.Rules[13]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[13]).Style["background-color"]);
        Assert.Equal(@"#⌘⌥", ((StyleRule)sheet.Rules[14]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[14]).Style["background-color"]);
        Assert.Equal(@"#𝄞♪♩♫♬", ((StyleRule)sheet.Rules[15]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[15]).Style["background-color"]);
        Assert.Equal("#?", ((StyleRule)sheet.Rules[16]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[16]).Style["background-color"]);
        Assert.Equal("#@", ((StyleRule)sheet.Rules[17]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[17]).Style["background-color"]);
        Assert.Equal("#.", ((StyleRule)sheet.Rules[18]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[18]).Style["background-color"]);
        Assert.Equal("#:)", ((StyleRule)sheet.Rules[19]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[19]).Style["background-color"]);
        Assert.Equal("#:`(", ((StyleRule)sheet.Rules[20]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[20]).Style["background-color"]);
        Assert.Equal("#123", ((StyleRule)sheet.Rules[21]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[21]).Style["background-color"]);
        Assert.Equal("#1a2b3c", ((StyleRule)sheet.Rules[22]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[22]).Style["background-color"]);
        Assert.Equal("#<p>", ((StyleRule)sheet.Rules[23]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[23]).Style["background-color"]);
        Assert.Equal("#<><<<>><>", ((StyleRule)sheet.Rules[24]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[24]).Style["background-color"]);
        Assert.Equal(
            "#++++++++++[>+++++++>++++++++++>+++>+<<<<-]>++.>+.+++++++..+++.>++.<<+++++++++++++++.>.+++.------.--------.>+.>.",
            ((StyleRule)sheet.Rules[25]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[25]).Style["background-color"]);
        Assert.Equal("##", ((StyleRule)sheet.Rules[26]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[26]).Style["background-color"]);
        Assert.Equal("###", ((StyleRule)sheet.Rules[27]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[27]).Style["background-color"]);
        Assert.Equal("##.#.#", ((StyleRule)sheet.Rules[28]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[28]).Style["background-color"]);
        Assert.Equal("#_", ((StyleRule)sheet.Rules[29]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[29]).Style["background-color"]);
        Assert.Equal("#.fake-class", ((StyleRule)sheet.Rules[30]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[30]).Style["background-color"]);
        Assert.Equal("#foo.bar", ((StyleRule)sheet.Rules[31]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[31]).Style["background-color"]);
        Assert.Equal("#:hover", ((StyleRule)sheet.Rules[32]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[32]).Style["background-color"]);
        Assert.Equal("#:hover:focus:active:focus-visible:focus-within", ((StyleRule)sheet.Rules[33]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[33]).Style["background-color"]);
        Assert.Equal("#[attr=value]", ((StyleRule)sheet.Rules[34]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[34]).Style["background-color"]);
        Assert.Equal("#f/o/o", ((StyleRule)sheet.Rules[35]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[35]).Style["background-color"]);
        Assert.Equal(@"#f\o\o", ((StyleRule)sheet.Rules[36]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[36]).Style["background-color"]);
        Assert.Equal("#f*o*o", ((StyleRule)sheet.Rules[37]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[37]).Style["background-color"]);
        Assert.Equal("#f!o!o", ((StyleRule)sheet.Rules[38]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[38]).Style["background-color"]);
        Assert.Equal("#f'o'o", ((StyleRule)sheet.Rules[39]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[39]).Style["background-color"]);
        Assert.Equal("#f~o~o", ((StyleRule)sheet.Rules[40]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[40]).Style["background-color"]);
        Assert.Equal("#f+o+o", ((StyleRule)sheet.Rules[41]).SelectorText);
        Assert.Equal("rgb(0, 255, 0)", ((StyleRule)sheet.Rules[41]).Style["background-color"]);
    }

    [Fact]
    public void StyleSheetFontFaceLinebreak()
    {
        var sheet = ParseSheet(@"@font-face

       {
  font-family: ""Bitstream Vera Serif Bold"";
  src: url(""http://developer.mozilla.org/@api/deki/files/2934/=VeraSeBd.ttf"");
}

body {
  font-family: ""Bitstream Vera Serif Bold"", serif;
}");
        Assert.Equal(2, sheet.Rules.Length);

        Assert.Equal("body", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal(@"""Bitstream Vera Serif Bold"", serif", ((StyleRule)sheet.Rules[1]).Style["font-family"]);
    }

    [Fact]
    public void StyleSheetFontFace()
    {
        var sheet = ParseSheet(@"@font-face {
  font-family: ""Bitstream Vera Serif Bold"";
  src: url(""http://developer.mozilla.org/@api/deki/files/2934/=VeraSeBd.ttf"");
}

body {
  font-family: ""Bitstream Vera Serif Bold"", serif;
}");
        Assert.Equal(2, sheet.Rules.Length);

        Assert.Equal("body", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal(@"""Bitstream Vera Serif Bold"", serif", ((StyleRule)sheet.Rules[1]).Style["font-family"]);
    }

    [Fact]
    public void StyleSheetHostLinebreak()
    {
        var sheet = ParseSheet(@"@host
    {
        :scope { color: white; }
    }");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetHost()
    {
        var sheet = ParseSheet(@"@host {
  :scope {
    display: block;
  }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetImportLinebreak()
    {
        var sheet = ParseSheet(@"@import
    url(test.css)
    screen
    ;");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("test.css", ((ImportRule)sheet.Rules[0]).Href);
    }

    [Fact]
    public void StyleSheetImportMessed()
    {
        var sheet = ParseSheet(@"
   @import url(""fineprint.css"") print;
  @import url(""bluish.css"") projection, tv;
      @import 'custom.css';
  @import ""common.css"" screen, projection  ;

  @import url('landscape.css') screen and (orientation:landscape);");
        Assert.Equal(5, sheet.Rules.Length);

        Assert.Equal("fineprint.css", ((ImportRule)sheet.Rules[0]).Href);
        Assert.Equal("print", ((ImportRule)sheet.Rules[0]).Media.MediaText);

        Assert.Equal("bluish.css", ((ImportRule)sheet.Rules[1]).Href);
        Assert.Equal("projection, tv", ((ImportRule)sheet.Rules[1]).Media.MediaText);

        Assert.Equal("custom.css", ((ImportRule)sheet.Rules[2]).Href);
        Assert.Equal("", ((ImportRule)sheet.Rules[2]).Media.MediaText);

        Assert.Equal("common.css", ((ImportRule)sheet.Rules[3]).Href);
        Assert.Equal("screen, projection", ((ImportRule)sheet.Rules[3]).Media.MediaText);

        Assert.Equal("landscape.css", ((ImportRule)sheet.Rules[4]).Href);
        Assert.Equal("screen and (orientation: landscape)", ((ImportRule)sheet.Rules[4]).Media.MediaText);
    }

    [Fact]
    public void StyleSheetImport()
    {
        var sheet = ParseSheet(@"@import url(""fineprint.css"") print;
@import url(""bluish.css"") projection, tv;
@import 'custom.css';
@import ""common.css"" screen, projection;
@import url('landscape.css') screen and (orientation:landscape);");
        Assert.Equal(5, sheet.Rules.Length);

        Assert.Equal("fineprint.css", ((ImportRule)sheet.Rules[0]).Href);
        Assert.Equal("print", ((ImportRule)sheet.Rules[0]).Media.MediaText);

        Assert.Equal("bluish.css", ((ImportRule)sheet.Rules[1]).Href);
        Assert.Equal("projection, tv", ((ImportRule)sheet.Rules[1]).Media.MediaText);

        Assert.Equal("custom.css", ((ImportRule)sheet.Rules[2]).Href);
        Assert.Equal("", ((ImportRule)sheet.Rules[2]).Media.MediaText);

        Assert.Equal("common.css", ((ImportRule)sheet.Rules[3]).Href);
        Assert.Equal("screen, projection", ((ImportRule)sheet.Rules[3]).Media.MediaText);

        Assert.Equal("landscape.css", ((ImportRule)sheet.Rules[4]).Href);
        Assert.Equal("screen and (orientation: landscape)", ((ImportRule)sheet.Rules[4]).Media.MediaText);
    }

    [Fact]
    public void StyleSheetKeyframesAdvanced()
    {
        var sheet = ParseSheet(@"@keyframes advanced {
  top {
    opacity[sqrt]: 0;
  }

  100 {
    opacity: 0.5;
  }

  bottom {
    opacity: 1;
  }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetKeyframesComplex()
    {
        var sheet = ParseSheet(@"@keyframes foo {
  0% { top: 0; left: 0 }
  30.50% { top: 50px }
  .68% ,
  72%
      , 85% { left: 50px }
  100% { top: 100px; left: 100% }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetKeyframesLinebreak()
    {
        var sheet = ParseSheet(@"@keyframes
    test
    {
        from { opacity: 1; }
        to { opacity: 0; }
    }
");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetKeyframesMessed()
    {
        var sheet = ParseSheet(@"@keyframes fade {from
  {opacity: 0;
     }
to
  {
     opacity: 1;}}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetKeyframesVendor()
    {
        var sheet = ParseSheet(@"@-webkit-keyframes fade {
  from { opacity: 0 }
  to { opacity: 1 }
}
");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetKeyframes()
    {
        var sheet = ParseSheet(@"@keyframes fade {
  /* from above */
  from {
    /* from inside */
    opacity: 0;
  }

  /* to above */
  to {
    /* to inside */
    opacity: 1;
  }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetMediaLinebreak()
    {
        var sheet = ParseSheet(@"@media

(
    min-width: 300px
)
{
    .test { width: 100px; }
}");
        Assert.Equal(1, sheet.Rules.Length);
        var rule = (MediaRule)sheet.Rules[0];

        Assert.Equal("(min-width: 300px)", rule.Media.MediaText);
        Assert.Equal(1, rule.Rules.Length);

        var subrule = rule.Rules[0];
        Assert.Equal(".test", ((StyleRule)subrule).SelectorText);
        Assert.Equal("100px", ((StyleRule)subrule).Style["width"]);
    }

    [Fact]
    public void StyleSheetMediaMessed()
    {
        var sheet = ParseSheet(@"@media screen, projection{ html

  {
background: #fffef0;
    color:#300;
  }
  body

{
    max-width: 35em;
    margin: 0 auto;


}
  }

@media print
{
              html {
              background: #fff;
              color: #000;
              }
              body {
              padding: 1in;
              border: 0.5pt solid #666;
              }
}");
        Assert.Equal(2, sheet.Rules.Length);

        {
            var rule = sheet.Rules[0];
            Assert.Equal("screen, projection", ((MediaRule)rule).Media.MediaText);
            Assert.Equal(2, ((MediaRule)rule).Rules.Length);

            {
                var subrule = ((MediaRule)rule).Rules[0];
                Assert.Equal("html", ((StyleRule)subrule).SelectorText);
                Assert.Equal("rgb(255, 254, 240)", ((StyleRule)subrule).Style["background-color"]);
                Assert.Equal("rgb(51, 0, 0)", ((StyleRule)subrule).Style["color"]);
            }

            {
                var subrule = ((MediaRule)rule).Rules[1];
                Assert.Equal("body", ((StyleRule)subrule).SelectorText);
                Assert.Equal("35em", ((StyleRule)subrule).Style["max-width"]);
                Assert.Equal("0", ((StyleRule)subrule).Style["margin-top"]);
                Assert.Equal("auto", ((StyleRule)subrule).Style["margin-right"]);
                Assert.Equal("0", ((StyleRule)subrule).Style["margin-bottom"]);
                Assert.Equal("auto", ((StyleRule)subrule).Style["margin-left"]);
            }
        }

        {
            var rule = sheet.Rules[1];
            Assert.Equal("print", ((MediaRule)rule).Media.MediaText);
            Assert.Equal(2, ((MediaRule)rule).Rules.Length);

            {
                var subrule = ((MediaRule)rule).Rules[0];
                Assert.Equal("html", ((StyleRule)subrule).SelectorText);
                Assert.Equal("rgb(255, 255, 255)", ((StyleRule)subrule).Style["background-color"]);
                Assert.Equal("rgb(0, 0, 0)", ((StyleRule)subrule).Style["color"]);
            }

            {
                var subrule = ((MediaRule)rule).Rules[1];
                Assert.Equal("body", ((StyleRule)subrule).SelectorText);
                Assert.Equal("1in", ((StyleRule)subrule).Style["padding-top"]);
                Assert.Equal("1in", ((StyleRule)subrule).Style["padding-right"]);
                Assert.Equal("1in", ((StyleRule)subrule).Style["padding-bottom"]);
                Assert.Equal("1in", ((StyleRule)subrule).Style["padding-left"]);
                Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-top"]);
                Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-right"]);
                Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-bottom"]);
                Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-left"]);
                Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-width"]);
                Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-width"]);
                Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-width"]);
                Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-width"]);
                Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-style"]);
                Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-style"]);
                Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-style"]);
                Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-style"]);
                Assert.Equal("rgb(102, 102, 102)",
                    ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-color"]);
                Assert.Equal("rgb(102, 102, 102)",
                    ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-color"]);
                Assert.Equal("rgb(102, 102, 102)",
                    ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-color"]);
                Assert.Equal("rgb(102, 102, 102)",
                    ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-color"]);
            }
        }
    }

    [Fact]
    public void StyleSheetMedia()
    {
        var sheet = ParseSheet(@"@media screen, projection {
  /* html above */
  html {
    /* html inside */
    background: #fffef0;
    color: #300;
  }

  /* body above */
  body {
    /* body inside */
    max-width: 35em;
    margin: 0 auto;
  }
}

@media print {
  html {
    background: #fff;
    color: #000;
  }
  body {
    padding: 1in;
    border: 0.5pt solid #666;
  }
}");
        Assert.Equal(2, sheet.Rules.Length);

        Assert.Equal("screen, projection", ((MediaRule)sheet.Rules[0]).Media.MediaText);
        Assert.Equal(2, ((MediaRule)sheet.Rules[0]).Rules.Length);

        Assert.Equal("html", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[0]).SelectorText);
        Assert.Equal("rgb(255, 254, 240)",
            ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[0]).Style["background-color"]);
        Assert.Equal("rgb(51, 0, 0)", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[0]).Style["color"]);

        Assert.Equal("body", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).SelectorText);
        Assert.Equal("35em", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).Style["max-width"]);
        Assert.Equal("0", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).Style["margin-top"]);
        Assert.Equal("auto", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).Style["margin-right"]);
        Assert.Equal("0", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).Style["margin-bottom"]);
        Assert.Equal("auto", ((StyleRule)((MediaRule)sheet.Rules[0]).Rules[1]).Style["margin-left"]);

        Assert.Equal("print", ((MediaRule)sheet.Rules[1]).Media.MediaText);
        Assert.Equal(2, ((MediaRule)sheet.Rules[1]).Rules.Length);

        Assert.Equal("html", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[0]).SelectorText);
        Assert.Equal("rgb(255, 255, 255)",
            ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[0]).Style["background-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[0]).Style["color"]);

        Assert.Equal("body", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).SelectorText);
        Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-top"]);
        Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-right"]);
        Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-bottom"]);
        Assert.Equal("1in", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["padding-left"]);
        Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-width"]);
        Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-width"]);
        Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-width"]);
        Assert.Equal("0.5pt", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-width"]);
        Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-style"]);
        Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-style"]);
        Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-style"]);
        Assert.Equal("solid", ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-style"]);
        Assert.Equal("rgb(102, 102, 102)",
            ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-top-color"]);
        Assert.Equal("rgb(102, 102, 102)",
            ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-right-color"]);
        Assert.Equal("rgb(102, 102, 102)",
            ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-bottom-color"]);
        Assert.Equal("rgb(102, 102, 102)",
            ((StyleRule)((MediaRule)sheet.Rules[1]).Rules[1]).Style["border-left-color"]);
    }

    [Fact]
    public void StyleSheetMessedUp()
    {
        var sheet = ParseSheet(@"body { foo
  :
  'bar' }

   body{foo:bar;bar:baz}
   body
   {
     foo
     :
     bar
     ;
     bar
     :
     baz
     }
");
        Assert.Equal(3, sheet.Rules.Length);

        Assert.Equal("body", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""bar""", ((StyleRule)sheet.Rules[0]).Style["foo"]);

        Assert.Equal("body", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal("bar", ((StyleRule)sheet.Rules[1]).Style["foo"]);
        Assert.Equal("baz", ((StyleRule)sheet.Rules[1]).Style["bar"]);

        Assert.Equal("body", ((StyleRule)sheet.Rules[2]).SelectorText);
        Assert.Equal("bar", ((StyleRule)sheet.Rules[2]).Style["foo"]);
        Assert.Equal("baz", ((StyleRule)sheet.Rules[2]).Style["bar"]);
    }

    [Fact]
    public void StyleSheetNamespaceLinebreak()
    {
        var sheet = ParseSheet(@"@namespace
    ""http://www.w3.org/1999/xhtml""
    ;");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetNamespace()
    {
        var sheet = ParseSheet(@"@namespace ""http://www.w3.org/1999/xhtml"";
@namespace svg ""http://www.w3.org/2000/svg"";");
        Assert.Equal(2, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetNoSemi()
    {
        var sheet = ParseSheet(@"
tobi loki jane {
  are: 'all';
  the-species: called ""ferrets""
}");
        Assert.Equal(1, sheet.Rules.Length);

        foreach (var rule in sheet.Rules)
        {
            Assert.Equal("tobi loki jane", ((StyleRule)rule).SelectorText);
            Assert.Equal(@"""all""", ((StyleRule)rule).Style["are"]);
            Assert.Equal(@"called ""ferrets""", ((StyleRule)rule).Style["the-species"]);
        }
    }

    [Fact]
    public void StyleSheetPageAtRulesAndProperties()
    {
        var sheet = ParseSheet("@page :left{size:A4;margin:30mm 15mm;@bottom-right{color:black;}}");
        Assert.Equal(1, sheet.Rules.Length);
        Assert.Equal(RuleType.Page, sheet.Rules[0].Type);
        Assert.Equal(1, sheet.Rules.Length);

        var pageRule = sheet.Rules[0] as PageRule;
        Assert.Equal(RuleType.Page, pageRule.Type);
        Assert.Equal(":left", pageRule.SelectorText);

        var marginRule = pageRule.Children.Last() as MarginStyleRule;
        Assert.Equal("@bottom-right", marginRule.SelectorText);
        Assert.Single(marginRule.Style.Children);
        Assert.Equal("color: rgb(0, 0, 0)", (marginRule.Style.Children.First() as ColorProperty).CssText);
    }

    [Fact]
    public void StyleSheetProps()
    {
        var sheet = ParseSheet(@"
tobi loki jane {
  are: 'all';
  the-species: called ""ferrets"";
  *even: 'ie crap';
}");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("tobi loki jane", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""all""", ((StyleRule)sheet.Rules[0]).Style["are"]);
        Assert.Equal(@"called ""ferrets""", ((StyleRule)sheet.Rules[0]).Style["the-species"]);
        Assert.Equal(@"""ie crap""", ((StyleRule)sheet.Rules[0]).Style["*even"]);
    }

    [Fact]
    public void StyleSheetQuoteEscape()
    {
        var sheet = ParseSheet(@"p[qwe=""a\"",b""] { color: red }
");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal(@"p[qwe=""a\"",b""]", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal("rgb(255, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["color"]);
    }

    [Fact]
    public void StyleSheetQuoted()
    {
        var sheet = ParseSheet(@"body {
  background: url('some;stuff;here') 50% 50% no-repeat;
}");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("body", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"url(""some;stuff;here"")", ((StyleRule)sheet.Rules[0]).Style["background-image"]);
        Assert.Equal("50% 50%", ((StyleRule)sheet.Rules[0]).Style["background-position"]);
        Assert.Equal("no-repeat", ((StyleRule)sheet.Rules[0]).Style["background-repeat"]);
    }

    [Fact]
    public void StyleSheetRule()
    {
        var sheet = ParseSheet(@"foo {
  bar: 'baz';
}");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("foo", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""baz""", ((StyleRule)sheet.Rules[0]).Style["bar"]);
    }

    [Fact]
    public void StyleSheetRules()
    {
        var sheet = ParseSheet(@"tobi {
  name: 'tobi';
  age: 2;
}

loki {
  name: 'loki';
  age: 1;
}");
        Assert.Equal(2, sheet.Rules.Length);

        Assert.Equal("tobi", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""tobi""", ((StyleRule)sheet.Rules[0]).Style["name"]);
        Assert.Equal("2", ((StyleRule)sheet.Rules[0]).Style["age"]);

        Assert.Equal("loki", ((StyleRule)sheet.Rules[1]).SelectorText);
        Assert.Equal(@"""loki""", ((StyleRule)sheet.Rules[1]).Style["name"]);
        Assert.Equal("1", ((StyleRule)sheet.Rules[1]).Style["age"]);
    }

    [Fact]
    public void StyleSheetSelectors()
    {
        var sheet = ParseSheet(@"foo,
bar,
baz {
  color: 'black';
}");
        Assert.Equal(1, sheet.Rules.Length);

        Assert.Equal("foo,bar,baz", ((StyleRule)sheet.Rules[0]).SelectorText);
        Assert.Equal(@"""black""", ((StyleRule)sheet.Rules[0]).Style["color"]);
    }

    [Fact]
    public void StyleSheetSupportsLinebreak()
    {
        var sheet = ParseSheet(@"@supports
    (display: flex)
    {
        .test { display: flex; }
    }");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetSupports()
    {
        var sheet = ParseSheet(@"@supports (display: flex) or (display: box) {
  /* flex above */
  .flex {
    /* flex inside */
    display: box;
    display: flex;
  }

  div {
    something: else;
  }
}");
        Assert.Equal(1, sheet.Rules.Length);
    }

    [Fact]
    public void StyleSheetWtf()
    {
        var sheet = ParseSheet(@".wtf {
  *overflow-x: hidden;
  //max-height: 110px;
  #height: 18px;
}");
        Assert.Equal(1, sheet.Rules.Length);

        foreach (var rule in sheet.Rules)
        {
            Assert.Equal(".wtf", ((StyleRule)rule).SelectorText);
            Assert.Equal("hidden", ((StyleRule)rule).Style["*overflow-x"]);
            Assert.Equal("110px", ((StyleRule)rule).Style["//max-height"]);
            Assert.Equal("18px", ((StyleRule)rule).Style["#height"]);
        }
    }

    [Fact]
    public void StyleSheetUnicodeEscapeLiteral()
    {
        var sheet = ParseSheet(@"h1 { background-color: \000062
lack; }");
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["background-color"]);
    }

    [Fact]
    public void StyleSheetUnicodeEscapeVarious()
    {
        var sheet = ParseSheet(
            "h1 { background-color: \\000062\r\nlack; color: \\000062\tlack; border-color: \\000062\nlack; outline-color: \\000062 lack }");
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["background-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["border-top-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["border-right-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["border-bottom-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["border-left-color"]);
        Assert.Equal("rgb(0, 0, 0)", ((StyleRule)sheet.Rules[0]).Style["outline-color"]);
    }

    [Fact]
    public void StyleSheetUnicodeEscapeLeadingSingleCarriageReturn()
    {
        var sheet = ParseSheet("h1 { background-image: \\000075\r\r\nrl('foo') }");
        Assert.Equal("u\nrl(\"foo\")", ((StyleRule)sheet.Rules[0]).Style["background-image"]);
    }

    [Fact]
    public void StyleSheetWithInitialCommentShouldWorkWithTriviaActive()
    {
        var parser = new StylesheetParser(preserveComments: true);
        var document = parser.Parse("/* Comment at the start */ body { font-size: 10pt; }");
        var comment = document.Children.First();

        Assert.IsType<Comment>(comment);
        Assert.Equal(" Comment at the start ", ((Comment)comment).Data);
    }

    [Fact]
    public void StylesheetIncludeUnknownDeclarationsWithKnownPropertyShouldNotUseUnknownProperty()
    {
        var parser = new StylesheetParser(includeUnknownDeclarations: true);
        var document = parser.Parse("body { border-width: 0; }");

        Assert.IsNotType<UnknownProperty>(((StyleRule)document.Rules[0]).Style.Children.First());
    }

    [Theory]
    [InlineData("@page { margin-bottom: 5pt; margin-top: 5pt }", "@page {margin-bottom: 5pt; margin-top: 5pt; }")]
    [InlineData("@page :left { margin-bottom: 5pt; margin-top: 5pt }",
        "@page :left {margin-bottom: 5pt; margin-top: 5pt; }")]
    public void PageRuleCSSOutput(string input, string expected)
    {
        var parser = new StylesheetParser();
        var document = parser.Parse(input);

        Assert.Equal(expected, document.ToCss());
    }
}

















