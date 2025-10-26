// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Entities;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.Html.Core.Parse
{
    /// <summary>
    /// Parse CSS properties values like numbers, Urls, etc.
    /// </summary>
    internal sealed class CssValueParser
    {
        #region Fields and Consts

        /// <summary>
        /// 
        /// </summary>
        private readonly RAdapter _adapter;

        #endregion


        /// <summary>
        /// Init.
        /// </summary>
        public CssValueParser(RAdapter adapter)
        {
            ArgumentNullException.ThrowIfNull(adapter, "global");

            _adapter = adapter;
        }

        /// <summary>
        /// Check if the given substring is a valid double number.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>true - valid double number, false - otherwise</returns>
        public static bool IsFloat(string str, int idx, int length)
        {
            if (length < 1)
                return false;

            bool sawDot = false;
            for (int i = 0; i < length; i++)
            {
                if (str[idx + i] == '.')
                {
                    if (sawDot)
                        return false;
                    sawDot = true;
                }
                else if (!char.IsDigit(str[idx + i]))
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Check if the given substring is a valid double number.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>true - valid int number, false - otherwise</returns>
        public static bool IsInt(string str, int idx, int length)
        {
            if (length < 1)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (!char.IsDigit(str[idx + i]))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Check if the given string is a valid length value.
        /// </summary>
        /// <param name="value">the string value to check</param>
        /// <returns>true - valid, false - invalid</returns>
        public static bool IsValidLength(string value)
        {
            if (value.Length <= 1) return false;

            var number = string.Empty;

            if (value.EndsWith('%'))
            {
                number = value[..^1];
            }
            else if (value.Length > 2)
            {
                number = value[..^2];
            }

            return double.TryParse(number, out _);
        }

        /// <summary>
        /// Evals a number and returns it. If number is a percentage, it will be multiplied by <see cref="hundredPercent"/>
        /// </summary>
        /// <param name="number">Number to be parsed</param>
        /// <param name="hundredPercent">Number that represents the 100% if parsed number is a percentage</param>
        /// <returns>Parsed number. Zero if error while parsing.</returns>
        public static double ParseNumber(string number, double hundredPercent)
        {
            if (string.IsNullOrEmpty(number))
            {
                return 0f;
            }

            var toParse = number;
            var isPercent = number.EndsWith('%');

            if (isPercent)
                toParse = number[..^1];

            if (!double.TryParse(toParse, NumberStyles.Number, NumberFormatInfo.InvariantInfo, out var result))
            {
                return 0f;
            }

            if (isPercent)
            {
                result = (result / 100f) * hundredPercent;
            }

            return result;
        }

        /// <summary>
        /// Parses a length. Lengths are followed by an unit identifier (e.g. 10px, 3.1em)
        /// </summary>
        /// <param name="length">Specified length</param>
        /// <param name="hundredPercent">Equivalent to 100 percent when length is percentage</param>
        /// <param name="fontAdjust">if the length is in pixels and the length is font related it needs to use 72/96 factor</param>
        /// <param name="box"></param>
        /// <returns>the parsed length value with adjustments</returns>
        public static double ParseLength(string length, double hundredPercent, CssBoxProperties box, bool fontAdjust = false)
        {
            return ParseLength(length, hundredPercent, box.GetEmHeight(), box.GetRemHeight(), null, fontAdjust, false);
        }

        /// <summary>
        /// Parses a length. Lengths are followed by an unit identifier (e.g. 10px, 3.1em)
        /// </summary>
        /// <param name="length">Specified length</param>
        /// <param name="hundredPercent">Equivalent to 100 percent when length is percentage</param>
        /// <param name="emFactor"></param>
        /// <param name="remFactor"></param>
        /// <param name="defaultUnit"></param>
        /// <param name="fontAdjust">if the length is in pixels and the length is font related it needs to use 72/96 factor</param>
        /// <param name="returnPoints">Allows the return double to be in points. If false, result will be pixels</param>
        /// <returns>the parsed length value with adjustments</returns>
        public static double ParseLength(string length, double hundredPercent, double emFactor, double remFactor, string? defaultUnit, bool fontAdjust, bool returnPoints)
        {
            //Return zero if no length specified, zero specified
            if (string.IsNullOrEmpty(length) || length == "0")
                return 0f;

            //Get units of the length
            var (unit, numberValue) = GetUnit(length, defaultUnit, out var hasUnit);

            //Factor will depend on the unit
            double factor;

            //Number of the length
            var number = hasUnit ? numberValue : ParseNumber(length, hundredPercent);

            //TODO: Units behave different in paper and in screen!
            switch (unit)
            {
                case CssConstants.Em:
                    factor = emFactor;
                    break;
                case CssConstants.Rem:
                    factor = remFactor;
                    break;
                case CssConstants.Ex:
                    factor = emFactor / 2;
                    break;
                case CssConstants.Px:
                    factor = fontAdjust ? 72f / 96f : 1f; //TODO:a check support for hi dpi
                    break;
                case CssConstants.Mm:
                    factor = 3.779527559f; //3 pixels per millimeter
                    break;
                case CssConstants.Cm:
                    factor = 37.795275591f; //37 pixels per centimeter
                    break;
                case CssConstants.In:
                    factor = 96f; //96 pixels per inch
                    break;
                case CssConstants.Pt:
                    factor = 96f / 72f; // 1 point = 1/72 of inch

                    if (returnPoints)
                    {
                        return number!.Value;
                    }

                    break;
                case CssConstants.Pc:
                    factor = 16f; // 1 pica = 12 points
                    break;
                case CssConstants.Percent:
                    factor = hundredPercent / 100d;
                    break;
                default:
                    factor = 0f;
                    break;
            }

            return factor * number!.Value;
        }

        /// <summary>
        /// Get the unit to use for the length, use default if no unit found in length string.
        /// </summary>
        private static (string? unit, double? value) GetUnit(string length, string? defaultUnit, out bool hasUnit)
        {
            var tokens = GetCssTokens(length);

            if (tokens is [UnitToken unitToken])
            {
                hasUnit = true;
                return (unitToken.Unit, unitToken.Value);
            }

            hasUnit = false;
            return (defaultUnit, null);
        }

        /// <summary>
        /// Check if the given color string value is valid.
        /// </summary>
        /// <param name="colorValue">color string value to parse</param>
        /// <returns>true - valid, false - invalid</returns>
        public bool IsColorValid(string colorValue)
        {
            return TryGetColor(colorValue, 0, colorValue.Length, out _);
        }

        /// <summary>
        /// Parses a color value in CSS style; e.g. #ff0000, red, rgb(255,0,0), rgb(100%, 0, 0)
        /// </summary>
        /// <param name="colorValue">color string value to parse</param>
        /// <returns>Color value</returns>
        public RColor GetActualColor(string colorValue)
        {
            TryGetColor(colorValue, 0, colorValue.Length, out var color);
            return color;
        }

        /// <summary>
        /// Parses a color value in CSS style; e.g. #ff0000, RED, RGB(255,0,0), RGB(100%, 0, 0)
        /// </summary>
        /// <param name="str">color substring value to parse</param>
        /// <param name="idx">substring start idx </param>
        /// <param name="length">substring length</param>
        /// <param name="color">return the parsed color</param>
        /// <returns>true - valid color, false - otherwise</returns>
        public bool TryGetColor(string str, int idx, int length, out RColor color)
        {
            try
            {
                if (!string.IsNullOrEmpty(str))
                {
                    return length switch
                    {
                        > 1 when str[idx] == '#' => GetColorByHex(str, idx, length, out color),
                        > 10 when CommonUtils.SubStringEquals(str, idx, 4, "rgb(") && str[length - 1] == ')' =>
                            GetColorByRgb(str, idx, length, out color),
                        > 13 when CommonUtils.SubStringEquals(str, idx, 5, "rgba(") && str[length - 1] == ')' =>
                            GetColorByRgba(str, idx, length, out color),
                        _ => GetColorByName(str, idx, length, out color)
                    };
                }
            }
            catch
            { }
            color = RColor.Black;
            return false;
        }

        /// <summary>
        /// Parses a border value in CSS style; e.g. 1px, 1, thin, thick, medium
        /// </summary>
        /// <param name="borderValue"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        public static double GetActualBorderWidth(string borderValue, CssBoxProperties b)
        {
            if (string.IsNullOrEmpty(borderValue))
            {
                return GetActualBorderWidth(CssConstants.Medium, b);
            }

            return borderValue switch
            {
                CssConstants.Thin => 1f,
                CssConstants.Medium => 2f,
                CssConstants.Thick => 4f,
                _ => Math.Abs(ParseLength(borderValue, 1, b))
            };
        }

        public string GetFontFamilyByName(string propValue)
        {
            int start = 0;
            while (start < propValue.Length)
            {
                while (char.IsWhiteSpace(propValue[start]) || propValue[start] == ',' || propValue[start] == '\'' || propValue[start] == '"')
                    start++;
                var end = propValue.IndexOf(',', start);
                if (end < 0)
                    end = propValue.Length;
                var adjEnd = end - 1;
                while (char.IsWhiteSpace(propValue[adjEnd]) || propValue[adjEnd] == '\'' || propValue[adjEnd] == '"')
                    adjEnd--;

                var font = propValue.Substring(start, adjEnd - start + 1);

                if (_adapter.IsFontExists(font))
                {
                    return font;
                }

                start = end;
            }

            return CssConstants.Inherit;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="propValue">the value of the property to parse</param>
        /// <returns>parsed value</returns>
        public static CssImage? GetImagePropertyValue(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            var urlToken = tokens.OfType<UrlToken>().SingleOrDefault();

            return urlToken is not null ? CssImage.GetUrl(urlToken.Data) : null;
        }

        public static CssFontFace GetFontFacePropertyValue(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            var urlToken = tokens.OfType<UrlToken>().SingleOrDefault();
            var formatToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "format");
            var techToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "tech");
            var localToken = tokens.OfType<FunctionToken>().SingleOrDefault(x => x.Data == "local");

            return new CssFontFace(urlToken?.Data, formatToken?.ArgumentTokens?.FirstOrDefault()?.Data, techToken?.ArgumentTokens?.FirstOrDefault()?.Data, localToken?.ArgumentTokens?.FirstOrDefault()?.Data);
        }

        public static List<Token> GetCssTokens(string propValue)
        {
            var lexer = new Lexer(propValue);

            List<Token> tokens = [];

            Token token;

            do
            {
                token = lexer.Get();

                if (token.Type != TokenType.EndOfFile && token.Type != TokenType.Whitespace)
                {
                    tokens.Add(token);
                }

            } while (token.Type != TokenType.EndOfFile);

            return tokens;
        }

        public static string GetFontFaceFamilyName(string propValue)
        {
            var tokens = GetCssTokens(propValue);

            if (tokens is [StringToken stringToken])
            {
                return stringToken.Data;
            }

            return propValue;
        }

        #region Private methods

        /// <summary>
        /// Get color by parsing given hex value color string (#A28B34).
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByHex(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;
            if (length == 7)
            {
                r = ParseHexInt(str, idx + 1, 2);
                g = ParseHexInt(str, idx + 3, 2);
                b = ParseHexInt(str, idx + 5, 2);
            }
            else if (length == 4)
            {
                r = ParseHexInt(str, idx + 1, 1);
                r = r * 16 + r;
                g = ParseHexInt(str, idx + 2, 1);
                g = g * 16 + g;
                b = ParseHexInt(str, idx + 3, 1);
                b = b * 16 + b;
            }
            if (r > -1 && g > -1 && b > -1)
            {
                color = RColor.FromArgb(r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by parsing given RGB value color string (RGB(255,180,90))
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByRgb(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;

            if (length > 10)
            {
                int s = idx + 4;
                r = ParseIntAtIndex(str, ref s);
                if (s < idx + length)
                {
                    g = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    b = ParseIntAtIndex(str, ref s);
                }
            }

            if (r > -1 && g > -1 && b > -1)
            {
                color = RColor.FromArgb(r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by parsing given RGBA value color string (RGBA(255,180,90,180))
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private static bool GetColorByRgba(string str, int idx, int length, out RColor color)
        {
            int r = -1;
            int g = -1;
            int b = -1;
            int a = -1;

            if (length > 13)
            {
                int s = idx + 5;
                r = ParseIntAtIndex(str, ref s);

                if (s < idx + length)
                {
                    g = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    b = ParseIntAtIndex(str, ref s);
                }
                if (s < idx + length)
                {
                    a = ParseIntAtIndex(str, ref s);
                }
            }

            if (r > -1 && g > -1 && b > -1 && a > -1)
            {
                color = RColor.FromArgb(a, r, g, b);
                return true;
            }
            color = RColor.Empty;
            return false;
        }

        /// <summary>
        /// Get color by given name, including .NET name.
        /// </summary>
        /// <returns>true - valid color, false - otherwise</returns>
        private bool GetColorByName(string str, int idx, int length, out RColor color)
        {
            color = _adapter.GetColor(str.Substring(idx, length));
            return color.A > 0;
        }

        /// <summary>
        /// Parse the given decimal number string to positive int value.<br/>
        /// Start at given <paramref name="startIdx"/>, ignore whitespaces and take
        /// as many digits as possible to parse to int.
        /// </summary>
        /// <param name="str">the string to parse</param>
        /// <param name="startIdx">the index to start parsing at</param>
        /// <returns>parsed int or 0</returns>
        private static int ParseIntAtIndex(string str, ref int startIdx)
        {
            int len = 0;
            while (char.IsWhiteSpace(str, startIdx))
                startIdx++;
            while (char.IsDigit(str, startIdx + len))
                len++;
            var val = ParseInt(str, startIdx, len);
            startIdx = startIdx + len + 1;
            return val;
        }

        /// <summary>
        /// Parse the given decimal number string to positive int value.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>int value, -1 if not valid</returns>
        private static int ParseInt(string str, int idx, int length)
        {
            if (length < 1)
                return -1;

            int num = 0;
            for (int i = 0; i < length; i++)
            {
                int c = str[idx + i];
                if (!(c >= 48 && c <= 57))
                    return -1;

                num = num * 10 + c - 48;
            }
            return num;
        }

        /// <summary>
        /// Parse the given hex number string to positive int value.
        /// Assume given substring is not empty and all indexes are valid!<br/>
        /// </summary>
        /// <returns>int value, -1 if not valid</returns>
        private static int ParseHexInt(string str, int idx, int length)
        {
            if (length < 1)
                return -1;

            int num = 0;
            for (int i = 0; i < length; i++)
            {
                int c = str[idx + i];
                if (!(c >= 48 && c <= 57) && !(c >= 65 && c <= 70) && !(c >= 97 && c <= 102))
                    return -1;

                num = num * 16 + (c <= 57 ? c - 48 : (10 + c - (c <= 70 ? 65 : 97)));
            }
            return num;
        }

        #endregion
    }
}