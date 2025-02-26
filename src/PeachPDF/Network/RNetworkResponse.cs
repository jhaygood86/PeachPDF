using System.Collections.Generic;
using System.IO;

namespace PeachPDF.Network
{
    public record RNetworkResponse(Stream? ResourceStream, Dictionary<string, string[]>? ResponseHeaders);
}
