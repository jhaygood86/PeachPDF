using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    internal abstract class Selectors : StylesheetNode, IEnumerable<ISelector>
    {
        protected readonly List<ISelector> _selectors;
        private int[]? _matchOrder;

        protected Selectors()
        {
            _selectors = new List<ISelector>();
        }

        /// <summary>
        /// Indices into <see cref="_selectors"/>, cheapest-to-evaluate first per
        /// <see cref="SelectorMatchCost"/>. Built once per selector instance and cached - selector cost
        /// is static, so the same order applies to every node this selector is ever tested against, and
        /// this is a one-time cost per parsed selector, not a per-box cost. A benign data race on first
        /// use (two threads both computing it) is fine: the computation is pure and idempotent, and
        /// <see cref="_selectors"/> is frozen after CSS parsing completes (<see cref="Add"/>/
        /// <see cref="Remove"/> are only ever called by <c>SelectorConstructor</c> while building the
        /// selector, before any matching can occur), so there is nothing to invalidate.
        /// </summary>
        internal int[] MatchOrder => _matchOrder ??= BuildMatchOrder();

        private int[] BuildMatchOrder()
        {
            var order = new int[_selectors.Count];
            var cost = new int[_selectors.Count];
            for (var i = 0; i < order.Length; i++)
            {
                order[i] = i;
                cost[i] = SelectorMatchCost.Of(_selectors[i]);
            }
            // Sorting the parallel cost array (rather than a Comparison<int> delegate re-deriving each
            // selector's cost on every comparison) computes SelectorMatchCost.Of exactly once per
            // selector instead of O(n log n) times; Array.Sort applies the same permutation to order.
            Array.Sort(cost, order);
            return order;
        }

        public virtual Priority Specificity
        {
            get
            {
                var sum = new Priority();

                return _selectors.Aggregate(sum, (current, t) => current + t.Specificity);
            }
        }

        public string Text => this.ToCss();
        public int Length => _selectors.Count;

        public ISelector this[int index]
        {
            get => _selectors[index];
            set => _selectors[index] = value;
        }

        public void Add(ISelector selector)
        {
            _selectors.Add(selector);
        }

        public void Remove(ISelector selector)
        {
            _selectors.Remove(selector);
        }

        public IEnumerator<ISelector> GetEnumerator()
        {
            return _selectors.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}