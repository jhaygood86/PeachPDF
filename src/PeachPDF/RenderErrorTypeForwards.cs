using System.Runtime.CompilerServices;

// The exception a render failure surfaces as, and its error-type enum, are thrown from deep inside the HTML core, so they live in PeachBrackets
// (still in the PeachPDF namespace, so every existing catch clause and reference compiles and binds unchanged). These forwards keep code built
// against PeachPDF.dll resolving them.
[assembly: TypeForwardedTo(typeof(PeachPDF.HtmlRenderException))]
[assembly: TypeForwardedTo(typeof(PeachPDF.HtmlRenderErrorType))]
