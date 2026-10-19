#nullable disable

using System.Collections.Frozen;
using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal static class PortNumbers
    {
        private static readonly FrozenDictionary<string, string> Ports = new Dictionary<string, string>
        {
            {ProtocolNames.Http, "80"},
            {ProtocolNames.Https, "443"},
            {ProtocolNames.Ftp, "21"},
            {ProtocolNames.File, ""},
            {ProtocolNames.Ws, "80"},
            {ProtocolNames.Wss, "443"},
            {ProtocolNames.Gopher, "70"},
            {ProtocolNames.Telnet, "23"},
            {ProtocolNames.Ssh, "22"}
        }.ToFrozenDictionary();

        public static string GetDefaultPort(string protocol)
        {
            Ports.TryGetValue(protocol, out var value);
            return value;
        }
    }
}