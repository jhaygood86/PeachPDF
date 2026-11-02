using System;
using System.Collections.Generic;
using System.Text;
using PeachBrackets.Svg;

namespace PeachBrackets.Html.Animation
{
    /// <summary>
    /// The mix of two <c>d</c> values (the <c>path()</c> function, or the attribute's own data string): two paths of the same commands are mixed number
    /// by number (SVG 2 section 9.4, CSS Shapes 1 <c>path()</c>), and anything else flips. The commands have to be the same <em>type</em> - an <c>H</c> is
    /// not an <c>L</c>, an <c>S</c> not a <c>C</c> - but not the same case, nor written the same way: a relative command is taken to its absolute form first,
    /// and a pair of coordinates after a <c>M</c> is the <c>L</c> it stands for. The result is written as absolute commands.
    /// </summary>
    internal static class PathDataInterpolator
    {
        private readonly record struct Command(char Type, double[] Args);

        /// <summary>The mix of <paramref name="from"/> and <paramref name="to"/> at <paramref name="t"/> as a <c>path()</c>, or false when they cannot be mixed.</summary>
        public static bool TryMix(string from, string to, double t, out string result)
        {
            result = string.Empty;

            if (!TryRead(from, out var a) || !TryRead(to, out var b) || a.Count != b.Count) return false;

            var text = new StringBuilder("path(\"");

            for (var i = 0; i < a.Count; i++)
            {
                if (a[i].Type != b[i].Type) return false;

                if (i > 0) text.Append(' ');
                text.Append(a[i].Type);

                for (var j = 0; j < a[i].Args.Length; j++)
                {
                    var mixed = a[i].Args[j] * (1 - t) + b[i].Args[j] * t;

                    // An arc's two flags are not numbers to mix: each is the one it is nearer to, a half way mix being the 1 (as a browser rounds it).
                    if (a[i].Type == 'A' && j is 3 or 4) mixed = mixed >= 0.5 ? 1 : 0;

                    text.Append(' ').Append(CssValueInterpolator.Format(mixed));
                }
            }

            result = text.Append("\")").ToString();
            return true;
        }

        /// <summary>
        /// The commands of a <c>path("...")</c> or of bare path data, each in its absolute form. False for anything else: <c>none</c>, a <c>path()</c> that
        /// names a fill rule (which <c>d</c> does not take), or data that is empty or malformed.
        /// </summary>
        private static bool TryRead(string value, out List<Command> commands)
        {
            commands = [];

            var data = value.Trim();

            if (data.StartsWith("path(", StringComparison.OrdinalIgnoreCase))
            {
                var open = data.IndexOfAny(['"', '\'']);
                if (open < 0 || data[5..open].Trim().Length > 0) return false;

                var close = data.LastIndexOf(data[open]);
                if (close <= open || data[(close + 1)..].Trim() != ")") return false;

                data = data[(open + 1)..close];
            }

            var pos = 0;
            double curX = 0, curY = 0, startX = 0, startY = 0;
            char? command = null;

            while (true)
            {
                SvgNumberScanner.SkipSeparators(data, ref pos);
                if (pos >= data.Length) break;

                char letter;

                if (char.IsAsciiLetter(data[pos])) letter = data[pos++];
                else if (command is { } previous) letter = previous switch { 'M' => 'L', 'm' => 'l', _ => previous };
                else return false;

                // The first command has to be a moveto.
                if (commands.Count == 0 && char.ToUpperInvariant(letter) != 'M') return false;

                var relative = char.IsAsciiLetterLower(letter);
                var type = char.ToUpperInvariant(letter);

                if (type == 'Z')
                {
                    commands.Add(new Command('Z', []));
                    curX = startX;
                    curY = startY;
                    command = null;
                    continue;
                }

                var count = type switch { 'M' or 'L' or 'T' => 2, 'H' or 'V' => 1, 'C' => 6, 'S' or 'Q' => 4, 'A' => 7, _ => -1 };
                if (count < 0) return false;

                var args = new double[count];

                for (var i = 0; i < count; i++)
                {
                    if (type == 'A' && i is 3 or 4)
                    {
                        if (!SvgNumberScanner.TryReadFlag(data, ref pos, out var flag)) return false;
                        args[i] = flag ? 1 : 0;
                    }
                    else if (!SvgNumberScanner.TryReadNumber(data, ref pos, out args[i]))
                    {
                        return false;
                    }
                }

                if (relative)
                {
                    switch (type)
                    {
                        case 'H': args[0] += curX; break;
                        case 'V': args[0] += curY; break;
                        case 'A': args[5] += curX; args[6] += curY; break;
                        default:
                            for (var i = 0; i < count; i += 2)
                            {
                                args[i] += curX;
                                args[i + 1] += curY;
                            }

                            break;
                    }
                }

                switch (type)
                {
                    case 'H': curX = args[0]; break;
                    case 'V': curY = args[0]; break;
                    default:
                        curX = args[count - 2];
                        curY = args[count - 1];
                        break;
                }

                if (type == 'M')
                {
                    startX = curX;
                    startY = curY;
                }

                commands.Add(new Command(type, args));
                command = letter;
            }

            return commands.Count > 0;
        }
    }
}
