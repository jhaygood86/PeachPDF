#nullable enable

using MimeKit;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    public class MimeKitNetworkLoader : RNetworkLoader
    {
        private readonly Task<MimeMessage> _messageTask;
        private MimeMessage? _message = null;

        public override RUri? BaseUri => null;

        public MimeKitNetworkLoader(Stream stream)
        {
            MimeParser parser = new(stream);
            _messageTask = parser.ParseMessageAsync();
        }

        public override async Task<string> GetPrimaryContents()
        {
            _message ??= await _messageTask;

            return _message.HtmlBody;
        }

        public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            _message ??= await _messageTask;

            MimePart? part = null;

            part = _message.BodyParts
                .OfType<MimePart>()
                .FirstOrDefault(x => x.ContentLocation == uri.Uri || x.Headers["Content-Location"] == uri.OriginalString);

            var stream = part?.Content.Open();
            var headers = part?.Headers.ToDictionary(x => x.Field, x => new[] { x.Value });

            return new RNetworkResponse(stream, headers);
        }
    }
}
