using System;
using System.Collections.Generic;
using PeachPDF.Html.Core.Entities;

namespace PeachPDF.Html.Core.Dom
{
    /// <summary>
    /// The generic copy-on-write mechanisms shared by <see cref="ComputedStyle"/> and every area record
    /// below. Declared once here rather than as instance methods on 17 separate record types
    /// (<see cref="ComputedStyle"/> plus the 16 areas).
    /// </summary>
    internal static class ComputedStyleCow
    {
        /// <summary>
        /// Produces the record that results from changing one property to <paramref name="newValue"/> -
        /// or, if it already holds that value, returns <paramref name="self"/> unchanged. Safe to call
        /// unconditionally regardless of whether <paramref name="self"/> is a shared <c>Default</c>
        /// singleton or already customized, and a no-op write allocates nothing. Used at the "leaf"
        /// level, where <paramref name="currentValue"/>/<paramref name="newValue"/> are the actual CSS
        /// property value (a string, a <see cref="CssImage"/>, etc.) - see <see cref="AdoptArea{TSelf,TArea}"/>
        /// for the analogous operation one level up, where the value being compared is a whole area record.
        /// </summary>
        internal static TSelf SetPropertyValue<TSelf, TValue>(this TSelf self, TValue currentValue, TValue newValue, Func<TSelf, TValue, TSelf> apply) =>
            EqualityComparer<TValue>.Default.Equals(currentValue, newValue) ? self : apply(self, newValue);

        /// <summary>
        /// The area-level counterpart to <see cref="SetPropertyValue{TSelf,TValue}"/>: swaps
        /// <paramref name="newArea"/> onto <paramref name="self"/> (a <see cref="ComputedStyle"/>) unless
        /// it's already there. Compares by <em>reference</em>, not by structural (record) equality -
        /// deliberately, for two reasons. First, every call site passes a <paramref name="newArea"/> that
        /// is either exactly <paramref name="currentArea"/> (when the leaf-level <see cref="SetPropertyValue{TSelf,TValue}"/>
        /// call that produced it was itself a no-op) or a freshly-forked, therefore-necessarily-different
        /// object (when it changed a field) - so a structural comparison here would always agree with a
        /// reference comparison anyway, at the cost of scanning every field of the area for nothing.
        /// Second, in <see cref="CssBox.InheritStyle"/>'s whole-area adoption, <paramref name="currentArea"/>
        /// and <paramref name="newArea"/> come from two unrelated boxes - using reference equality there
        /// means two boxes whose areas happen to be equal in content but are separate objects still get
        /// unified onto the same shared instance, which is what makes the "every box in a subtree that
        /// never overrides an area ends up ReferenceEquals-sharing it" guarantee (see
        /// <see cref="ComputedStyle"/>'s remarks) actually hold, rather than only holding when a child
        /// happens to start from the shared <c>Default</c>.
        /// </summary>
        internal static TSelf AdoptArea<TSelf, TArea>(this TSelf self, TArea currentArea, TArea newArea, Func<TSelf, TArea, TSelf> apply)
            where TArea : class =>
            ReferenceEquals(currentArea, newArea) ? self : apply(self, newArea);
    }

    /// <summary>
    /// The one field with no <c>css-properties.json</c> entry - not a real CSS property (no
    /// <c>PropertyNames</c>/<c>CssDefaults</c> entry), a PeachPDF-internal companion to <c>FontFamily</c>
    /// carrying the full unresolved font-family list. Every other <see cref="FontArea"/> field (including
    /// <c>FontFamily</c> itself) is generated - see <c>ComputedStyleAreas.g.cs</c>.
    /// </summary>
    internal sealed partial record FontArea
    {
        public string? FontFamilyList { get; init; }
    }

    /// <summary>
    /// The one field with no <c>css-properties.json</c> propertyPath of its own - not a real CSS
    /// property, a PeachPDF-internal companion to <c>Position</c> carrying the <c>&lt;custom-ident&gt;</c>
    /// argument of css-gcpm-3's <c>running(&lt;custom-ident&gt;)</c> (relevant only when
    /// <c>Position.Value == PositionMode.Running</c>). Every other <see cref="DisplayPositioningArea"/>
    /// field (including <c>Position</c> itself) is generated - see <c>ComputedStyleAreas.g.cs</c>.
    /// </summary>
    internal sealed partial record DisplayPositioningArea
    {
        public string? RunningElementName { get; init; }
    }
}
