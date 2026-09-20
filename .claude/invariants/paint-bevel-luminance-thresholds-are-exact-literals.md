# A bevel's luminance thresholds are exact literals compared strictly

`BorderBevelColors`' two constants are not tuning knobs. Each is the relative luminance of a specific
gray, and each comparison is written the way Blink's `CalculateInsetOutsetColor` writes it:

```csharp
if (luminance <= NearBlackLuminance)                 // 0.014443844  == lum(rgb(32,32,32))
    ...both faces lighten...
return luminance > NearWhiteLuminance ? color : ...; // 0.83077      == lum(rgb(235,235,235))
```

**Do not round either literal to fewer digits, and do not swap `<=`/`>` for the other inclusivity.**
The boundary grays clear their constants by almost nothing:

| gray | luminance | constant | decides |
| --- | --- | --- | --- |
| 32 | 0.014443843596 | 0.014443844 | inside by 4e-10 → both faces lighten |
| 33 | 0.015208514423 | 0.014443844 | outside → darkens |
| 235 | 0.830769876775 | 0.83077 | inside by 1.2e-7 → still lightens |
| 236 | 0.838799011741 | 0.83077 | outside → keeps the declared color |

Shortening `0.014443844` to `0.0144` flips gray 32 to darkening; relaxing `>` to `>=` at the top end
does not flip 235 by itself, but any shortening of `0.83077` will.

## Why this is easy to break silently

A bevel is two colors, and most of the suite asserts a painted face against `BorderBevelColors.Shade`
on the other side of the comparison — those tests cannot fail when the rule moves. The assertions that
actually guard the boundaries pin literal `RColor`s:
`BorderBevelColors_NearBlack_LightensBothFacesRatherThanVanishing` and
`BorderBevelColors_NearWhite_KeepsTheDeclaredColorOnTheLitFace` in `BorderStylePaintIntegrationTests`.
Keep the 32/33 and 235/236 pairs in them.

## Do not substitute a contrast ratio

An earlier implementation approximated the near-black threshold as
`ContrastRatio(color, Dark(color)) < 1.3`. It reproduces the 32/33 gray boundary exactly, so it looks
right, but it disagrees with Chrome on 23,328 very dark chromatic colors (`#001e4c` being
representative: luminance just above the threshold, contrast ratio just below 1.3). The 1.75 contrast
ratio that does appear in Blink's source belongs to the pre-M149 branch and decides the *lit* face,
not the dark one.
