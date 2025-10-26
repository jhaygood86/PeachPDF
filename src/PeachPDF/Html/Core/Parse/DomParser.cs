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
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// Handle css DOM tree generation from raw html and stylesheet.
    /// </summary>
    internal sealed class DomParser
    {
        /// <summary>
        /// Parser for CSS
        /// </summary>
        private readonly CssParser _cssParser;

        /// <summary>
        /// Init.
        /// </summary>
        public DomParser(CssParser cssParser)
        {
            ArgumentNullException.ThrowIfNull(cssParser);

            _cssParser = cssParser;
        }

        /// <summary>
        /// Generate css tree by parsing the given html and applying the given css style data on it.
        /// </summary>
        /// <param name="html">the html to parse</param>
        /// <param name="htmlContainer">the html container to use for reference resolve</param>
        /// <param name="cssData">the css data to use</param>
        /// <returns>the root of the generated tree</returns>
        public async Task<(CssBox cssBox, CssData cssData)> GenerateCssTree(string html, HtmlContainerInt htmlContainer, CssData cssData)
        {
            CssBox.ClearCounter();
            var root = HtmlParser.ParseDocument(html);
            root.IsRoot = true;
            root.HtmlContainer = htmlContainer;
            const bool cssDataChanged = false;

            (cssData, _) = await CascadeParseStyles(root, htmlContainer, cssData, cssDataChanged);

            //var media = htmlContainer.GetCssMediaType(cssData.MediaBlocks.Keys);
            var media = "print"; // TODO: fix this

            var cssValueParser = new CssValueParser(htmlContainer.Adapter);

            await CascadeApplyStyleFonts(cssData, htmlContainer.Adapter);

            CascadeApplyPageStyles(htmlContainer, root, cssData);

            CascadeApplyStyles(cssValueParser, root, cssData, media);

            CorrectTextBoxes(root);

            CorrectImgBoxes(root);

            CorrectLineBreaksBlocks(root);

            CorrectInlineBoxesParent(root);

            CorrectAbsolutelyPositionedInlineElements(root);

            CorrectBlockInsideInline(root);

            CorrectInlineBoxesParent(root);

            CorrectAnonymousTables(root);

            return (root, cssData);
        }


        #region Private methods

        private static async Task CascadeApplyStyleFonts(CssData cssData, RAdapter adapter)
        {
            foreach (var stylesheet in cssData.Stylesheets)
            {
                foreach (var fontRule in stylesheet.FontfaceSetRules)
                {
                    var fontFamilyName = CssValueParser.GetFontFaceFamilyName(fontRule.Family);
                    var fontFaceDefinition = CssValueParser.GetFontFacePropertyValue(fontRule.Source);

                    var isLoaded = false;

                    if (fontFaceDefinition.Local is not null)
                    {
                        isLoaded = await adapter.AddLocalFontFamily(fontFamilyName, fontFaceDefinition.Local);
                    }

                    if (!isLoaded && fontFaceDefinition.Url is not null)
                    {

                        await adapter.AddFontFamilyFromUrl(fontFamilyName, fontFaceDefinition.Url, fontFaceDefinition.Format);
                    }
                }
            }
        }

        /// <summary>
        /// Read styles defined inside the dom structure in links and style elements.<br/>
        /// If the html tag is "style" tag parse it content and add to the css data for all future tags parsing.<br/>
        /// If the html tag is "link" that point to style data parse it content and add to the css data for all future tags parsing.<br/>
        /// </summary>
        /// <param name="box">the box to parse style data in</param>
        /// <param name="htmlContainer">the html container to use for reference resolve</param>
        /// <param name="cssData">the style data to fill with found styles</param>
        /// <param name="cssDataChanged">check if the css data has been modified by the handled html not to change the base css data</param>
        private async Task<(CssData cssData, bool cssDataChanged)> CascadeParseStyles(CssBox box, HtmlContainerInt htmlContainer, CssData cssData, bool cssDataChanged)
        {
            if (box.HtmlTag != null)
            {
                // Check for the <link rel=stylesheet> tag
                if (box.HtmlTag.Name.Equals("link", StringComparison.CurrentCultureIgnoreCase) &&
                   box.GetAttribute("rel", string.Empty).Equals("stylesheet", StringComparison.CurrentCultureIgnoreCase))
                {
                    CloneCssData(ref cssData, ref cssDataChanged);
                    var stylesheet = await StylesheetLoadHandler.LoadStylesheet(htmlContainer, box.GetAttribute("href", string.Empty));
                    if (stylesheet != null)
                        await _cssParser.ParseStyleSheet(cssData, stylesheet);
                }

                // Check for the <style> tag
                if (box.HtmlTag.Name.Equals("style", StringComparison.CurrentCultureIgnoreCase) && box.Boxes.Count > 0)
                {
                    CloneCssData(ref cssData, ref cssDataChanged);
                    foreach (var child in box.Boxes)
                        await _cssParser.ParseStyleSheet(cssData, child.Text!);
                }
            }

            foreach (var childBox in box.Boxes)
            {
                (cssData, cssDataChanged) = await CascadeParseStyles(childBox, htmlContainer, cssData, cssDataChanged);
            }

            return (cssData, cssDataChanged);
        }

        private static void CascadeApplyPageStyles(HtmlContainerInt htmlContainer, CssBox root, CssData cssData)
        {
            foreach (var style in cssData.Stylesheets)
            {
                foreach (var pageRule in style.PageRules)
                {
                    if (pageRule.Style.MarginLeft.Length > 0)
                    {
                        htmlContainer.MarginLeft = CssValueParser.ParseLength(pageRule.Style.MarginLeft, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginTop.Length > 0)
                    {
                        htmlContainer.MarginTop = CssValueParser.ParseLength(pageRule.Style.MarginTop, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginBottom.Length > 0)
                    {
                        htmlContainer.MarginBottom = CssValueParser.ParseLength(pageRule.Style.MarginBottom, htmlContainer.PageSize.Width, root);
                    }

                    if (pageRule.Style.MarginRight.Length > 0)
                    {
                        htmlContainer.MarginRight = CssValueParser.ParseLength(pageRule.Style.MarginRight, htmlContainer.PageSize.Width, root);
                    }
                }
            }
        }

        /// <summary>
        /// Applies style to all boxes in the tree.<br/>
        /// If the html tag has style defined for each apply that style to the css box of the tag.<br/>
        /// If the html tag has "class" attribute and the class name has style defined apply that style on the tag css box.<br/>
        /// If the html tag has "style" attribute parse it and apply the parsed style on the tag css box.<br/>
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="box">the box to apply the style to</param>
        /// <param name="cssData">the style data for the html</param>
        /// <param name="media">The media type to apply styles to</param>
        private static void CascadeApplyStyles(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            // Set initial styles
            foreach (var style in CssDefaults.InitialValues)
            {
                CssUtils.SetPropertyValue(valueParser, box, style.Key, style.Value);
            }

            box.InheritStyle();

            // try assign style using all wildcard
            var importantPropertyNames = AssignCssBlocks(valueParser, box, cssData, media);

            if (box.HtmlTag != null)
            {
                TranslateAttributes(box.HtmlTag, box);

                // Check for the style="" attribute
                if (box.HtmlTag.HasAttribute("style"))
                {
                    var styleAttributeText = box.HtmlTag.TryGetAttribute("style");
                    var stylesheet = "* { " + styleAttributeText + " }";

                    var block = CssParser.ParseStyleSheet(stylesheet);
                    AssignCssBlock(valueParser, box, block.StyleRules.Single(), importantPropertyNames);
                }
            }

            // Correct current color
            CssUtils.ApplyCurrentColor(box, valueParser);

            // cascade text decoration only to boxes that actually have text so it will be handled correctly.
            if (box.TextDecoration != string.Empty && box.Text == null)
            {
                foreach (var childBox in box.Boxes)
                {
                    childBox.TextDecoration = box.TextDecoration;
                    childBox.TextDecorationLine = box.TextDecorationLine;
                    childBox.TextDecorationStyle = box.TextDecorationStyle;
                    childBox.TextDecorationColor = box.TextDecorationColor;
                }

                box.TextDecoration = string.Empty;
                box.TextDecorationLine = string.Empty;
                box.TextDecorationStyle = string.Empty;
                box.TextDecorationColor = string.Empty;
            }

            foreach (var childBox in box.Boxes)
            {
                CascadeApplyStyles(valueParser, childBox, cssData, media);
            }
        }

        /// <summary>
        /// Assigns the given css style blocks to the given css box checking if matching.
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="box">the css box to assign css to</param>
        /// <param name="cssData">the css data to use to get the matching css blocks</param>
        /// <param name="media">The media type to apply styles for</param>
        /// <returns>The list of applied important property names</returns>
        private static HashSet<string> AssignCssBlocks(CssValueParser valueParser, CssBox box, CssData cssData, string media)
        {
            var combinedBlocks = new List<IStyleRule>();
            var styleRules = cssData.GetStyleRules(media, box);
            combinedBlocks.AddRange(styleRules);

            HashSet<string> importantPropertyNames = [];

            foreach (var block in combinedBlocks)
            {
                AssignCssBlock(valueParser, box, block, importantPropertyNames);
            }

            return importantPropertyNames;
        }

        /// <summary>
        /// Assigns the given css style block properties to the given css box.
        /// </summary>
        /// <param name="valueParser">the css value parser to use</param>
        /// <param name="box">the css box to assign css to</param>
        /// <param name="stylesheetRule">the stylesheet rule to assign</param>
        /// <param name="importantPropertyNames">Carries the property names that have been marked important so they don't get re-applied</param>
        private static void AssignCssBlock(CssValueParser valueParser, CssBox box, IStyleRule stylesheetRule, HashSet<string> importantPropertyNames)
        {
            foreach (var prop in stylesheetRule.Style)
            {
                var value = prop.Value switch
                {
                    CssConstants.Inherit when box.ParentBox != null => CssUtils.GetPropertyValue(box.ParentBox, prop.Name),
                    CssConstants.Initial => CssDefaults.InitialValues[prop.Name],
                    _ => prop.Value
                };

                if (importantPropertyNames.Contains(prop.Name.ToLowerInvariant()))
                {
                    continue;
                }

                if (prop.IsImportant)
                {
                    importantPropertyNames.Add(prop.Name.ToLowerInvariant());
                }

                if (value is not null && IsStyleOnElementAllowed(box, prop.Name, value))
                {
                    CssUtils.SetPropertyValue(valueParser, box, prop.Name, value);
                }
            }
        }

        /// <summary>
        /// Check if the given style is allowed to be set on the given css box.<br/>
        /// Used to prevent invalid CssBoxes creation like table with inline display style.
        /// </summary>
        /// <param name="box">the css box to assign css to</param>
        /// <param name="key">the style key to check</param>
        /// <param name="value">the style value to check</param>
        /// <returns>true - style allowed, false - not allowed</returns>
        private static bool IsStyleOnElementAllowed(CssBox box, string key, string value)
        {
            if (box.HtmlTag == null || key != HtmlConstants.Display) return true;

            if (value is CssConstants.None)
            {
                return true;
            }

            return box.HtmlTag.Name switch
            {
                HtmlConstants.Table => value == CssConstants.Table,
                HtmlConstants.Tr => value == CssConstants.TableRow,
                HtmlConstants.Tbody => value == CssConstants.TableRowGroup,
                HtmlConstants.Thead => value == CssConstants.TableHeaderGroup,
                HtmlConstants.Tfoot => value == CssConstants.TableFooterGroup,
                HtmlConstants.Col => value == CssConstants.TableColumn,
                HtmlConstants.Colgroup => value == CssConstants.TableColumnGroup,
                HtmlConstants.Td or HtmlConstants.Th => value == CssConstants.TableCell,
                HtmlConstants.Caption => value == CssConstants.TableCaption,
                _ => true
            };
        }

        /// <summary>
        /// Clone css data if it has not already been cloned.<br/>
        /// Used to preserve the base css data used when changed by style inside html.
        /// </summary>
        private static void CloneCssData(ref CssData cssData, ref bool cssDataChanged)
        {
            if (cssDataChanged) return;

            cssDataChanged = true;
            cssData = cssData.Clone();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="tag"></param>
        /// <param name="box"></param>
        private static void TranslateAttributes(HtmlTag tag, CssBox box)
        {
            if (!tag.HasAttributes()) return;

            foreach (var att in tag.Attributes!.Keys)
            {
                var value = tag.Attributes[att];

                switch (att)
                {
                    case HtmlConstants.Align:
                        if (tag.Name is "img")
                        {
                            switch (value)
                            {
                                case HtmlConstants.Left:
                                    box.VerticalAlign = CssConstants.Top;
                                    box.Float = CssConstants.Left;
                                    break;
                                case HtmlConstants.Right:
                                    box.VerticalAlign = CssConstants.Top;
                                    box.Float = CssConstants.Right;
                                    break;
                                case HtmlConstants.Bottom:
                                    box.VerticalAlign = CssConstants.Baseline;
                                    break;
                                case HtmlConstants.Middle:
                                    box.VerticalAlign = CssConstants.PeachBaselineMiddle;
                                    break;
                                case HtmlConstants.Top:
                                    box.VerticalAlign = CssConstants.Top;
                                    break;
                            }
                        }
                        else
                        {
                            if (value is HtmlConstants.Left or HtmlConstants.Center or HtmlConstants.Right or HtmlConstants.Justify)
                                box.TextAlign = value.ToLower();
                            else
                                box.VerticalAlign = value.ToLower();

                        }

                        break;
                    case HtmlConstants.Background:
                        box.BackgroundImage = value.ToLower();
                        break;
                    case HtmlConstants.Bgcolor:
                        box.BackgroundColor = value.ToLower();
                        break;
                    case HtmlConstants.Border:
                        if (!string.IsNullOrEmpty(value) && value != "0")
                            box.BorderLeftStyle = box.BorderTopStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                        box.BorderLeftWidth = box.BorderTopWidth = box.BorderRightWidth = box.BorderBottomWidth = TranslateLength(value);

                        if (tag.Name == HtmlConstants.Table)
                        {
                            if (value != "0")
                                ApplyTableBorder(box, "1px");
                        }
                        else
                        {
                            box.BorderTopStyle = box.BorderLeftStyle = box.BorderRightStyle = box.BorderBottomStyle = CssConstants.Solid;
                        }
                        break;
                    case HtmlConstants.Bordercolor:
                        box.BorderLeftColor = box.BorderTopColor = box.BorderRightColor = box.BorderBottomColor = value.ToLower();
                        break;
                    case HtmlConstants.Cellspacing:
                        box.BorderSpacing = TranslateLength(value);
                        break;
                    case HtmlConstants.Cellpadding:
                        ApplyTablePadding(box, value);
                        break;
                    case HtmlConstants.Color:
                        box.Color = value.ToLower();
                        break;
                    case HtmlConstants.Dir:
                        box.Direction = value.ToLower();
                        break;
                    case HtmlConstants.Face:
                        //box.FontFamily = _cssParser.ParseFontFamily(value);
                        throw new NotImplementedException();
                    case HtmlConstants.Height:
                        box.Height = TranslateLength(value);
                        break;
                    case HtmlConstants.Hspace:
                        box.MarginRight = box.MarginLeft = TranslateLength(value);
                        break;
                    case HtmlConstants.Nowrap:
                        box.WhiteSpace = CssConstants.NoWrap;
                        break;
                    case HtmlConstants.Size:
                        if (tag.Name.Equals(HtmlConstants.Hr, StringComparison.OrdinalIgnoreCase))
                            box.Height = TranslateLength(value);
                        else if (tag.Name.Equals(HtmlConstants.Font, StringComparison.OrdinalIgnoreCase))
                            box.FontSize = value;
                        break;
                    case HtmlConstants.Valign:
                        box.VerticalAlign = value.ToLower();
                        break;
                    case HtmlConstants.Vspace:
                        box.MarginTop = box.MarginBottom = TranslateLength(value);
                        break;
                    case HtmlConstants.Width:
                        box.Width = TranslateLength(value);
                        break;
                }
            }
        }

        /// <summary>
        /// Converts an HTML length into a Css length
        /// </summary>
        /// <param name="htmlLength"></param>
        /// <returns></returns>
        private static string TranslateLength(string htmlLength)
        {
            var len = new CssLength(htmlLength);

            return len.HasError ? string.Format(NumberFormatInfo.InvariantInfo, "{0}px", htmlLength) : htmlLength;
        }

        /// <summary>
        /// Cascades to the TD's the border specified in the TABLE tag.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="border"></param>
        private static void ApplyTableBorder(CssBox table, string border)
        {
            SetForAllCells(table, cell =>
            {
                cell.BorderLeftStyle = cell.BorderTopStyle = cell.BorderRightStyle = cell.BorderBottomStyle = CssConstants.Solid;
                cell.BorderLeftWidth = cell.BorderTopWidth = cell.BorderRightWidth = cell.BorderBottomWidth = border;
            });
        }

        /// <summary>
        /// Cascades to the TD's the border specified in the TABLE tag.
        /// </summary>
        /// <param name="table"></param>
        /// <param name="padding"></param>
        private static void ApplyTablePadding(CssBox table, string padding)
        {
            var length = TranslateLength(padding);
            SetForAllCells(table, cell => cell.PaddingLeft = cell.PaddingTop = cell.PaddingRight = cell.PaddingBottom = length);
        }

        /// <summary>
        /// Execute action on all the "td" cells of the table.<br/>
        /// Handle if there is "theader" or "tbody" exists.
        /// </summary>
        /// <param name="table">the table element</param>
        /// <param name="action">the action to execute</param>
        private static void SetForAllCells(CssBox table, Action<CssBox> action)
        {
            foreach (var l1 in table.Boxes)
            {
                foreach (var l2 in l1.Boxes)
                {
                    if (l2.HtmlTag is { Name: "td" })
                    {
                        action(l2);
                    }
                    else
                    {
                        foreach (var l3 in l2.Boxes)
                        {
                            action(l3);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Go over all the text boxes (boxes that have some text that will be rendered) and
        /// remove all boxes that have only white-spaces but are not 'preformatted' so they do not effect
        /// the rendered html.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectTextBoxes(CssBox box)
        {
            for (var i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var childBox = box.Boxes[i];

                CssContentEngine.ApplyContent(childBox);

                if (childBox.Text != null)
                {
                    // is the box has text
                    var keepBox = !string.IsNullOrWhiteSpace(childBox.Text);

                    // if the box is a br
                    keepBox = keepBox || childBox.IsBrElement;

                    // is the box is pre-formatted
                    keepBox = keepBox || childBox.WhiteSpace == CssConstants.Pre || childBox.WhiteSpace == CssConstants.PreWrap;

                    // is the box is only one in the parent
                    keepBox = keepBox || box.Boxes.Count == 1;

                    // is it a whitespace between two inline boxes
                    keepBox = keepBox || (i > 0 && i < box.Boxes.Count - 1 && box.Boxes[i - 1].IsInline && box.Boxes[i + 1].IsInline);

                    // is first/last box where is in inline box and it's next/previous box is inline
                    keepBox = keepBox || (i == 0 && box.Boxes.Count > 1 && box.Boxes[1].IsInline && box.IsInline) || (i == box.Boxes.Count - 1 && box.Boxes.Count > 1 && box.Boxes[i - 1].IsInline && box.IsInline);

                    if (keepBox)
                    {
                        // valid text box, parse it to words
                        childBox.ParseToWords();
                    }
                    else
                    {
                        // remove text box that has no 
                        childBox.ParentBox!.Boxes.RemoveAt(i);
                    }
                }
                else
                {
                    // recursive
                    CorrectTextBoxes(childBox);
                }
            }
        }

        /// <summary>
        /// Go over all image boxes and if its display style is set to block, put it inside another block but set the image to inline.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectImgBoxes(CssBox box)
        {
            for (int i = box.Boxes.Count - 1; i >= 0; i--)
            {
                var childBox = box.Boxes[i];
                if (childBox is CssBoxImage && childBox.Display == CssConstants.Block)
                {
                    var block = CssBox.CreateBlock(childBox.ParentBox!, null, childBox);
                    childBox.ParentBox = block;
                    childBox.Display = CssConstants.Inline;
                }
                else
                {
                    // recursive
                    CorrectImgBoxes(childBox);
                }
            }
        }

        /// <summary>
        /// Correct the DOM tree recursively by replacing  "br" html boxes with anonymous blocks that respect br spec.<br/>
        /// If the "br" tag is after inline box then the anon block will have zero height only acting as newline,
        /// but if it is after block box then it will have min-height of the font size so it will create empty line.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        /// move to a new line</param>
        private static void CorrectLineBreaksBlocks(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                CorrectLineBreaksBlocks(childBox);
            }

            if (!box.IsBrElement)
            {
                return;
            }

            var previousSibling = DomUtils.GetPreviousSibling(box);

            if (previousSibling is null or { IsBlock: true })
            {
                var nextSibling = DomUtils.GetFollowingSiblings(box, b => b is { IsInline: true, IsBrElement: false }, true).FirstOrDefault();

                if (nextSibling is null)
                {
                    box.Text = "\n";
                    box.ParseToWords();
                }
            }
        }

        /// <summary>
        /// Correct DOM tree if there is block boxes that are inside inline blocks.<br/>
        /// Need to rearrange the tree so block box will be only the child of other block box.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectBlockInsideInline(CssBox box)
        {
            try
            {
                if (DomUtils.ContainsInlinesOnly(box) && !ContainsInlinesOnlyDeep(box))
                {
                    var tempRightBox = CorrectBlockInsideInlineImp(box);
                    while (tempRightBox != null)
                    {
                        // loop on the created temp right box for the fixed box until no more need (optimization remove recursion)
                        CssBox? newTempRightBox = null;
                        if (DomUtils.ContainsInlinesOnly(tempRightBox) && !ContainsInlinesOnlyDeep(tempRightBox))
                            newTempRightBox = CorrectBlockInsideInlineImp(tempRightBox);

                        tempRightBox.ParentBox!.SetAllBoxes(tempRightBox);
                        tempRightBox.ParentBox = null;
                        tempRightBox = newTempRightBox;
                    }
                }

                if (DomUtils.ContainsInlinesOnly(box)) return;

                foreach (var childBox in box.Boxes)
                {
                    CorrectBlockInsideInline(childBox);
                }
            }
            catch (Exception ex)
            {
                box.HtmlContainer?.ReportError(HtmlRenderErrorType.HtmlParsing, "Failed in block inside inline box correction", ex);
            }
        }

        /// <summary>
        /// Rearrange the DOM of the box to have block box with boxes before the inner block box and after.
        /// </summary>
        /// <param name="box">the box that has the problem</param>
        private static CssBox? CorrectBlockInsideInlineImp(CssBox box)
        {
            if (box.Display == CssConstants.Inline)
                box.Display = CssConstants.Block;

            if (box.Boxes.Count > 1 || box.Boxes[0].Boxes.Count > 1)
            {
                var leftBlock = CssBox.CreateBlock(box);

                while (ContainsInlinesOnlyDeep(box.Boxes[0]))
                    box.Boxes[0].ParentBox = leftBlock;
                leftBlock.SetBeforeBox(box.Boxes[0]);

                var splitBox = box.Boxes[1];
                splitBox.ParentBox = null;

                CorrectBlockSplitBadBox(box, splitBox, leftBlock);

                // remove block that did not get any inner elements
                if (leftBlock.Boxes.Count < 1)
                    leftBlock.ParentBox = null;

                int minBoxes = leftBlock.ParentBox != null ? 2 : 1;
                if (box.Boxes.Count <= minBoxes) return null;
                // create temp box to handle the tail elements and then get them back so no deep hierarchy is created
                var tempRightBox = CssBox.CreateBox(box, null, box.Boxes[minBoxes]);
                while (box.Boxes.Count > minBoxes + 1)
                    box.Boxes[minBoxes + 1].ParentBox = tempRightBox;

                return tempRightBox;
            }
            else if (box.Boxes[0].Display == CssConstants.Inline)
            {
                box.Boxes[0].Display = CssConstants.Block;
            }

            return null;
        }

        /// <summary>
        /// Split bad box that has inline and block boxes into two parts, the left - before the block box
        /// and right - after the block box.
        /// </summary>
        /// <param name="parentBox">the parent box that has the problem</param>
        /// <param name="badBox">the box to split into different boxes</param>
        /// <param name="leftBlock">the left block box that is created for the split</param>
        private static void CorrectBlockSplitBadBox(CssBox parentBox, CssBox badBox, CssBox leftBlock)
        {
            CssBox? leftbox = null;
            while (badBox.Boxes[0].IsInline && ContainsInlinesOnlyDeep(badBox.Boxes[0]))
            {
                if (leftbox == null)
                {
                    // if there is no elements in the left box there is no reason to keep it
                    leftbox = CssBox.CreateBox(leftBlock, badBox.HtmlTag);
                    leftbox.InheritStyle(badBox, true);
                }
                badBox.Boxes[0].ParentBox = leftbox;
            }

            var splitBox = badBox.Boxes[0];
            if (!ContainsInlinesOnlyDeep(splitBox))
            {
                CorrectBlockSplitBadBox(parentBox, splitBox, leftBlock);
                splitBox.ParentBox = null;
            }
            else
            {
                splitBox.ParentBox = parentBox;
            }

            if (badBox.Boxes.Count > 0)
            {
                CssBox rightBox;
                if (splitBox.ParentBox != null || parentBox.Boxes.Count < 3)
                {
                    rightBox = CssBox.CreateBox(parentBox, badBox.HtmlTag);
                    rightBox.InheritStyle(badBox, true);

                    if (parentBox.Boxes.Count > 2)
                        rightBox.SetBeforeBox(parentBox.Boxes[1]);

                    if (splitBox.ParentBox != null)
                        splitBox.SetBeforeBox(rightBox);
                }
                else
                {
                    rightBox = parentBox.Boxes[2];
                }

                rightBox.SetAllBoxes(badBox);
            }
            else if (splitBox.ParentBox != null && parentBox.Boxes.Count > 1)
            {
                splitBox.SetBeforeBox(parentBox.Boxes[1]);
                if (splitBox.HtmlTag is { Name: "br" } && (leftbox != null || leftBlock.Boxes.Count > 1))
                    splitBox.Display = CssConstants.Inline;
            }
        }

        /// <summary>
        /// Makes block boxes be among only block boxes and all inline boxes have block parent box.<br/>
        /// Inline boxes should live in a pool of Inline boxes only so they will define a single block.<br/>
        /// At the end of this process a block box will have only block siblings and inline box will have
        /// only inline siblings.
        /// </summary>
        /// <param name="box">the current box to correct its sub-tree</param>
        private static void CorrectInlineBoxesParent(CssBox box)
        {
            if (ContainsVariantBoxes(box))
            {
                for (int i = 0; i < box.Boxes.Count; i++)
                {
                    if (box.Boxes[i].IsInline)
                    {
                        var newbox = CssBox.CreateBlock(box, null, box.Boxes[i++]);
                        while (i < box.Boxes.Count && box.Boxes[i].IsInline)
                        {
                            box.Boxes[i].ParentBox = newbox;
                        }
                    }
                }
            }

            if (!DomUtils.ContainsInlinesOnly(box))
            {
                foreach (var childBox in box.Boxes)
                {
                    CorrectInlineBoxesParent(childBox);
                }
            }
        }


        private static void CorrectAbsolutelyPositionedInlineElements(CssBox box)
        {
            if (box is { Display: CssConstants.Inline, Position: CssConstants.Absolute })
            {
                var blockBox = new CssBox(box.ParentBox, null);
                blockBox.Display = CssConstants.Block;
                blockBox.Position = CssConstants.Absolute;
                blockBox.Left = box.Left;
                blockBox.Top = box.Top;
                blockBox.Bottom = box.Bottom;
                blockBox.Right = box.Right;
                blockBox.Width = box.Width;
                blockBox.Height = box.Height;
                blockBox.TextAlign = box.TextAlign;

                box.Position = CssConstants.Static;
                box.ParentBox = blockBox;
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAbsolutelyPositionedInlineElements(childBox);
            }
        }

        /// <summary>
        /// Corrects the missing elements in tables per https://www.w3.org/TR/CSS2/tables.html#anonymous-boxes
        /// </summary>
        /// <param name="box"></param>
        private static void CorrectAnonymousTables(CssBox box)
        {
            // 1. Remove irrelevant boxes
            CorrectAnonymousTablesRemoveIrrelevantBoxes(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesRemoveIrrelevantBoxes(childBox);
            }


            // 2. Generate missing child wrappers
            CorrectAnonymousTablesGenerateMissingChildWrappers(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesGenerateMissingChildWrappers(childBox);
            }

            // 3. Generate Missing Parents
            CorrectAnonymousTablesGenerateMissingParents(box);

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTablesGenerateMissingParents(childBox);
            }

            foreach (var childBox in box.Boxes.ToArray())
            {
                CorrectAnonymousTables(childBox);
            }
        }

        private static void CorrectAnonymousTablesRemoveIrrelevantBoxes(CssBox box)
        {
            // 1.1 All child boxes of a 'table-column' parent are treated as if they had 'display: none'
            if (box.Display is CssConstants.TableColumn)
            {
                foreach (var childBox in box.Boxes)
                {
#if DEBUG
                    Console.WriteLine($"dom: set child box {childBox.Id} of table-column parent {box.Id} to display: none");
#endif

                    childBox.Display = CssConstants.None;
                }
            }

            // 1.2 If a child C of a 'table-column-group' parent is not a 'table-column' box, then it is treated as if it had 'display: none'.
            if (box.ParentBox?.Display is CssConstants.TableColumnGroup && box.Display is not CssConstants.TableColumn)
            {
#if DEBUG
                Console.WriteLine($"dom: set child box {box.Id} to display:none if parent is table-column-group and child is not table-column");
#endif

                box.Display = CssConstants.None;
            }

            // 1.3 This is handled via CorrectTextBoxes above
            // 1.4 This is handled via CorrectTextBoxes above
        }

        private static void CorrectAnonymousTablesGenerateMissingChildWrappers(CssBox box)
        {
            // 2.1 If a child C of a 'table' or 'inline-table' box is not a proper table child, then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are not proper table children.
            if (box.ParentBox?.Display is CssConstants.Table)
            {
                if (!DomUtils.IsProperTableChild(box))
                {
#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a proper table child and parent is a table, then generate table around element");
#endif

                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    box.ParentBox = tableRowBox;
                }
            }

            // 2.2 If a child C of a row group box is not a 'table-row' box, then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are not 'table-row' boxes.
            if (box.ParentBox?.IsTableRowGroupBox ?? false)
            {
                if (box.Display is not CssConstants.TableRow)
                {
#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a table row and parent is a table row group box, then generate table-row around element");
#endif

                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    box.ParentBox = tableRowBox;
                }
            }

            // 2.3 If a child C of a 'table-row' box is not a 'table-cell', then generate an anonymous 'table-cell' box around C and all consecutive siblings of C that are not 'table-cell' boxes.
            if (box.ParentBox?.Display is CssConstants.TableRow)
            {
                if (box.Display is not CssConstants.TableCell)
                {

#if DEBUG
                    Console.WriteLine($"dom: if box {box.Id} is not a table cell and parent is a table row, then generate table-row around element and following  elements");
#endif

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => sibling.Display is CssConstants.TableCell, true)
                            .ToList();

                    var tableCellBox = new CssBox(box.ParentBox, null);
                    tableCellBox.Display = CssConstants.TableCell;
                    box.ParentBox = tableCellBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableCellBox);
                }
            }
        }

        private static void CorrectAnonymousTablesGenerateMissingParents(CssBox box)
        {
            // 3.1 For each 'table-cell' box C in a sequence of consecutive internal table and 'table-caption' siblings, if C's parent is not a 'table-row' then generate an anonymous 'table-row' box around C and all consecutive siblings of C that are 'table-cell' boxes.
            if (box.Display is CssConstants.TableCell)
            {
                if (box.ParentBox?.Display is not CssConstants.TableRow)
                {
                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, sibling => sibling.Display is CssConstants.TableCell, true)
                            .ToList();

                    var tableRowBox = new CssBox(box.ParentBox, null);
                    tableRowBox.Display = CssConstants.TableRow;
                    box.ParentBox = tableRowBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableRowBox);
                }
            }

            // 3.2 For each proper table child C in a sequence of consecutive proper table children, if C is misparented then generate an anonymous 'table' or 'inline-table' box T around C and all consecutive siblings of C that are proper table children. (If C's parent is an 'inline' box, then T must be an 'inline-table' box; otherwise it must be a 'table' box.)
            // - A 'table-row' is misparented if its parent is neither a row group box nor a 'table' or 'inline-table' box.
            // - A 'table-column' box is misparented if its parent is neither a 'table-column-group' box nor a 'table' or 'inline-table' box.
            // - A row group box, 'table-column-group' box, or 'table-caption' box is misparented if its parent is neither a 'table' box nor an 'inline-table' box.

            if (DomUtils.IsProperTableChild(box))
            {
                var isMissingParent = box.ParentBox is null;
                var isParentNotTable = box.ParentBox?.Display is not CssConstants.Table;
                var isParentNotInlineTable = box.ParentBox?.Display is not CssConstants.InlineTable;

                var isMisparented = isMissingParent && isParentNotTable && isParentNotInlineTable;

                if (isMisparented)
                {
                    var parentDisplay = box.ParentBox is null || box.ParentBox.IsBlock ? CssConstants.Table : CssConstants.InlineTable;

                    var followingMatchingSiblings =
                        DomUtils.GetFollowingSiblings(box, DomUtils.IsProperTableChild, true)
                            .ToList();

                    var tableBox = new CssBox(box.ParentBox, null);
                    tableBox.Display = parentDisplay;
                    box.ParentBox = tableBox;

                    followingMatchingSiblings.ForEach(sib => sib.ParentBox = tableBox);
                }
            }

        }

        /// <summary>
        /// Check if the given box contains only inline child boxes in all subtree.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - only inline child boxes, false - otherwise</returns>
        private static bool ContainsInlinesOnlyDeep(CssBox box)
        {
            foreach (var childBox in box.Boxes)
            {
                if (!childBox.IsInline || !ContainsInlinesOnlyDeep(childBox))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Check if the given box contains inline and block child boxes.
        /// </summary>
        /// <param name="box">the box to check</param>
        /// <returns>true - has variant child boxes, false - otherwise</returns>
        private static bool ContainsVariantBoxes(CssBox box)
        {
            bool hasBlock = false;
            bool hasInline = false;
            for (int i = 0; i < box.Boxes.Count && (!hasBlock || !hasInline); i++)
            {
                var isBlock = !box.Boxes[i].IsInline;
                hasBlock = hasBlock || isBlock;
                hasInline = hasInline || !isBlock;
            }

            return hasBlock && hasInline;
        }

        #endregion
    }
}