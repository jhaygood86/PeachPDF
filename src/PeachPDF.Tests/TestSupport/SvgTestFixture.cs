namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// An original, hand-authored SVG "badge" graphic (not copied from any external source) used to
    /// exercise the v1 SVG feature set end to end: viewBox, nested &lt;g&gt; with both
    /// <c>transform</c> (matrix) and <c>opacity</c>, &lt;path&gt; with multiple subpaths (M/L/C/A/Z,
    /// including a cubic curve and an elliptical arc), &lt;circle&gt;, &lt;polygon&gt;, a
    /// &lt;linearGradient&gt; and &lt;radialGradient&gt; (both gradientUnits="userSpaceOnUse", the
    /// radial one with a gradientTransform), and a &lt;clipPath&gt; containing a &lt;use&gt; of a
    /// &lt;defs&gt; shape.
    /// </summary>
    public static class SvgTestFixture
    {
        public const string Markup = """
            <svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 200 200" width="200" height="200">
              <defs>
                <circle id="badgeCircle" cx="100" cy="100" r="80"/>
                <linearGradient id="bgGradient" gradientUnits="userSpaceOnUse" x1="20" y1="20" x2="180" y2="180">
                  <stop offset="0" style="stop-color:#4A90D9"/>
                  <stop offset="1" style="stop-color:#1B3A6B"/>
                </linearGradient>
                <radialGradient id="shineGradient" gradientUnits="userSpaceOnUse" cx="100" cy="70" r="90" gradientTransform="matrix(1 0 0 0.6 0 28)">
                  <stop offset="0" stop-color="#FFFFFF"/>
                  <stop offset="1" stop-color="#FFFFFF" stop-opacity="0"/>
                </radialGradient>
                <clipPath id="circleClip">
                  <use xlink:href="#badgeCircle"/>
                </clipPath>
              </defs>
              <g clip-path="url(#circleClip)">
                <polygon points="0,0 200,0 200,200 0,200" fill="url(#bgGradient)"/>
                <g opacity="0.5">
                  <circle cx="100" cy="70" r="90" fill="url(#shineGradient)"/>
                </g>
              </g>
              <g transform="matrix(1 0 0 1 5 5)" stroke="#FFFFFF" stroke-width="4" stroke-miterlimit="10" fill="#FFFFFF">
                <path d="M60,105 L85,130 L100,90 C110,70 130,70 140,90 A15,15 0 0 1 140,110 L100,150 L60,110 Z"/>
              </g>
              <polygon points="70,170 100,155 130,170 130,190 100,180 70,190" fill="#C0392B" stroke="#7B241C" stroke-width="2"/>
            </svg>
            """;
    }
}
