using Acornima;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Jint.Runtime.Descriptors;
using Microsoft.Extensions.Logging;
using PeachBrackets.ScriptHosting;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace PeachBrackets.Scripting.Internal;

/// <summary>One document's JavaScript: an engine with a window and the DOM of the parsed page, the scripts run in order, and a virtual clock for timers.</summary>
internal sealed partial class ScriptRuntime
{
    private readonly JintScriptOptions _options;
    private readonly Dictionary<ScriptNode, NodeObject> _wrappers = [];
    private readonly HashSet<ScriptNode> _ran = [];
    private bool _reporting;

    public ScriptRuntime(ScriptDocument document, JintScriptOptions options, CancellationToken cancellationToken)
    {
        Document = document;
        document.LiveMediaPosition = LiveMediaPosition;
        _options = options;
        _cancellation = cancellationToken;

        Engine = new Engine(engine =>
        {
            if (options.ScriptTimeout > TimeSpan.Zero) engine.TimeoutInterval(options.ScriptTimeout);
            if (options.MaxRecursionDepth > 0) engine.LimitRecursion(options.MaxRecursionDepth);
            if (options.MemoryLimitBytes > 0) engine.LimitMemory(options.MemoryLimitBytes);
            engine.CancellationToken(cancellationToken);
        });

        InstallPrototypes();
        Window = InstallWindow();

        // Extends the CSS object the window just defined, so it comes after it.
        Engine.Execute(ColorScripts.Source);
        Engine.Execute(AtomicsScripts.Source);
        InstallHostApis();
    }

    public Engine Engine { get; }

    public ScriptDocument Document { get; }

    public WindowObject Window { get; }

    public NodeObject DocumentObject => Wrap(Document.Root);

    /// <summary>The script that is running, for <c>document.currentScript</c>.</summary>
    public ScriptNode? CurrentScript { get; private set; }

    public string ReadyState { get; private set; } = "loading";

    public NodeObject Wrap(ScriptNode node)
    {
        if (_wrappers.TryGetValue(node, out var existing))
            return existing;

        var prototype = node.Kind switch
        {
            ScriptNodeKind.Document => node.OwnerDocument.IsHtml || _plainDocuments.Contains(node.OwnerDocument) ? DocumentPrototype : XmlDocumentPrototype,
            ScriptNodeKind.Element => ElementPrototypeFor(node.TagName),
            ScriptNodeKind.Text => node.IsCDataSection ? CDataPrototype : TextPrototype,
            ScriptNodeKind.Comment => CommentPrototype,
            ScriptNodeKind.ProcessingInstruction => ProcessingInstructionPrototype,
            ScriptNodeKind.DocumentType => DocumentTypePrototype,
            _ => FragmentPrototype,
        };

        WatchDocument(node.OwnerDocument);
        var wrapper = new NodeObject(this, node, prototype);
        _wrappers[node] = wrapper;
        return wrapper;
    }

    public JsValue WrapOrNull(ScriptNode? node) => node is null ? JsValue.Null : Wrap(node);

    public JsValue WrapList(IEnumerable<ScriptNode> nodes, bool collection = false)
    {
        var list = (ObjectInstance)Js.Array(Engine, [.. nodes.Select(n => (JsValue)Wrap(n))]);
        list.Prototype = collection ? HtmlCollectionPrototype : NodeListPrototype;
        return list;
    }

    /// <summary>Writes to the options' logger, if there is one.</summary>
    public void WriteLog(LogLevel level, EventId id, string text) => _options.Logger?.Log(level, id, text);

    public Exception TypeError(string message) => new JavaScriptException(Engine.Intrinsics.TypeError, message);

    private static bool IsScriptFailure(Exception error) =>
        error is JavaScriptException or ParseErrorException or TimeoutException or RecursionDepthOverflowException or StatementsCountOverflowException
            or MemoryLimitExceededException or InvalidOperationException or ArgumentException;

    public void ReportError(Exception error)
    {
        var text = error switch
        {
            JavaScriptException js => "Uncaught " + js.Message + (js.Location.Start.Line > 0 ? $" (line {js.Location.Start.Line})" : string.Empty),
            ParseErrorException parse => "SyntaxError: " + parse.Message,
            _ => error.GetType().Name + ": " + error.Message,
        };

        // A worker's error is the page's to hear of as an error event on the Worker object; the log gets it as information.
        WriteLog(IsWorker ? LogLevel.Information : LogLevel.Error, IsWorker ? ScriptLogEvents.WorkerFailed : ScriptLogEvents.ScriptFailed, text);
        ForwardErrorToParent(text, error is JavaScriptException thrownError ? thrownError.Error : JsValue.Undefined);
        if (_reporting)
            return;

        var detail = error is JavaScriptException thrown ? thrown.Error : JsValue.Undefined;
        var errorEvent = CreateEvent("error", false, true);
        errorEvent.FastSetProperty("message", new PropertyDescriptor(Js.Str(text), PropertyFlag.AllForbidden));
        errorEvent.FastSetProperty("error", new PropertyDescriptor(detail, PropertyFlag.AllForbidden));
        _reporting = true;
        try
        {
            DispatchEvent(Window, errorEvent);
        }
        finally
        {
            _reporting = false;
        }
    }

    /// <summary>Runs a function the page gave, reporting what it throws instead of letting it end the run.</summary>
    public JsValue? Call(JsValue function, JsValue self, params JsValue[] args)
    {
        try
        {
            var result = Engine.Call(function, self, args);
            RunMicrotasks();
            return result;
        }
        catch (Exception error) when (IsScriptFailure(error))
        {
            ReportError(error);
            return null;
        }
    }

    public void RunMicrotasks()
    {
        try
        {
            Engine.Advanced.ProcessTasks();
        }
        catch (Exception error) when (IsScriptFailure(error))
        {
            ReportError(error);
        }
    }

    private void Execute(string code, string source)
    {
        try
        {
            Engine.Execute(code, source);
            RunMicrotasks();
        }
        catch (Exception error) when (IsScriptFailure(error))
        {
            ReportError(error);
        }
    }

    /// <summary>Runs the document's scripts, fires its load events and plays the timers until the page settles.</summary>
    public async ValueTask RunAsync()
    {
        // The host's scripts run first, whether or not the document's own do.
        foreach (var userScript in _options.UserScripts)
            Execute(userScript.Code, userScript.Name);

        // Scripts that are not deferred run in document order; one a script adds is found by looking again each time.
        if (_options.RunAuthorScripts)
        {
            await RunPendingScriptsAsync(deferred: false);
            await RunPendingScriptsAsync(deferred: true);
        }

        ReadyState = "interactive";
        FireEvent(DocumentObject, "readystatechange", bubbles: false, cancelable: false);
        FireEvent(DocumentObject, "DOMContentLoaded", bubbles: true, cancelable: false);
        RunMicrotasks();

        await Document.LoadFontFacesAsync();
        await RunFontLoadsAsync();

        await LoadInitialImagesAsync();
        RunMicrotasks();
        LoadInitialFrames();
        RunMicrotasks();

        ReadyState = "complete";
        FireEvent(DocumentObject, "readystatechange", bubbles: false, cancelable: false);
        FireEvent(Window, "load", bubbles: false, cancelable: false);
        RunMicrotasks();

        // The canvases that make their children drawable paint once the page has loaded.
        StartInitialCanvasPaints();
        StartInitialMedia();
        await PlayTimersAsync();
        ReportPendingWork();
    }

    private async ValueTask RunPendingScriptsAsync(bool deferred)
    {
        while (true)
        {
            var next = Document.Scripts.FirstOrDefault(s => !_ran.Contains(s) && IsDeferred(s) == deferred && IsRunnable(s));
            if (next is null)
                return;

            _ran.Add(next);
            string? code;
            var source = "inline";
            if (next.GetAttribute("src") is { Length: > 0 } src)
            {
                if (!_options.LoadExternalScripts)
                    continue;

                source = src;
                code = await Document.LoadTextAsync(src);
                if (code is null)
                {
                    WriteLog(LogLevel.Error, ScriptLogEvents.ScriptLoadFailed, $"The script '{src}' could not be loaded.");
                    FireEvent(Wrap(next), "error", bubbles: false, cancelable: false);
                    continue;
                }
            }
            else
            {
                code = next.TextContent;
            }

            CurrentScript = next;
            try
            {
                Execute(code, source);
            }
            finally
            {
                CurrentScript = null;
            }

            if (next.HasAttribute("src"))
                FireEvent(Wrap(next), "load", bubbles: false, cancelable: false);
        }
    }

    private static bool IsDeferred(ScriptNode script) => script.HasAttribute("src") && (script.HasAttribute("defer") || script.HasAttribute("async"));

    private bool IsRunnable(ScriptNode script)
    {
        if (!script.IsConnected || script.ScriptAlreadyStarted)
            return false;

        var type = script.GetAttribute("type")?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(type))
            return script.GetAttribute("language") is not { } language || language.Contains("javascript", StringComparison.OrdinalIgnoreCase);

        if (type == "module")
            return _options.RunModuleScripts;

        return type is "text/javascript" or "application/javascript" or "application/x-javascript" or "text/ecmascript" or "application/ecmascript"
            or "text/jscript" or "text/livescript" or "text/x-javascript";
    }
}
