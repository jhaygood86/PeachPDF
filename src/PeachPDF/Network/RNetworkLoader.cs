﻿#nullable enable

using System.IO;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    public abstract class RNetworkLoader
    {
        public abstract Task<string> GetPrimaryContents();
        public abstract Task<Stream?> GetResourceStream(RUri uri);
        public abstract RUri? BaseUri { get; }
    }
}
