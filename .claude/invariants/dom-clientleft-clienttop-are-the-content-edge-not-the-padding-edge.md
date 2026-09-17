# `ClientLeft`/`ClientTop` are the CONTENT edge, not the padding edge

    public double ClientLeft => Location.X + ActualBorderLeftWidth + ActualPaddingLeft;
    public double ClientTop  => Location.Y + ActualBorderTopWidth  + ActualPaddingTop;

Border **and** padding. The padding edge is `Location.X + ActualBorderLeftWidth`.

## The measured symptom

An absolutely positioned box placed with `top: 0; left: 0` inside a `position: relative` ancestor
with `padding: 30pt 40pt; border: 10pt` landed at **(70, 60)** where Chrome 141 puts it at
**(30, 30)** — off by exactly the ancestor's `padding-left` and `padding-top`, because the offset was
added to `ClientLeft`/`ClientTop` on the belief that those were the padding edge. `right`/`bottom`
did not go through that expression and were correct, so the same box disagreed with itself depending
on which pair of offsets placed it. Issue #1160.

## Why it is easy to get wrong

The name says otherwise to anyone who knows the DOM: `element.clientLeft` there is the **border
width**, and the client rect is the **padding box**. So `ClientLeft` reads as "the padding edge" and
is not. The code that had this wrong carried a comment asserting the wrong reading in so many words —
"the containing block's PADDING edge (ClientLeft/ClientTop — inside the border)" — which is how it
went unquestioned.

## The rule

Anything that wants CSS's **containing block** of a positioned box — CSS 2.1 §10.1's padding box —
must not use `ClientLeft`/`ClientTop`. Use `Location + ActualBorder*Width`. `ClientLeft`/`ClientTop`
are right for laying out **in-flow content**, which genuinely starts at the content edge, and that is
what their other callers want.

## Why a green suite will not catch it

Every fixture in `AbsolutePositioningIntegrationTests` set `padding: 0` on the positioned ancestor,
which makes the padding edge and the content edge the same point and the two readings
indistinguishable. A fixture for anything that resolves against a box edge needs **non-zero padding
and non-zero border, with different values**, or it cannot tell the three edges apart.
