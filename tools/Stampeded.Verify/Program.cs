using System.ComponentModel;
using System.Text.Json;
using System.Xml;
using Stampeded.Verify;

try
{
    switch (args)
    {
        case ["ffi-version", var root]:
            Console.WriteLine(UpstreamVerifier.FfiVersion(Path.GetFullPath(root)));
            break;
        case ["ffi-checksum", var root]:
            Console.WriteLine(UpstreamVerifier.FfiChecksum(Path.GetFullPath(root)));
            break;
        case ["refresh-ffi", var root, var index]:
            UpstreamVerifier.RefreshFfi(Path.GetFullPath(root), Path.GetFullPath(index));
            break;
        case ["prepare-upstream", var root, var metadata]:
            UpstreamVerifier.Prepare(Path.GetFullPath(root), Path.GetFullPath(metadata));
            break;
        case ["upstream", var root]:
            UpstreamVerifier.Verify(Path.GetFullPath(root));
            break;
        case ["package", var root, var package]:
            UpstreamVerifier.Verify(Path.GetFullPath(root));
            PackageVerifier.Verify(Path.GetFullPath(root), Path.GetFullPath(package));
            break;
        default:
            Console.Error.WriteLine("Usage: Stampeded.Verify ffi-version|ffi-checksum <repository> | prepare-upstream <repository> <cargo-metadata.json> | upstream <repository> | package <repository> <package.nupkg>");
            return 2;
    }
    return 0;
}
catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException
    or InvalidOperationException or JsonException or XmlException or Win32Exception)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
