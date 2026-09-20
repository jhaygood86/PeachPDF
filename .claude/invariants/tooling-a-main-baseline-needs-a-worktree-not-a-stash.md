# A "before" measurement needs a worktree, not `git stash`

To compare behaviour against `main` from a feature branch, check `main` out somewhere else:

```bash
git worktree add /tmp/mainwt main
```

**`git stash` only reverts uncommitted changes.** On a branch that has already committed part of its
work, stashing reverts the rest and leaves everything committed in place — so the "before" column is
really the branch again, minus a bit. It is worst for a *deleted* file: `git stash -- src/Foo` cannot
bring back `src/Foo/Bar.cs` if `Bar.cs`'s deletion is in a commit, and nothing says so.

## The symptom to recognise

**The two columns come out identical, or nearly so.** That reads as "no regression, confirmed" and is
actually the tell that nothing was reverted. A comparison worth running is one you expect to differ
somewhere; if it differs nowhere, suspect the baseline before believing the result.

The second symptom is a baseline that is internally inconsistent — in the case that produced this file,
a "main" render in which some `<hr>`s painted through a painter that had already been deleted and
others did not.

## What it cost

Issue #1225 shipped two false conclusions from stash-based baselines in one change, each of which
survived a careful read of the diff because the numbers looked like evidence:

- "A default `<hr>` is unchanged" — it was doubly darkened, `#9a9a9a` → `#464646`, which is the
  commonest `<hr>` on the web. Caught in review.
- "The old null valueless-attribute value did not crash anything" — it threw on 22 of 39 probed
  snippets on real `main`. Also caught in review.

Both came back clean because both baselines were the branch.

## When it does work

`git stash` is fine for a before/after that touches only files whose changes are *uncommitted*, which
is the common case mid-edit. The rule is about the moment a branch has commits on it: from then on, a
baseline comes from a worktree.
