// Turns the rendered PDF into a blob URL the page can both preview and offer for download.
//
// The bytes arrive as a DotNetStreamReference rather than a base64 string, which avoids inflating a
// multi-megabyte PDF by a third on the way across the interop boundary.

export async function createPdfObjectUrl(contentStreamReference) {
    const arrayBuffer = await contentStreamReference.arrayBuffer();
    const blob = new Blob([arrayBuffer], { type: 'application/pdf' });
    return URL.createObjectURL(blob);
}

export function triggerDownload(url, fileName) {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName ?? 'document.pdf';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
}

export function revokeObjectUrl(url) {
    if (url) {
        URL.revokeObjectURL(url);
    }
}
