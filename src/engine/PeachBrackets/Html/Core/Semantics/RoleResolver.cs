using PeachBrackets.Accessibility;
using PeachBrackets.Html.Dom;
using System;
using System.Collections.Generic;

namespace PeachBrackets.Html.Semantics
{
    /// <summary>What the elements around an element say about it: the places where the same element has a different role.</summary>
    /// <param name="InSectioning">inside an <c>article</c>, <c>aside</c>, <c>main</c>, <c>nav</c> or <c>section</c>, where a <c>header</c> or <c>footer</c> is not a page landmark</param>
    /// <param name="InList">inside a list, where an <c>li</c> is a list item</param>
    /// <param name="InGrid">inside a grid, where a <c>td</c> is a grid cell</param>
    /// <param name="FieldsetDisabled">inside a disabled <c>fieldset</c>, which disables the controls in it</param>
    /// <param name="Language">the language set by the nearest <c>lang</c> attribute</param>
    /// <param name="SuppressText">whether the text below is already the name of the node it is in, so no text nodes are made for it</param>
    internal readonly record struct SemanticContext(
        bool InSectioning, bool InList, bool InGrid, bool FieldsetDisabled, string? Language, bool SuppressText);

    /// <summary>
    /// Decides what role an element has: the one its <c>role</c> attribute names if that is a role it may have, otherwise the one HTML gives
    /// the element (the HTML Accessibility API Mappings), which can depend on its attributes and on the elements around it.
    /// </summary>
    internal static class RoleResolver
    {
        private static readonly Dictionary<string, AccessibilityRole> RoleNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["alert"] = AccessibilityRole.Alert, ["alertdialog"] = AccessibilityRole.AlertDialog, ["application"] = AccessibilityRole.Application,
            ["article"] = AccessibilityRole.Article, ["banner"] = AccessibilityRole.Banner, ["blockquote"] = AccessibilityRole.BlockQuote,
            ["button"] = AccessibilityRole.Button, ["caption"] = AccessibilityRole.Caption, ["cell"] = AccessibilityRole.Cell,
            ["checkbox"] = AccessibilityRole.CheckBox, ["code"] = AccessibilityRole.Code, ["columnheader"] = AccessibilityRole.ColumnHeader,
            ["combobox"] = AccessibilityRole.ComboBox, ["complementary"] = AccessibilityRole.Complementary, ["contentinfo"] = AccessibilityRole.ContentInfo,
            ["definition"] = AccessibilityRole.Definition, ["deletion"] = AccessibilityRole.Deletion, ["dialog"] = AccessibilityRole.Dialog,
            ["directory"] = AccessibilityRole.Directory, ["document"] = AccessibilityRole.Document, ["emphasis"] = AccessibilityRole.Emphasis,
            ["feed"] = AccessibilityRole.Feed, ["figure"] = AccessibilityRole.Figure, ["form"] = AccessibilityRole.Form,
            ["generic"] = AccessibilityRole.Generic, ["grid"] = AccessibilityRole.Grid, ["gridcell"] = AccessibilityRole.GridCell,
            ["group"] = AccessibilityRole.Group, ["heading"] = AccessibilityRole.Heading, ["img"] = AccessibilityRole.Image,
            ["image"] = AccessibilityRole.Image, ["insertion"] = AccessibilityRole.Insertion, ["link"] = AccessibilityRole.Link,
            ["list"] = AccessibilityRole.List, ["listbox"] = AccessibilityRole.ListBox, ["listitem"] = AccessibilityRole.ListItem,
            ["log"] = AccessibilityRole.Log, ["main"] = AccessibilityRole.Main, ["marquee"] = AccessibilityRole.Marquee,
            ["math"] = AccessibilityRole.Math, ["meter"] = AccessibilityRole.Meter, ["menu"] = AccessibilityRole.Menu,
            ["menubar"] = AccessibilityRole.MenuBar, ["menuitem"] = AccessibilityRole.MenuItem, ["menuitemcheckbox"] = AccessibilityRole.MenuItemCheckBox,
            ["menuitemradio"] = AccessibilityRole.MenuItemRadio, ["navigation"] = AccessibilityRole.Navigation, ["none"] = AccessibilityRole.Presentation,
            ["note"] = AccessibilityRole.Note, ["option"] = AccessibilityRole.Option, ["paragraph"] = AccessibilityRole.Paragraph,
            ["presentation"] = AccessibilityRole.Presentation, ["progressbar"] = AccessibilityRole.ProgressBar, ["radio"] = AccessibilityRole.Radio,
            ["radiogroup"] = AccessibilityRole.RadioGroup, ["region"] = AccessibilityRole.Region, ["row"] = AccessibilityRole.Row,
            ["rowgroup"] = AccessibilityRole.RowGroup, ["rowheader"] = AccessibilityRole.RowHeader, ["scrollbar"] = AccessibilityRole.ScrollBar,
            ["search"] = AccessibilityRole.Search, ["searchbox"] = AccessibilityRole.SearchBox, ["separator"] = AccessibilityRole.Separator,
            ["slider"] = AccessibilityRole.Slider, ["spinbutton"] = AccessibilityRole.SpinButton, ["status"] = AccessibilityRole.Status,
            ["strong"] = AccessibilityRole.Strong, ["subscript"] = AccessibilityRole.Subscript, ["superscript"] = AccessibilityRole.Superscript,
            ["switch"] = AccessibilityRole.Switch, ["tab"] = AccessibilityRole.Tab, ["table"] = AccessibilityRole.Table,
            ["tablist"] = AccessibilityRole.TabList, ["tabpanel"] = AccessibilityRole.TabPanel, ["term"] = AccessibilityRole.Term,
            ["textbox"] = AccessibilityRole.TextBox, ["time"] = AccessibilityRole.Time, ["timer"] = AccessibilityRole.Timer,
            ["toolbar"] = AccessibilityRole.ToolBar, ["tooltip"] = AccessibilityRole.ToolTip, ["tree"] = AccessibilityRole.Tree,
            ["treegrid"] = AccessibilityRole.TreeGrid, ["treeitem"] = AccessibilityRole.TreeItem, ["graphics-document"] = AccessibilityRole.GraphicsDocument,
        };

        /// <summary>The roles whose name may come from the content of the element (WAI-ARIA 1.2, "Name From: contents").</summary>
        private static readonly HashSet<AccessibilityRole> NameFromContent =
        [
            AccessibilityRole.Button, AccessibilityRole.Cell, AccessibilityRole.CheckBox, AccessibilityRole.ColumnHeader, AccessibilityRole.GridCell,
            AccessibilityRole.Heading, AccessibilityRole.Link, AccessibilityRole.MenuItem, AccessibilityRole.MenuItemCheckBox,
            AccessibilityRole.MenuItemRadio, AccessibilityRole.Option, AccessibilityRole.Radio, AccessibilityRole.Row, AccessibilityRole.RowHeader,
            AccessibilityRole.Switch, AccessibilityRole.Tab, AccessibilityRole.ToolTip, AccessibilityRole.TreeItem, AccessibilityRole.Caption,
            AccessibilityRole.Definition, AccessibilityRole.Term, AccessibilityRole.Code, AccessibilityRole.Emphasis, AccessibilityRole.Strong,
            AccessibilityRole.Subscript, AccessibilityRole.Superscript, AccessibilityRole.Deletion, AccessibilityRole.Insertion, AccessibilityRole.Time,
        ];

        /// <summary>Whether a role's name is made from what is inside the element.</summary>
        public static bool NamesFromContent(AccessibilityRole role) => NameFromContent.Contains(role);

        /// <summary>Whether an element's <c>role</c> attribute names a role it can have: the first token that is one, as WAI-ARIA lets a list fall back.</summary>
        public static bool TryGetExplicit(HtmlTag tag, out AccessibilityRole role)
        {
            role = AccessibilityRole.Generic;
            if (tag.TryGetAttribute("role") is not { Length: > 0 } value)
                return false;

            foreach (var token in value.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                if (RoleNames.TryGetValue(token, out role))
                    return true;
            }

            return false;
        }

        /// <summary>The role of an element, and the heading level if it is a heading.</summary>
        public static (AccessibilityRole Role, int Level) Resolve(CssBox box, HtmlTag tag, in SemanticContext context, bool focusable, bool hasAuthorName)
        {
            if (TryGetExplicit(tag, out var explicitRole))
            {
                // A presentational role is ignored on an element that can be focused: taking it out of the tree would take the control with it.
                if (!(explicitRole == AccessibilityRole.Presentation && focusable))
                    return (explicitRole, explicitRole == AccessibilityRole.Heading ? HeadingLevel(tag, 2) : 0);
            }

            var role = Native(box, tag, context, hasAuthorName);
            return (role, role == AccessibilityRole.Heading ? HeadingLevel(tag, NativeHeadingLevel(tag.Name)) : 0);
        }

        private static int NativeHeadingLevel(string name) =>
            name.Length == 2 && (name[0] is 'h' or 'H') && name[1] is >= '1' and <= '6' ? name[1] - '0' : 2;

        private static int HeadingLevel(HtmlTag tag, int fallback) =>
            tag.TryGetAttribute("aria-level") is { } level && int.TryParse(level, out var value) && value >= 1 ? value : fallback;

        private static AccessibilityRole Native(CssBox box, HtmlTag tag, in SemanticContext context, bool hasAuthorName)
        {
            switch (tag.Name.ToLowerInvariant())
            {
                case "a" or "area": return tag.HasAttribute("href") ? AccessibilityRole.Link : AccessibilityRole.Generic;
                case "article": return AccessibilityRole.Article;
                case "aside": return AccessibilityRole.Complementary;
                case "blockquote": return AccessibilityRole.BlockQuote;
                case "button": return AccessibilityRole.Button;
                case "caption": return AccessibilityRole.Caption;
                case "code": return AccessibilityRole.Code;
                case "dd": return AccessibilityRole.Definition;
                case "del" or "s": return AccessibilityRole.Deletion;
                case "details" or "fieldset" or "optgroup" or "address": return AccessibilityRole.Group;
                case "dfn" or "dt": return AccessibilityRole.Term;
                case "dialog": return AccessibilityRole.Dialog;
                case "em": return AccessibilityRole.Emphasis;
                case "figure": return AccessibilityRole.Figure;
                case "footer": return context.InSectioning ? AccessibilityRole.Generic : AccessibilityRole.ContentInfo;
                case "form": return hasAuthorName ? AccessibilityRole.Form : AccessibilityRole.Generic;
                case "h1" or "h2" or "h3" or "h4" or "h5" or "h6": return AccessibilityRole.Heading;
                case "header": return context.InSectioning ? AccessibilityRole.Generic : AccessibilityRole.Banner;
                case "hr": return AccessibilityRole.Separator;
                case "img": return box is CssBoxImage || tag.HasAttribute("src") || tag.HasAttribute("alt") ? ImageRole(tag, hasAuthorName) : AccessibilityRole.Generic;
                case "input": return Input(tag);
                case "ins": return AccessibilityRole.Insertion;
                case "li": return context.InList ? AccessibilityRole.ListItem : AccessibilityRole.Generic;
                case "main": return AccessibilityRole.Main;
                case "math": return AccessibilityRole.Math;
                case "menu" or "ol" or "ul": return AccessibilityRole.List;
                case "meter": return AccessibilityRole.Meter;
                case "nav": return AccessibilityRole.Navigation;
                case "option": return AccessibilityRole.Option;
                case "output": return AccessibilityRole.Status;
                case "p": return AccessibilityRole.Paragraph;
                case "progress": return AccessibilityRole.ProgressBar;
                case "search": return AccessibilityRole.Search;
                case "section": return hasAuthorName ? AccessibilityRole.Region : AccessibilityRole.Generic;
                case "select": return tag.HasAttribute("multiple") || (int.TryParse(tag.TryGetAttribute("size"), out var size) && size > 1) ? AccessibilityRole.ListBox : AccessibilityRole.ComboBox;
                case "strong": return AccessibilityRole.Strong;
                case "sub": return AccessibilityRole.Subscript;
                case "sup": return AccessibilityRole.Superscript;
                case "summary": return AccessibilityRole.Button;
                case "svg": return AccessibilityRole.GraphicsDocument;
                case "table": return AccessibilityRole.Table;
                case "tbody" or "thead" or "tfoot": return AccessibilityRole.RowGroup;
                case "td": return context.InGrid ? AccessibilityRole.GridCell : AccessibilityRole.Cell;
                case "textarea": return AccessibilityRole.TextBox;
                case "th": return tag.TryGetAttribute("scope")?.ToLowerInvariant() is "row" or "rowgroup" ? AccessibilityRole.RowHeader : AccessibilityRole.ColumnHeader;
                case "time": return AccessibilityRole.Time;
                case "tr": return AccessibilityRole.Row;
                default: return AccessibilityRole.Generic;
            }
        }

        // An image with an empty alt is decoration: it says nothing, unless the author named it some other way.
        private static AccessibilityRole ImageRole(HtmlTag tag, bool hasAuthorName) =>
            tag.TryGetAttribute("alt") is { Length: 0 } && !hasAuthorName ? AccessibilityRole.Presentation : AccessibilityRole.Image;

        private static AccessibilityRole Input(HtmlTag tag)
        {
            var type = tag.TryGetAttribute("type")?.Trim().ToLowerInvariant() ?? "text";
            var hasList = tag.HasAttribute("list");
            return type switch
            {
                "button" or "submit" or "reset" or "image" or "color" or "file" => AccessibilityRole.Button,
                "checkbox" => AccessibilityRole.CheckBox,
                "radio" => AccessibilityRole.Radio,
                "range" => AccessibilityRole.Slider,
                "number" => AccessibilityRole.SpinButton,
                "search" => hasList ? AccessibilityRole.ComboBox : AccessibilityRole.SearchBox,
                "hidden" => AccessibilityRole.Generic,
                "date" or "datetime-local" or "month" or "time" or "week" => AccessibilityRole.Group,
                _ => hasList ? AccessibilityRole.ComboBox : AccessibilityRole.TextBox,
            };
        }
    }
}
