#nullable enable

using MimeKit;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Network
{
    /// <summary>
    /// An <see cref="RNetworkLoader"/> backed by a self-contained MHTML archive (what Chrome calls a "single
    /// page document"). The root HTML and every resource it references (stylesheets, images, fonts) are read
    /// from the archive's MIME parts, matched by their <c>Content-Location</c>, so no network or file-system
    /// access is needed to render the document.
    /// </summary>
    public class MimeKitNetworkLoader : RNetworkLoader
    {
        private readonly Task<MimeMessage> _messageTask;
        private MimeMessage? _message = null;

        /// <inheritdoc/>
        public override RUri? BaseUri => null;

        /// <summary>
        /// Creates a loader that reads the MHTML archive from <paramref name="stream"/>.
        /// </summary>
        /// <param name="stream">A stream positioned at the start of a MIME/MHTML archive. The caller owns the stream.</param>
        public MimeKitNetworkLoader(Stream stream)
        {
            MimeParser parser = new(stream);
            _messageTask = parser.ParseMessageAsync();
        }

        /// <inheritdoc/>
        public override async Task<string> GetPrimaryContents()
        {
            _message ??= await _messageTask;

            return _message.HtmlBody ?? string.Empty;
        }

        /// <inheritdoc/>
        public override async Task<RNetworkResponse?> GetResourceStream(RUri uri)
        {
            _message ??= await _messageTask;

            MimePart? part = null;

            part = _message.BodyParts
                .OfType<MimePart>()
                .FirstOrDefault(x => x.ContentLocation == uri.Uri || x.Headers["Content-Location"] == uri.OriginalString);

            var stream = part?.Content?.Open();
            var headers = part?.Headers.ToDictionary(x => x.Field, x => new[] { x.Value });

            return new RNetworkResponse(stream, headers);
        }
    }
}
