namespace PeachBrackets.Updates;

/// <summary>What is wrong with the value of a form control (<see cref="HtmlDocument.GetValidationProblems"/>), in the terms of the HTML constraint validation API.</summary>
public enum ValidationKind
{
    /// <summary>A required control has no value (<c>valueMissing</c>).</summary>
    ValueMissing,

    /// <summary>The value is not of the kind the control's type asks for: an email address, a URL, a number (<c>typeMismatch</c>, <c>badInput</c>).</summary>
    TypeMismatch,

    /// <summary>The value does not match the <c>pattern</c> (<c>patternMismatch</c>).</summary>
    PatternMismatch,

    /// <summary>The value is shorter than <c>minlength</c> (<c>tooShort</c>).</summary>
    TooShort,

    /// <summary>The value is longer than <c>maxlength</c> (<c>tooLong</c>).</summary>
    TooLong,

    /// <summary>The value is less than <c>min</c> (<c>rangeUnderflow</c>).</summary>
    RangeUnderflow,

    /// <summary>The value is more than <c>max</c> (<c>rangeOverflow</c>).</summary>
    RangeOverflow,

    /// <summary>The value is not a multiple of <c>step</c> from its base (<c>stepMismatch</c>).</summary>
    StepMismatch,
}

/// <summary>One control whose value does not satisfy its constraints.</summary>
/// <param name="Element">the control</param>
/// <param name="Kind">what is wrong</param>
/// <param name="Message">a sentence to show the user, as a browser would</param>
public readonly record struct ValidationProblem(DocumentElement Element, ValidationKind Kind, string Message);
