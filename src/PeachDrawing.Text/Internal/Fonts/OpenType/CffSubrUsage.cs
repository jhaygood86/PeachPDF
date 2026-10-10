using System;
using System.Collections.Generic;

namespace PeachDrawing.Text.Internal.Fonts.OpenType
{
    /// <summary>
    /// Which subroutines a set of charstrings can reach.
    /// </summary>
    /// <remarks>
    /// A CJK font factors its repeated strokes into subroutines, and they are
    /// most of what is left once the unused outlines are gone -- several
    /// megabytes of a Noto CJK face. They can only be dropped once it is known
    /// which ones the glyphs still in the font call, directly or through
    /// another subroutine.
    ///
    /// This walks a charstring for its calls and nothing else: no outline is
    /// built and no operator is executed. It only has to track the operand
    /// stack accurately enough to know the number on top when a call happens,
    /// and to step over the two operators whose length is not implied by their
    /// operands.
    ///
    /// Anything unexpected abandons the scan. A subroutine wrongly thought
    /// unused would be dropped and the glyph that calls it would be drawn
    /// wrongly, so the only safe answer to "I am not sure" is to keep
    /// everything.
    /// </remarks>
    internal sealed class CffSubrUsage
    {
        private const int MaxDepth = 10;

        private readonly IReadOnlyList<byte[]> _globalSubrs;
        private readonly Func<int, IReadOnlyList<byte[]>> _localSubrsFor;

        private HashSet<int> _global = [];
        private readonly Dictionary<int, HashSet<int>> _local = [];

        private int _currentFd;
        private int _hintCount;
        private int _stackDepth;
        private double _top;
        private bool _hasTop;

        public CffSubrUsage(IReadOnlyList<byte[]> globalSubrs, Func<int, IReadOnlyList<byte[]>> localSubrsFor)
        {
            _globalSubrs = globalSubrs;
            _localSubrsFor = localSubrsFor;
        }

        /// <summary>Whether every charstring was understood.</summary>
        public bool Complete { get; private set; } = true;

        public IReadOnlySet<int> GlobalSubrs => _global;

        public IReadOnlySet<int> LocalSubrsForFd(int fd) =>
            _local.TryGetValue(fd, out var used) ? used : (IReadOnlySet<int>)new HashSet<int>();

        public void Add(ReadOnlySpan<byte> charstring, int fd)
        {
            _currentFd = fd;
            _hintCount = 0;
            _stackDepth = 0;
            _hasTop = false;

            if (!_local.ContainsKey(fd))
                _local[fd] = [];

            Walk(charstring, 0);
        }

        /// <summary>The number a subroutine index is offset by, per the spec.</summary>
        private static int Bias(int count) => count switch
        {
            < 1240 => 107,
            < 33900 => 1131,
            _ => 32768,
        };

        private void Walk(ReadOnlySpan<byte> code, int depth)
        {
            if (depth > MaxDepth)
            {
                Complete = false;
                return;
            }

            var pos = 0;

            while (pos < code.Length)
            {
                if (!Complete)
                    return;

                int b0 = code[pos];

                if (b0 >= 32 || b0 == 28)
                {
                    pos += PushNumber(code, pos);
                    continue;
                }

                pos++;

                switch (b0)
                {
                    case 1:  // hstem
                    case 3:  // vstem
                    case 18: // hstemhm
                    case 23: // vstemhm
                        // Each hint takes two operands.
                        _hintCount += _stackDepth / 2;
                        Clear();
                        break;

                    case 19: // hintmask
                    case 20: // cntrmask
                        // Operands still on the stack are an implicit vstem.
                        _hintCount += _stackDepth / 2;
                        Clear();
                        pos += (Math.Max(_hintCount, 1) + 7) / 8;
                        break;

                    case 10: // callsubr
                        if (!Call(_localSubrsFor(_currentFd), _local[_currentFd], depth))
                            return;
                        break;

                    case 29: // callgsubr
                        if (!Call(_globalSubrs, _global, depth))
                            return;
                        break;

                    case 11: // return
                        return;

                    case 14: // endchar
                        Clear();
                        return;

                    case 12: // escape: a two byte operator, none of which call
                        if (pos >= code.Length)
                        {
                            Complete = false;
                            return;
                        }

                        pos++;
                        Clear();
                        break;

                    default:
                        // Every other operator consumes its operands.
                        Clear();
                        break;
                }
            }
        }

        private bool Call(IReadOnlyList<byte[]> subrs, HashSet<int> used, int depth)
        {
            if (!_hasTop)
            {
                Complete = false;
                return false;
            }

            var index = (int)_top + Bias(subrs.Count);

            // The call consumes the index it was given.
            _stackDepth = Math.Max(0, _stackDepth - 1);
            _hasTop = false;

            if (index < 0 || index >= subrs.Count)
            {
                Complete = false;
                return false;
            }

            if (used.Add(index))
                Walk(subrs[index], depth + 1);

            return Complete;
        }

        private void Clear()
        {
            _stackDepth = 0;
            _hasTop = false;
        }

        private int PushNumber(ReadOnlySpan<byte> code, int pos)
        {
            int b0 = code[pos];

            switch (b0)
            {
                case 28:
                    _top = (short)((code[pos + 1] << 8) | code[pos + 2]);
                    Pushed();
                    return 3;

                case >= 32 and <= 246:
                    _top = b0 - 139;
                    Pushed();
                    return 1;

                case >= 247 and <= 250:
                    _top = ((b0 - 247) * 256) + code[pos + 1] + 108;
                    Pushed();
                    return 2;

                case >= 251 and <= 254:
                    _top = (-(b0 - 251) * 256) - code[pos + 1] - 108;
                    Pushed();
                    return 2;

                case 255:
                    // 16.16 fixed; only its whole part could name a subroutine.
                    _top = (short)((code[pos + 1] << 8) | code[pos + 2]);
                    Pushed();
                    return 5;

                default:
                    Complete = false;
                    return code.Length;
            }
        }

        private void Pushed()
        {
            _stackDepth++;
            _hasTop = true;
        }
    }
}
