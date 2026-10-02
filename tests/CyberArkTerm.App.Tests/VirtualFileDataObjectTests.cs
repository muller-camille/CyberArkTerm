using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using CyberArkTerm.App.Services;
using CyberArkTerm.Core.Ssh;
using ComDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace CyberArkTerm.App.Tests;

/// <summary>
/// Données glissées vers l'Explorateur, vues comme l'Explorateur les lit (interface COM) : description pendant le
/// glissement, contenu seulement au dépôt, téléchargé une seule fois.
/// </summary>
public class VirtualFileDataObjectTests
{
    [Fact]
    public void ContentIsFetchedOnlyWhenTheExplorerAsksForIt()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var local = Path.GetTempFileName();
        File.WriteAllText(local, "contenu distant", Encoding.UTF8);
        try
        {
            int fetches = 0;
            VirtualFile[] files = [new("logs", true, 0, default), new(@"logs\a.txt", false, 18, DateTime.UtcNow)];
            ComDataObject data = new VirtualFileDataObject(files, () =>
            {
                fetches++;
                return new string?[] { null, local };
            });

            // Pendant le glissement : description et effet préféré, sans téléchargement.
            var descriptor = Format(VirtualFileDataObject.DescriptorFormat, TYMED.TYMED_HGLOBAL);
            Assert.Equal(0, data.QueryGetData(ref descriptor));
            data.GetData(ref descriptor, out var medium);
            Assert.Equal(2, Marshal.ReadInt32(medium.unionmember));
            ReleaseStgMedium(ref medium);

            var effect = Format(VirtualFileDataObject.PreferredDropEffectFormat, TYMED.TYMED_HGLOBAL);
            data.GetData(ref effect, out medium);
            Assert.Equal(1, Marshal.ReadInt32(medium.unionmember));
            ReleaseStgMedium(ref medium);

            var enumerator = data.EnumFormatEtc(DATADIR.DATADIR_GET);
            var formats = new FORMATETC[8];
            var count = new int[1];
            enumerator.Next(formats.Length, formats, count);
            Assert.Equal(3, count[0]);
            Assert.Equal(0, fetches);

            // Au dépôt : contenu du fichier, téléchargé une fois ; un dossier n'a pas de contenu.
            var contents = Format(VirtualFileDataObject.ContentsFormat, TYMED.TYMED_ISTREAM, lindex: 1);
            data.GetData(ref contents, out medium);
            Assert.Equal("contenu distant", ReadStream(medium.unionmember));
            ReleaseStgMedium(ref medium);
            data.GetData(ref contents, out medium);
            ReleaseStgMedium(ref medium);
            Assert.Equal(1, fetches);

            var folder = Format(VirtualFileDataObject.ContentsFormat, TYMED.TYMED_ISTREAM, lindex: 0);
            Assert.Throws<COMException>(() => data.GetData(ref folder, out _));
        }
        finally
        {
            File.Delete(local);
        }
    }

    [Fact]
    public void CancelledDownloadIsReportedAndNotRetried()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        int fetches = 0;
        ComDataObject data = new VirtualFileDataObject([new("a.txt", false, 1, default)], () =>
        {
            fetches++;
            return null;
        });

        var contents = Format(VirtualFileDataObject.ContentsFormat, TYMED.TYMED_ISTREAM, lindex: 0);
        var first = Assert.Throws<COMException>(() => data.GetData(ref contents, out _));
        Assert.Equal(unchecked((int)0x80004004), first.HResult);
        Assert.Throws<COMException>(() => data.GetData(ref contents, out _));
        Assert.Equal(1, fetches);
    }

    private static FORMATETC Format(short format, TYMED tymed, int lindex = -1) => new()
    {
        cfFormat = format,
        dwAspect = DVASPECT.DVASPECT_CONTENT,
        lindex = lindex,
        tymed = tymed,
    };

    private static string ReadStream(IntPtr pointer)
    {
        var stream = (IStream)Marshal.GetObjectForIUnknown(pointer);
        var buffer = new byte[256];
        var read = Marshal.AllocHGlobal(sizeof(int));
        try
        {
            stream.Read(buffer, buffer.Length, read);
            return Encoding.UTF8.GetString(buffer, 0, Marshal.ReadInt32(read)).TrimStart('﻿');
        }
        finally
        {
            Marshal.FreeHGlobal(read);
        }
    }

    [DllImport("ole32.dll")]
    private static extern void ReleaseStgMedium(ref STGMEDIUM medium);
}
