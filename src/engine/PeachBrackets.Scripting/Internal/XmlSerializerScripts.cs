namespace PeachBrackets.Scripting.Internal;

/// <summary>
/// <c>XMLSerializer</c> and the XML serialization of the DOM Parsing standard, in script over the DOM objects: namespace prefix maps, prefix
/// generation, the namespace declarations an element needs, and the well-formedness checks of the <c>require well-formed</c> flag. A fragment of
/// <see cref="WebPlatformScripts.Bundle"/> put where <see cref="WebPlatformScripts.XmlSerializerMarker"/> stands.
/// </summary>
internal static class XmlSerializerScripts
{
    public const string Source = """
            // ---------------------------------------------------------------- XMLSerializer
            var XML_NS = "http://www.w3.org/XML/1998/namespace", XMLNS_NS = "http://www.w3.org/2000/xmlns/", XHTML_NS = "http://www.w3.org/1999/xhtml";
            var voidElements = /^(area|base|basefont|bgsound|br|col|embed|frame|hr|img|input|keygen|link|menuitem|meta|param|source|track|wbr)$/;
            var xmlNameStart = "A-Z_a-z\\u00C0-\\u00D6\\u00D8-\\u00F6\\u00F8-\\u02FF\\u0370-\\u037D\\u037F-\\u1FFF\\u200C-\\u200D\\u2070-\\u218F\\u2C00-\\u2FEF\\u3001-\\uD7FF\\uF900-\\uFDCF\\uFDF0-\\uFFFD";
            var xmlLocalName = new RegExp("^[" + xmlNameStart + "][" + xmlNameStart + "\\\-.0-9\\u00B7\\u0300-\\u036F\\u203F-\\u2040]*$");
            var xmlChars = /^(?:[\t\n\r -퟿-�]|[\uD800-\uDBFF][\uDC00-\uDFFF])*$/;
            function xmlError() { return new DOMException("The node could not be serialized as well-formed XML.", "InvalidStateError"); }

            function PrefixMap() { this.entries = new Map(); }
            PrefixMap.prototype.copy = function () { var c = new PrefixMap(); this.entries.forEach(function (list, ns) { c.entries.set(ns, list.slice()); }); return c; };
            PrefixMap.prototype.has = function (ns, prefix) { var list = this.entries.get(ns); return !!list && list.indexOf(prefix) >= 0; };
            PrefixMap.prototype.add = function (ns, prefix) { var list = this.entries.get(ns); if (list) list.push(prefix); else this.entries.set(ns, [prefix]); };
            // The prefix to use for a namespace: the preferred one when the map knows it, else the latest one added.
            PrefixMap.prototype.preferred = function (ns, preferred) {
                var list = this.entries.get(ns);
                if (!list) return null;
                if (preferred !== null && list.indexOf(preferred) >= 0) return preferred;
                return list[list.length - 1];
            };
            function generatePrefix(map, ns, index) {
                var prefix = "ns" + index.value;
                index.value++;
                map.add(ns, prefix);
                return prefix;
            }
            function escapeText(text) { return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }
            function escapeAttribute(text) {
                return text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/\t/g, "&#9;").replace(/\n/g, "&#10;").replace(/\r/g, "&#13;");
            }

            function serializeXmlNode(node, contextNs, map, index, wf) {
                switch (node.nodeType) {
                    case 1: return serializeXmlElement(node, contextNs, map, index, wf);
                    case 9: return serializeXmlChildren(node, contextNs, map, index, wf);
                    case 11: return serializeXmlChildren(node, contextNs, map, index, wf);
                    case 10: {
                        if (wf && (!/^[ \r\na-zA-Z0-9\-'()+,./:=?;!*#@$_%]*$/.test(node.publicId) || node.publicId.indexOf("\u0000") >= 0)) throw xmlError();
                        if (wf && (node.systemId.indexOf('"') >= 0 && node.systemId.indexOf("'") >= 0)) throw xmlError();
                        var out = "<!DOCTYPE " + node.name;
                        if (node.publicId !== "") out += ' PUBLIC "' + node.publicId + '"';
                        else if (node.systemId !== "") out += " SYSTEM";
                        if (node.systemId !== "") out += ' "' + node.systemId + '"';
                        return out + ">";
                    }
                    case 3: if (wf && !xmlChars.test(node.data)) throw xmlError(); return escapeText(node.data);
                    case 4: {
                        if (wf && (node.data.indexOf("]]>") >= 0 || !xmlChars.test(node.data))) throw xmlError();
                        return "<![CDATA[" + node.data + "]]>";
                    }
                    case 8: {
                        if (wf && (!xmlChars.test(node.data) || node.data.indexOf("--") >= 0 || node.data.charAt(node.data.length - 1) === "-")) throw xmlError();
                        return "<!--" + node.data + "-->";
                    }
                    case 7: {
                        if (wf && (node.target.indexOf(":") >= 0 || /^xml$/i.test(node.target) || !xmlChars.test(node.data) || node.data.indexOf("?>") >= 0)) throw xmlError();
                        return "<?" + node.target + (node.data === "" ? "" : " " + node.data) + "?>";
                    }
                    default: return "";
                }
            }
            function serializeXmlChildren(node, contextNs, map, index, wf) {
                var out = "";
                for (var child = node.firstChild; child; child = child.nextSibling) out += serializeXmlNode(child, contextNs, map, index, wf);
                return out;
            }
            function serializeXmlElement(node, contextNs, map, index, wf) {
                var ns = node.namespaceURI === undefined ? null : node.namespaceURI;
                if (wf && (node.localName.indexOf(":") >= 0 || !xmlLocalName.test(node.localName))) throw xmlError();
                var markup = "<", qualifiedName = "", skipEndTag = false, ignoreNsDefinition = false;
                var localMap = map.copy(), localPrefixes = Object.create(null);
                // Record the namespace declarations the element carries.
                var attrs = Array.prototype.slice.call(node.attributes);
                var localDefault = null;
                attrs.forEach(function (attr) {
                    if (attr.namespaceURI !== XMLNS_NS) return;
                    if (attr.prefix === null && attr.localName === "xmlns") { localDefault = attr.value; return; }
                    if (attr.prefix === "xmlns") {
                        var definition = attr.value === "" ? null : attr.value;
                        if (!localMap.has(definition, attr.localName)) { localMap.add(definition, attr.localName); localPrefixes[attr.localName] = attr.value; }
                    }
                });
                var inherited = contextNs, nsAttribute = "";
                if (inherited === ns) {
                    if (localDefault !== null) ignoreNsDefinition = true;
                    qualifiedName = node.localName;
                    markup += qualifiedName;
                } else {
                    var prefix = node.prefix;
                    var candidate = localMap.preferred(ns, prefix);
                    if (candidate !== null) {
                        qualifiedName = candidate + ":" + node.localName;
                        if (localDefault !== null && localDefault !== XML_NS) inherited = localDefault === "" ? null : localDefault;
                        markup += qualifiedName;
                    } else if (prefix !== null) {
                        if (prefix in localPrefixes) prefix = generatePrefix(localMap, ns, index);
                        else localMap.add(ns, prefix);
                        qualifiedName = prefix + ":" + node.localName;
                        markup += qualifiedName;
                        nsAttribute = ' xmlns:' + prefix + '="' + escapeAttribute(ns === null ? "" : ns) + '"';
                        if (localDefault !== null) inherited = localDefault === "" ? null : localDefault;
                    } else if (localDefault === null || localDefault !== ns) {
                        ignoreNsDefinition = true;
                        qualifiedName = node.localName;
                        inherited = ns;
                        markup += qualifiedName + ' xmlns="' + escapeAttribute(ns === null ? "" : ns) + '"';
                    } else {
                        qualifiedName = node.localName;
                        inherited = ns;
                        markup += qualifiedName;
                    }
                }
                markup += nsAttribute;
                markup += serializeXmlAttributes(attrs, localMap, index, localPrefixes, ignoreNsDefinition, wf);
                if (ns === XHTML_NS && !node.firstChild && voidElements.test(node.localName)) { markup += " /"; skipEndTag = true; }
                else if (ns !== XHTML_NS && !node.firstChild) { markup += "/"; skipEndTag = true; }
                markup += ">";
                if (skipEndTag) return markup;
                var children = ns === XHTML_NS && node.localName === "template" && node.content ? node.content : node;
                markup += serializeXmlChildren(children, inherited, localMap, index, wf);
                return markup + "</" + qualifiedName + ">";
            }
            function serializeXmlAttributes(attrs, map, index, localPrefixes, ignoreNsDefinition, wf) {
                var result = "", seen = Object.create(null);
                attrs.forEach(function (attr) {
                    if (wf) {
                        var key = (attr.namespaceURI === null ? "" : attr.namespaceURI) + "|" + attr.localName;
                        if (seen[key]) throw xmlError();
                        seen[key] = true;
                    }
                    var candidate = null, ns = attr.namespaceURI;
                    if (ns === null && attr.localName === "xmlns" && !wf) return;
                    if (ns !== null) {
                        candidate = map.preferred(ns, attr.prefix);
                        if (ns === XMLNS_NS) {
                            if (attr.value === XML_NS || (attr.prefix === null && ignoreNsDefinition) || (attr.prefix !== null && (localPrefixes[attr.localName] !== attr.value || !map.has(attr.value === "" ? null : attr.value, attr.localName)))) return;
                            if (wf && attr.value === XMLNS_NS) throw xmlError();
                            if (wf && attr.value === "" ) throw xmlError();
                            if (attr.prefix === "xmlns") candidate = "xmlns";
                        } else if (candidate === null) {
                            candidate = generatePrefix(map, ns, index);
                            result += ' xmlns:' + candidate + '="' + escapeAttribute(ns) + '"';
                        }
                    }
                    result += " ";
                    if (candidate !== null) result += candidate + ":";
                    if (wf && (attr.localName.indexOf(":") >= 0 || !xmlLocalName.test(attr.localName) || (attr.localName === "xmlns" && ns === null))) throw xmlError();
                    if (wf && !xmlChars.test(attr.value)) throw xmlError();
                    result += attr.localName + '="' + escapeAttribute(attr.value) + '"';
                });
                return result;
            }
            function serializeXml(node, wf) {
                var map = new PrefixMap();
                map.add(XML_NS, "xml");
                return serializeXmlNode(node, null, map, { value: 1 }, wf);
            }
            function XMLSerializer() {
                if (new.target === undefined) throw new TypeError("Failed to construct 'XMLSerializer': Please use the 'new' operator, this DOM object constructor cannot be called as a function.");
            }
            method(XMLSerializer.prototype, "serializeToString", function serializeToString(root) {
                if (arguments.length < 1) throw new TypeError("Failed to execute 'serializeToString' on 'XMLSerializer': 1 argument required, but only 0 present.");
                if (!(root instanceof Node)) throw new TypeError("Failed to execute 'serializeToString' on 'XMLSerializer': parameter 1 is not of type 'Node'.");
                return serializeXml(root, false);
            });
            tag(XMLSerializer, "XMLSerializer");
            net.serializeXml = serializeXml;

        """;
}
