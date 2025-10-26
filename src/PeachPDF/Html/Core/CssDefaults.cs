// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;

namespace PeachPDF.Html.Core
{
    internal static class CssDefaults
    {
        /// <summary>
        /// CSS Specification's Default Style Sheet for HTML 4
        /// </summary>
        /// <remarks>
        /// http://www.w3.org/TR/CSS21/sample.html
        /// </remarks>
        public const string DefaultStyleSheet = """
                                                
            html, address,
            article, aside,
            footer, header,
            blockquote,
            body, dd, div,
            dl, dt, fieldset, form,
            frame, frameset,
            h1, h2, h3, h4,
            h5, h6, 
            hgroup, main, nav,
            section, search,
            noframes,
            ol, p, ul, center,
            dir, menu, pre   { display: block }
            li              { display: list-item }
            head            { display: none }
            table           { display: table }
            tr              { display: table-row }
            thead           { display: table-header-group }
            tbody           { display: table-row-group }
            tfoot           { display: table-footer-group }
            col             { display: table-column }
            colgroup        { display: table-column-group }
            td, th          { display: table-cell }
            caption         { display: table-caption }
            th              { font-weight: bolder; text-align: center }
            caption         { text-align: center }
            body            { margin: 8px }
            h1              { font-size: 2em; margin: .67em 0 }
            h2              { font-size: 1.5em; margin: .75em 0 }
            h3              { font-size: 1.17em; margin: .83em 0 }
            h4, p,
            blockquote, ul,
            fieldset, form,
            ol, dl, dir,
            menu            { margin: 1.12em 0 }
            h5              { font-size: .83em; margin: 1.5em 0 }
            h6              { font-size: .75em; margin: 1.67em 0 }
            h1, h2, h3, h4,
            h5, h6, b,
            strong          { font-weight: bolder; }
            blockquote      { margin-left: 40px; margin-right: 40px }
            i, cite, em,
            var, address    { font-style: italic }
            pre, tt, code,
            kbd, samp       { font-family: monospace }
            pre             { white-space: pre }
            button, textarea,
            input, select   { display: inline-block }
            big             { font-size: 1.17em }
            small, sub, sup { font-size: .83em }
            sub             { vertical-align: sub }
            sup             { vertical-align: super }
            table           { border-spacing: 2px; }
            thead, tbody,
            tfoot, tr       { vertical-align: middle }
            td, th          { vertical-align: inherit }
            s, strike, del  { text-decoration: line-through }
            hr              { border: 1px inset; }
            ol, ul, dir,
            menu, dd        { margin-left: 40px }
            ol              { list-style-type: decimal }
            ol ul, ul ol,
            ul ul, ol ol    { margin-top: 0; margin-bottom: 0 }
            ol ul, ul ul    { list-style-type: circle }
            ul ul ul, 
            ol ul ul, 
            ul ol ul        { list-style-type: square }
            u, ins          { text-decoration: underline }
            
            br:before       { content: "\A" }
            :before, :after { white-space: pre-line }
            center          { text-align: center }
            :link, :visited { text-decoration: underline }
            :focus          { outline: thin dotted invert }
            
            /* Begin bidirectionality settings (do not change) */
            BDO[DIR="ltr"]  { direction: ltr; unicode-bidi: bidi-override }
            BDO[DIR="rtl"]  { direction: rtl; unicode-bidi: bidi-override }

            *[DIR="ltr"]    { direction: ltr; unicode-bidi: embed }
            *[DIR="rtl"]    { direction: rtl; unicode-bidi: embed }

            @media print {
              h1            { page-break-before: always }
              h1, h2, h3,
              h4, h5, h6    { page-break-after: avoid }
              ul, ol, dl    { page-break-before: avoid }
            }

            /* Not in the specification but necessary */
            a               { color: #0055BB; text-decoration:underline }
            table           { border-color:#dfdfdf; }
            td, th          { border-color:#dfdfdf; overflow: hidden; }
            style, title,
            script, link,
            meta, area,
            base, param     { display:none }
            hr              { border-top-color: #9A9A9A; border-left-color: #9A9A9A; border-bottom-color: #EEEEEE; border-right-color: #EEEEEE; }
            pre             { font-size: 10pt; margin-top: 15px; }
        """;

        public static Dictionary<string, string> InitialValues = new()
        {
            { PropertyNames.BackgroundAttachment, CssConstants.Scroll },
            { PropertyNames.BackgroundClip, CssConstants.BorderBox },
            { PropertyNames.BackgroundColor, CssConstants.Transparent },
            { PropertyNames.BackgroundImage, CssConstants.None },
            { PropertyNames.BackgroundOrigin, CssConstants.PaddingBox },
            { PropertyNames.BackgroundPosition, "0% 0%"},
            { "background-repeat", CssConstants.Repeat },
            { "background-size", $"{CssConstants.Auto} {CssConstants.Auto}"},
            { "border-bottom-color", CssConstants.CurrentColor },
            { "border-bottom-style", CssConstants.None },
            { "border-bottom-width", CssConstants.Medium },
            { "border-left-color", CssConstants.CurrentColor },
            { "border-left-style", CssConstants.None },
            { "border-left-width", CssConstants.Medium },
            { "border-right-color", CssConstants.CurrentColor },
            { "border-right-style", CssConstants.None },
            { "border-right-width", CssConstants.Medium },
            { "border-top-color", CssConstants.CurrentColor },
            { "border-top-style", CssConstants.None },
            { "border-top-width", CssConstants.Medium },
            { "box-sizing", CssConstants.ContentBox },
            { "content", CssConstants.Normal },
            { "counter-increment", CssConstants.None },
            { "counter-reset", CssConstants.None },
            { "counter-set", CssConstants.None },
            { "string-set", CssConstants.None },
            { "font-stretch", CssConstants.Normal },
            { "font-style", CssConstants.Normal },
            { "font-variant", CssConstants.Normal },
            { "font-weight", CssConstants.Normal },
            { "line-height", CssConstants.Normal },
            { "list-style-image", CssConstants.None },
            { "list-style-position", CssConstants.Outside },
            { "text-decoration-color", CssConstants.CurrentColor },
            { "text-decoration-style", CssConstants.Solid },
            { "z-index", CssConstants.Auto }
        };
    }
}