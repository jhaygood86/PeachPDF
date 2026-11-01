using PeachDrawing.Core;
using PeachBrackets.Svg;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PeachBrackets.Engine.Tests.Svg
{
    /// <summary>
    /// CSS animations on the elements of an inline <c>&lt;svg&gt;</c> against Chrome: each row is a stylesheet and an element, a point of the run, and what Chrome
    /// computes for the element at that point (a paused animation with a negative delay, <c>getComputedStyle</c>; Chrome 155). Generated: the cases are in
    /// <see cref="Cases"/>, the values in <see cref="ChromeValues"/>. A shape's expected string is
    /// <c>fill;stroke;fill-opacity;stroke-opacity;opacity;stroke-width;stroke-dashoffset;transform</c>, a gradient stop's is its colour with the stop-opacity folded into the alpha.
    /// </summary>
    public class SvgCssAnimationChromeTests
    {
        private static readonly Dictionary<string, (string Css, string Body)> Cases = new()
        {
            ["F0_fill"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F1_fill"] = ("@keyframes k { from { fill: red } to { fill: lime } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F2_fill"] = ("@keyframes k { from { fill: hsl(0 100% 50%) } to { fill: hsl(240 100% 50%) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F3_fill"] = ("@keyframes k { from { fill: rgb(255 0 0 / .2) } to { fill: rgb(0 0 255 / .8) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F4_fill"] = ("@keyframes k { from { fill: lab(50 40 20) } to { fill: oklch(0.5 0.15 260) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F5_fill"] = ("@keyframes k { from { fill: #f00 } to { fill: transparent } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["F6_fill"] = ("@keyframes k { from { fill: #336699 } to { fill: #cc9933 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["U1_to_only_attr"] = ("@keyframes k { to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#ff0000\"/>"),
            ["U2_from_only_attr"] = ("@keyframes k { from { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#ff0000\"/>"),
            ["U3_to_only_rule"] = ("@keyframes k { to { fill: #0000ff } }rect { fill: #ff0000 } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["U4_to_only_inline"] = ("@keyframes k { to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" style=\"fill: #ff0000\"/>"),
            ["U5_to_only_initial"] = ("@keyframes k { to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["U6_to_only_inherited"] = ("@keyframes k { to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<g fill=\"#ff0000\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["U7_to_only_inherited_deep"] = ("@keyframes k { to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<g fill=\"#ff0000\"><g><rect data-r width=\"10\" height=\"10\"/></g></g>"),
            ["U8_opacity_not_inherited"] = ("@keyframes k { to { opacity: 0 } }rect { animation: k 1s linear;  }", "<g opacity=\"0.5\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["U9_opacity_to_only"] = ("@keyframes k { to { opacity: 0.2 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["U10_inherit_from_parent_animated"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }g { animation: k 1s linear;  }", "<g><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["U11_child_to_only_with_animated_parent"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }@keyframes k2 { to { fill: #00ff00 } }g { animation: k 1s linear;  }rect { animation: k2 1s linear;  }", "<g><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["P1_important_rule_beats_animation"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { fill: #00ff00 !important } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["P2_animation_beats_inline"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" style=\"fill: #00ff00\"/>"),
            ["P3_animation_beats_rule"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { fill: #00ff00 } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["P4_animation_beats_attr"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
            ["P7_inline_important_beats_animation"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" style=\"fill: #00ff00 !important\"/>"),
            ["P8_important_other_property_untouched"] = ("@keyframes k { from { fill: #ff0000; opacity: 0 } to { fill: #0000ff; opacity: 1 } }rect { fill: #00ff00 !important } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["P5_inline_animation_prop"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }", "<rect data-r width=\"10\" height=\"10\" style=\"animation: k 1s linear\"/>"),
            ["P6_class_selector"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }.a { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" class=\"a\"/><rect data-r width=\"10\" height=\"10\" class=\"b\"/>"),
            ["O0_opacity"] = ("@keyframes k { from { opacity: 0 } to { opacity: 1 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000000\"/>"),
            ["O1_fill-opacity"] = ("@keyframes k { from { fill-opacity: 1 } to { fill-opacity: 0.2 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000000\"/>"),
            ["O2_stroke-opacity"] = ("@keyframes k { from { stroke-opacity: 0.2 } to { stroke-opacity: 0.8 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000000\"/>"),
            ["O_over_ov1_opacity"] = ("@keyframes k { from { opacity: 0.2 } to { opacity: 0.8 } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.5, 2, .5, -1) }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["O_over_ov1_fillop"] = ("@keyframes k { from { fill-opacity: 0.2 } to { fill-opacity: 0.8 } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.5, 2, .5, -1) }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["O_over_ov1_strokew"] = ("@keyframes k { from { stroke-width: 2px } to { stroke-width: 8px } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.5, 2, .5, -1) }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["O_over_ov2_opacity"] = ("@keyframes k { from { opacity: 0.2 } to { opacity: 0.8 } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.3, 1.8, .7, 1.4) }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["O_over_ov2_fillop"] = ("@keyframes k { from { fill-opacity: 0.2 } to { fill-opacity: 0.8 } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.3, 1.8, .7, 1.4) }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["O_over_ov2_strokew"] = ("@keyframes k { from { stroke-width: 2px } to { stroke-width: 8px } }rect { animation: k 1s linear; animation-timing-function: cubic-bezier(.3, 1.8, .7, 1.4) }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["S1_strokew_px"] = ("@keyframes k { from { stroke-width: 2px } to { stroke-width: 10px } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["S2_strokew_unitless"] = ("@keyframes k { from { stroke-width: 2 } to { stroke-width: 10 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["S3_strokew_mixed_units"] = ("@keyframes k { from { stroke-width: 2 } to { stroke-width: 10px } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["S4_strokew_to_only_attr"] = ("@keyframes k { to { stroke-width: 10 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\" stroke-width=\"2\"/>"),
            ["S5_strokew_inherited_underlying"] = ("@keyframes k { to { stroke-width: 10 } }rect { animation: k 1s linear;  }", "<g stroke-width=\"2\" stroke=\"#000\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["S6_dashoffset_line_drawing"] = ("@keyframes k { from { stroke-dashoffset: 100 } to { stroke-dashoffset: 0 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\" stroke-dasharray=\"100\"/>"),
            ["S7_dashoffset_to_only"] = ("@keyframes k { to { stroke-dashoffset: -50 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\" stroke-dasharray=\"10 5\"/>"),
            ["S8_stroke_color"] = ("@keyframes k { from { stroke: #ff0000 } to { stroke: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["S9_stroke_none_to_color"] = ("@keyframes k { from { stroke: none } to { stroke: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["S10_dasharray"] = ("@keyframes k { from { stroke-dasharray: 2 4 } to { stroke-dasharray: 10 20 } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" stroke=\"#000\"/>"),
            ["T1_rotate"] = ("@keyframes k { from { transform: rotate(0deg) } to { transform: rotate(90deg) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T2_translate"] = ("@keyframes k { from { transform: translateX(0px) } to { transform: translateX(40px) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T3_scale"] = ("@keyframes k { from { transform: scale(1) } to { transform: scale(3) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T4_mismatched"] = ("@keyframes k { from { transform: translateX(0px) } to { transform: rotate(90deg) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T5_list"] = ("@keyframes k { from { transform: translate(0px, 0px) rotate(0deg) } to { transform: translate(40px, 20px) rotate(180deg) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T6_to_only_none"] = ("@keyframes k { to { transform: rotate(90deg) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T7_to_only_attr"] = ("@keyframes k { to { transform: translateX(40px) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" transform=\"translate(10 0)\"/>"),
            ["T8_turn_unit"] = ("@keyframes k { from { transform: rotate(0turn) } to { transform: rotate(1turn) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["T9_group"] = ("@keyframes k { from { transform: translateX(0px) } to { transform: translateX(40px) } }g { animation: k 1s linear;  }", "<g><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["T10_skew_scale"] = ("@keyframes k { from { transform: skewX(0deg) scale(1, 1) } to { transform: skewX(30deg) scale(2, 3) } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["D1_visibility"] = ("@keyframes k { from { visibility: hidden } to { visibility: visible } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M1_two_animations"] = ("@keyframes a { from { fill: #ff0000 } to { fill: #0000ff } }@keyframes b { from { opacity: 0 } to { opacity: 1 } }rect { animation: a 1s linear, b 1s linear }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M2_later_wins"] = ("@keyframes a { from { fill: #ff0000 } to { fill: #0000ff } }@keyframes b { from { fill: #00ff00 } to { fill: #ffff00 } }rect { animation: a 1s linear, b 1s linear }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M3_later_implicit_from_earlier"] = ("@keyframes a { from { fill: #ff0000 } to { fill: #0000ff } }@keyframes b { to { fill: #00ff00 } }rect { animation: a 1s linear, b 1s linear }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M4_alternate"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 0.5s linear 3 alternate }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M5_reverse"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear reverse }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M6_fill_forwards"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 0.5s linear forwards }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M7_no_fill_after_end"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 0.5s linear }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
            ["M8_ease_in_out"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s ease-in-out }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M9_steps"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s steps(4) }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M10_multi_keyframes"] = ("@keyframes k { 0% { fill: #ff0000 } 30% { fill: #00ff00 } 100% { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M11_keyframe_easing"] = ("@keyframes k { from { fill: #ff0000; animation-timing-function: step-end } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["M12_paused_state_ignored_elsewhere"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/><rect data-r width=\"10\" height=\"10\" class=\"x\"/>"),
            ["K1_inherit"] = ("@keyframes k { from { fill: #ff0000 } to { fill: inherit } }rect { animation: k 1s linear;  }", "<g fill=\"#0000ff\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["K2_initial"] = ("@keyframes k { from { fill: #ff0000 } to { fill: initial } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["K3_unset_inherited"] = ("@keyframes k { from { fill: #ff0000 } to { fill: unset } }rect { animation: k 1s linear;  }", "<g fill=\"#0000ff\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["K4_unset_not_inherited"] = ("@keyframes k { from { opacity: 0.2 } to { opacity: unset } }rect { animation: k 1s linear;  }", "<g opacity=\"0.5\"><rect data-r width=\"10\" height=\"10\"/></g>"),
            ["K5_var"] = ("@keyframes k { from { fill: var(--a) } to { fill: var(--b) } }:root { --a: #ff0000; --b: #0000ff } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["K6_currentcolor"] = ("@keyframes k { from { fill: #ff0000 } to { fill: currentcolor } }svg { color: #0000ff } rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\"/>"),
            ["K7_revert"] = ("@keyframes k { from { fill: #ff0000 } to { fill: revert } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
            ["K8_revert_layer"] = ("@keyframes k { from { fill: #ff0000 } to { fill: revert-layer } }rect { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
            ["K9_revert_inherited_parent"] = ("@keyframes k { from { fill: #ff0000 } to { fill: revert } }rect { animation: k 1s linear;  }", "<g fill=\"#0000ff\"><rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/></g>"),
            ["G1_stop_color"] = ("@keyframes k { from { stop-color: #ff0000 } to { stop-color: #0000ff } }stop { animation: k 1s linear;  }", "<defs><linearGradient id=\"g\"><stop data-r offset=\"0\" stop-color=\"#00ff00\"/></linearGradient></defs>"),
            ["G2_stop_opacity"] = ("@keyframes k { from { stop-opacity: 1 } to { stop-opacity: 0 } }stop { animation: k 1s linear;  }", "<defs><linearGradient id=\"g\"><stop data-r offset=\"0\" stop-color=\"#00ff00\"/></linearGradient></defs>"),
            ["G3_stop_to_only"] = ("@keyframes k { to { stop-color: #0000ff } }stop { animation: k 1s linear;  }", "<defs><linearGradient id=\"g\"><stop data-r offset=\"0\" stop-color=\"#ff0000\"/></linearGradient></defs>"),
            ["G4_stop_to_only_rule"] = ("@keyframes k { to { stop-color: #0000ff } }stop { stop-color: #ff0000 } stop { animation: k 1s linear;  }", "<defs><linearGradient id=\"g\"><stop data-r offset=\"0\"/></linearGradient></defs>"),
            ["G5_stop_initial"] = ("@keyframes k { to { stop-color: #0000ff } }stop { animation: k 1s linear;  }", "<defs><linearGradient id=\"g\"><stop data-r offset=\"0\"/></linearGradient></defs>"),
            ["N1_other_element_untouched"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }.a { animation: k 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" class=\"a\"/><rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\" class=\"b\"/>"),
            ["N2_unknown_keyframes"] = ("rect { animation: nope 1s linear;  }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
            ["N3_none"] = ("@keyframes k { from { fill: #ff0000 } to { fill: #0000ff } }rect { animation: none }", "<rect data-r width=\"10\" height=\"10\" fill=\"#00ff00\"/>"),
        };

        public static TheoryData<string, double, string, string> ChromeValues => new()
        {
            { "F0_fill", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F0_fill", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F0_fill", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F1_fill", 0.25, "fill=191,64,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F1_fill", 0.5, "fill=128,128,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F1_fill", 0.75, "fill=64,191,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F2_fill", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F2_fill", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F2_fill", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F3_fill", 0.25, "fill=109,0,146,89;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F3_fill", 0.5, "fill=51,0,204,128;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F3_fill", 0.75, "fill=20,0,235,166;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F4_fill", 0.25, "fill=155,96,115,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F4_fill", 0.5, "fill=124,99,139,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F4_fill", 0.75, "fill=89,99,162,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F5_fill", 0.25, "fill=255,0,0,191;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F5_fill", 0.5, "fill=255,0,0,128;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F5_fill", 0.75, "fill=255,0,0,64;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F6_fill", 0.25, "fill=89,115,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F6_fill", 0.5, "fill=128,128,102,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "F6_fill", 0.75, "fill=166,140,77,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U1_to_only_attr", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U1_to_only_attr", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U1_to_only_attr", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U2_from_only_attr", 0.25, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U2_from_only_attr", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U2_from_only_attr", 0.75, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U3_to_only_rule", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U3_to_only_rule", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U3_to_only_rule", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U4_to_only_inline", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U4_to_only_inline", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U4_to_only_inline", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U5_to_only_initial", 0.25, "fill=0,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U5_to_only_initial", 0.5, "fill=0,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U5_to_only_initial", 0.75, "fill=0,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U6_to_only_inherited", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U6_to_only_inherited", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U6_to_only_inherited", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U7_to_only_inherited_deep", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U7_to_only_inherited_deep", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U7_to_only_inherited_deep", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U8_opacity_not_inherited", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.75;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U8_opacity_not_inherited", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.5;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U8_opacity_not_inherited", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.25;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U9_opacity_to_only", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.8;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U9_opacity_to_only", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.6;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U9_opacity_to_only", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.4;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U10_inherit_from_parent_animated", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U10_inherit_from_parent_animated", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U10_inherit_from_parent_animated", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U11_child_to_only_with_animated_parent", 0.25, "fill=143,64,48,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U11_child_to_only_with_animated_parent", 0.5, "fill=64,128,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "U11_child_to_only_with_animated_parent", 0.75, "fill=16,191,48,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P1_important_rule_beats_animation", 0.25, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P1_important_rule_beats_animation", 0.5, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P1_important_rule_beats_animation", 0.75, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P2_animation_beats_inline", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P2_animation_beats_inline", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P2_animation_beats_inline", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P3_animation_beats_rule", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P3_animation_beats_rule", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P3_animation_beats_rule", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P4_animation_beats_attr", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P4_animation_beats_attr", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P4_animation_beats_attr", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P7_inline_important_beats_animation", 0.25, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P7_inline_important_beats_animation", 0.5, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P7_inline_important_beats_animation", 0.75, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P8_important_other_property_untouched", 0.25, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=0.25;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P8_important_other_property_untouched", 0.5, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=0.5;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P8_important_other_property_untouched", 0.75, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=0.75;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P5_inline_animation_prop", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P5_inline_animation_prop", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P5_inline_animation_prop", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P6_class_selector", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P6_class_selector", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "P6_class_selector", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O0_opacity", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=0.25;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O0_opacity", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=0.5;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O0_opacity", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=0.75;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O1_fill-opacity", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=0.8;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O1_fill-opacity", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=0.6;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O1_fill-opacity", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=0.4;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O2_stroke-opacity", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=0.35;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O2_stroke-opacity", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=0.5;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O2_stroke-opacity", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=0.65;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_opacity", 0.1, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.414;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_opacity", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.609;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_opacity", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.5;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_opacity", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.391;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_opacity", 0.9, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.586;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_fillop", 0.1, "fill=0,0,0,255;stroke=none;fo=0.414;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_fillop", 0.25, "fill=0,0,0,255;stroke=none;fo=0.609;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_fillop", 0.5, "fill=0,0,0,255;stroke=none;fo=0.5;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_fillop", 0.75, "fill=0,0,0,255;stroke=none;fo=0.391;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_fillop", 0.9, "fill=0,0,0,255;stroke=none;fo=0.586;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_strokew", 0.1, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4.137;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_strokew", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6.094;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_strokew", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=5;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_strokew", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=3.906;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov1_strokew", 0.9, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=5.863;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_opacity", 0.1, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.504;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_opacity", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.797;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_opacity", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.995;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_opacity", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.964;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_opacity", 0.9, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.876;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_fillop", 0.1, "fill=0,0,0,255;stroke=none;fo=0.504;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_fillop", 0.25, "fill=0,0,0,255;stroke=none;fo=0.797;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_fillop", 0.5, "fill=0,0,0,255;stroke=none;fo=0.995;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_fillop", 0.75, "fill=0,0,0,255;stroke=none;fo=0.964;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_fillop", 0.9, "fill=0,0,0,255;stroke=none;fo=0.876;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_strokew", 0.1, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=5.043;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_strokew", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=7.968;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_strokew", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=9.95;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_strokew", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=9.637;do=0;tr=1,0,0,1,0,0", "" },
            { "O_over_ov2_strokew", 0.9, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8.758;do=0;tr=1,0,0,1,0,0", "" },
            { "S1_strokew_px", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4;do=0;tr=1,0,0,1,0,0", "" },
            { "S1_strokew_px", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6;do=0;tr=1,0,0,1,0,0", "" },
            { "S1_strokew_px", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8;do=0;tr=1,0,0,1,0,0", "" },
            { "S2_strokew_unitless", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4;do=0;tr=1,0,0,1,0,0", "" },
            { "S2_strokew_unitless", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6;do=0;tr=1,0,0,1,0,0", "" },
            { "S2_strokew_unitless", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8;do=0;tr=1,0,0,1,0,0", "" },
            { "S3_strokew_mixed_units", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4;do=0;tr=1,0,0,1,0,0", "" },
            { "S3_strokew_mixed_units", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6;do=0;tr=1,0,0,1,0,0", "" },
            { "S3_strokew_mixed_units", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8;do=0;tr=1,0,0,1,0,0", "" },
            { "S4_strokew_to_only_attr", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4;do=0;tr=1,0,0,1,0,0", "" },
            { "S4_strokew_to_only_attr", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6;do=0;tr=1,0,0,1,0,0", "" },
            { "S4_strokew_to_only_attr", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8;do=0;tr=1,0,0,1,0,0", "" },
            { "S5_strokew_inherited_underlying", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=4;do=0;tr=1,0,0,1,0,0", "" },
            { "S5_strokew_inherited_underlying", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=6;do=0;tr=1,0,0,1,0,0", "" },
            { "S5_strokew_inherited_underlying", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=8;do=0;tr=1,0,0,1,0,0", "" },
            { "S6_dashoffset_line_drawing", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=75;tr=1,0,0,1,0,0", "" },
            { "S6_dashoffset_line_drawing", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=50;tr=1,0,0,1,0,0", "" },
            { "S6_dashoffset_line_drawing", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=25;tr=1,0,0,1,0,0", "" },
            { "S7_dashoffset_to_only", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=-12.5;tr=1,0,0,1,0,0", "" },
            { "S7_dashoffset_to_only", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=-25;tr=1,0,0,1,0,0", "" },
            { "S7_dashoffset_to_only", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=-37.5;tr=1,0,0,1,0,0", "" },
            { "S8_stroke_color", 0.25, "fill=0,0,0,255;stroke=191,0,64,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S8_stroke_color", 0.5, "fill=0,0,0,255;stroke=128,0,128,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S8_stroke_color", 0.75, "fill=0,0,0,255;stroke=64,0,191,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S9_stroke_none_to_color", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S9_stroke_none_to_color", 0.5, "fill=0,0,0,255;stroke=0,0,255,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S9_stroke_none_to_color", 0.75, "fill=0,0,0,255;stroke=0,0,255,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S10_dasharray", 0.25, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S10_dasharray", 0.5, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "S10_dasharray", 0.75, "fill=0,0,0,255;stroke=0,0,0,255;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "T1_rotate", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.924,0.383,-0.383,0.924,0,0", "" },
            { "T1_rotate", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.707,0.707,-0.707,0.707,0,0", "" },
            { "T1_rotate", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.383,0.924,-0.924,0.383,0,0", "" },
            { "T2_translate", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,10,0", "" },
            { "T2_translate", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,20,0", "" },
            { "T2_translate", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,30,0", "" },
            { "T3_scale", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1.5,0,0,1.5,0,0", "" },
            { "T3_scale", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=2,0,0,2,0,0", "" },
            { "T3_scale", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=2.5,0,0,2.5,0,0", "" },
            { "T4_mismatched", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.924,0.383,-0.383,0.924,0,0", "" },
            { "T4_mismatched", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.707,0.707,-0.707,0.707,0,0", "" },
            { "T4_mismatched", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.383,0.924,-0.924,0.383,0,0", "" },
            { "T5_list", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.707,0.707,-0.707,0.707,10,5", "" },
            { "T5_list", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0,1,-1,0,20,10", "" },
            { "T5_list", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=-0.707,0.707,-0.707,-0.707,30,15", "" },
            { "T6_to_only_none", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.924,0.383,-0.383,0.924,0,0", "" },
            { "T6_to_only_none", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.707,0.707,-0.707,0.707,0,0", "" },
            { "T6_to_only_none", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0.383,0.924,-0.924,0.383,0,0", "" },
            { "T7_to_only_attr", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,17.5,0", "" },
            { "T7_to_only_attr", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,25,0", "" },
            { "T7_to_only_attr", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,32.5,0", "" },
            { "T8_turn_unit", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0,1,-1,0,0,0", "" },
            { "T8_turn_unit", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=-1,0,0,-1,0,0", "" },
            { "T8_turn_unit", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=0,-1,1,0,0,0", "" },
            { "T9_group", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "T9_group", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "T9_group", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "T10_skew_scale", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1.25,0,0.197,1.5,0,0", "" },
            { "T10_skew_scale", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1.5,0,0.536,2,0,0", "" },
            { "T10_skew_scale", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1.75,0,1.036,2.5,0,0", "" },
            { "D1_visibility", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "D1_visibility", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "D1_visibility", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M1_two_animations", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=0.25;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M1_two_animations", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=0.5;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M1_two_animations", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=0.75;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M2_later_wins", 0.25, "fill=64,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M2_later_wins", 0.5, "fill=128,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M2_later_wins", 0.75, "fill=191,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M3_later_implicit_from_earlier", 0.25, "fill=143,64,48,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M3_later_implicit_from_earlier", 0.5, "fill=64,128,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M3_later_implicit_from_earlier", 0.75, "fill=16,191,48,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M4_alternate", 0.1, "fill=179,0,77,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M4_alternate", 0.3, "fill=26,0,230,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M4_alternate", 0.6, "fill=204,0,51,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M4_alternate", 0.9, "fill=77,0,179,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M5_reverse", 0.25, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M5_reverse", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M5_reverse", 0.75, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M6_fill_forwards", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M6_fill_forwards", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M7_no_fill_after_end", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M7_no_fill_after_end", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M8_ease_in_out", 0.25, "fill=222,0,33,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M8_ease_in_out", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M8_ease_in_out", 0.75, "fill=33,0,222,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M9_steps", 0.1, "fill=255,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M9_steps", 0.3, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M9_steps", 0.6, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M9_steps", 0.9, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M10_multi_keyframes", 0.1, "fill=170,85,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M10_multi_keyframes", 0.3, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M10_multi_keyframes", 0.5, "fill=0,182,73,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M10_multi_keyframes", 0.9, "fill=0,36,219,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M11_keyframe_easing", 0.25, "fill=255,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M11_keyframe_easing", 0.5, "fill=255,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M11_keyframe_easing", 0.75, "fill=255,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M12_paused_state_ignored_elsewhere", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M12_paused_state_ignored_elsewhere", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "M12_paused_state_ignored_elsewhere", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K1_inherit", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K1_inherit", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K1_inherit", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K2_initial", 0.25, "fill=191,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K2_initial", 0.5, "fill=128,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K2_initial", 0.75, "fill=64,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K3_unset_inherited", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K3_unset_inherited", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K3_unset_inherited", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K4_unset_not_inherited", 0.25, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.4;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K4_unset_not_inherited", 0.5, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.6;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K4_unset_not_inherited", 0.75, "fill=0,0,0,255;stroke=none;fo=1;so=1;op=0.8;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K5_var", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K5_var", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K5_var", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K6_currentcolor", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K6_currentcolor", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K6_currentcolor", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K7_revert", 0.25, "fill=191,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K7_revert", 0.5, "fill=128,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K7_revert", 0.75, "fill=64,0,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K8_revert_layer", 0.25, "fill=191,64,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K8_revert_layer", 0.5, "fill=128,128,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K8_revert_layer", 0.75, "fill=64,191,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K9_revert_inherited_parent", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K9_revert_inherited_parent", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "K9_revert_inherited_parent", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "G1_stop_color", 0.25, "", "191,0,64,255" },
            { "G1_stop_color", 0.5, "", "128,0,128,255" },
            { "G1_stop_color", 0.75, "", "64,0,191,255" },
            { "G2_stop_opacity", 0.25, "", "0,255,0,191" },
            { "G2_stop_opacity", 0.5, "", "0,255,0,128" },
            { "G2_stop_opacity", 0.75, "", "0,255,0,64" },
            { "G3_stop_to_only", 0.25, "", "191,0,64,255" },
            { "G3_stop_to_only", 0.5, "", "128,0,128,255" },
            { "G3_stop_to_only", 0.75, "", "64,0,191,255" },
            { "G4_stop_to_only_rule", 0.25, "", "191,0,64,255" },
            { "G4_stop_to_only_rule", 0.5, "", "128,0,128,255" },
            { "G4_stop_to_only_rule", 0.75, "", "64,0,191,255" },
            { "G5_stop_initial", 0.25, "", "0,0,64,255" },
            { "G5_stop_initial", 0.5, "", "0,0,128,255" },
            { "G5_stop_initial", 0.75, "", "0,0,191,255" },
            { "N1_other_element_untouched", 0.25, "fill=191,0,64,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N1_other_element_untouched", 0.5, "fill=128,0,128,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N1_other_element_untouched", 0.75, "fill=64,0,191,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0|fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N2_unknown_keyframes", 0.25, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N2_unknown_keyframes", 0.5, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N2_unknown_keyframes", 0.75, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N3_none", 0.25, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N3_none", 0.5, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
            { "N3_none", 0.75, "fill=0,255,0,255;stroke=none;fo=1;so=1;op=1;sw=1;do=0;tr=1,0,0,1,0,0", "" },
        };

        private static IEnumerable<SvgElement> Flatten(IEnumerable<SvgElement> elements)
        {
            foreach (var element in elements)
            {
                yield return element;

                if (element is SvgGroupElement group)
                {
                    foreach (var child in Flatten(group.Children)) yield return child;
                }
            }
        }

        private static string Paint(SvgPaint paint) => paint.Kind == SvgPaintKind.Solid ? $"{paint.PaintColor.R},{paint.PaintColor.G},{paint.PaintColor.B},{paint.PaintColor.A}" : paint.Kind == SvgPaintKind.None ? "none" : paint.Kind.ToString();

        private static bool SameColour(string expected, string actual)
        {
            if (expected == actual) return true;
            if (expected == "none" || actual == "none") return false;

            var e = expected.Split(',').Select(double.Parse).ToArray();
            var a = actual.Split(',').Select(double.Parse).ToArray();

            // Low alpha is compared premultiplied: the 8-bit straight colour of a nearly transparent paint is noise.
            if (e[3] < 40 || a[3] < 40)
                return Enumerable.Range(0, 3).All(i => Math.Abs(e[i] * e[3] / 255 - a[i] * a[3] / 255) <= 2.5) && Math.Abs(e[3] - a[3]) <= 2.5;

            return Enumerable.Range(0, 4).All(i => Math.Abs(e[i] - a[i]) <= 2.5);
        }

        [Theory]
        [MemberData(nameof(ChromeValues))]
        public async Task MatchesChrome(string name, double progress, string shapes, string stops)
        {
            var (css, body) = Cases[name];
            var document = await SvgCssAnimationTests.BuildAsync(css, body, progress);

            var actualShapes = Flatten(document.Children).Where(e => e is not SvgGroupElement).ToList();
            var expectedShapes = shapes.Length == 0 ? [] : shapes.Split('|');
            Assert.Equal(expectedShapes.Length, actualShapes.Count);

            for (var i = 0; i < expectedShapes.Length; i++)
            {
                var expected = expectedShapes[i].Split(';').ToDictionary(part => part[..part.IndexOf('=')], part => part[(part.IndexOf('=') + 1)..]);
                var shape = actualShapes[i];
                var context = $"{name} at {progress}, shape {i}";

                Assert.True(SameColour(expected["fill"], Paint(shape.Fill)), $"{context}: fill Chrome {expected["fill"]}, got {Paint(shape.Fill)}");
                Assert.True(SameColour(expected["stroke"], Paint(shape.Stroke)), $"{context}: stroke Chrome {expected["stroke"]}, got {Paint(shape.Stroke)}");

                void Number(string key, double actual, double tolerance) =>
                    Assert.True(Math.Abs(double.Parse(expected[key], CultureInfo.InvariantCulture) - actual) <= tolerance, $"{context}: {key} Chrome {expected[key]}, got {actual}");

                Number("fo", shape.FillOpacity, 0.02);
                Number("so", shape.StrokeOpacity, 0.02);
                Number("op", shape.Opacity, 0.02);
                Number("sw", shape.StrokeWidth, 0.05);
                Number("do", shape.StrokeDashOffset, 0.05);

                var want = expected["tr"].Split(',').Select(x => double.Parse(x, CultureInfo.InvariantCulture)).ToArray();
                var m = shape.Transform ?? System.Numerics.Matrix3x2.Identity;
                var got = new double[] { m.M11, m.M12, m.M21, m.M22, m.M31, m.M32 };
                Assert.True(want.Zip(got, (w, g) => Math.Abs(w - g) <= 0.03 * Math.Max(1, Math.Abs(w))).All(ok => ok), $"{context}: transform Chrome {expected["tr"]}, got {string.Join(",", got)}");
            }

            var actualStops = document.Gradients.Values.SelectMany(g => g.Stops).ToList();
            var expectedStops = stops.Length == 0 ? [] : stops.Split('|');
            Assert.Equal(expectedStops.Length, actualStops.Count);

            for (var i = 0; i < expectedStops.Length; i++)
            {
                var got = $"{actualStops[i].PaintColor.R},{actualStops[i].PaintColor.G},{actualStops[i].PaintColor.B},{actualStops[i].PaintColor.A}";
                Assert.True(SameColour(expectedStops[i], got), $"{name} at {progress}, stop {i}: Chrome {expectedStops[i]}, got {got}");
            }
        }
    }
}
